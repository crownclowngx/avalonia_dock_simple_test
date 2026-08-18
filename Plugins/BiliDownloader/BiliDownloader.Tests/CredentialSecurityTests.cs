using System.Security.Cryptography;
using System.Text;
using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.Tests;

public sealed class CredentialSecurityTests
{
    [Fact]
    public async Task 首次存储纪元切换会清空插件状态但不删除外部文件()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        await File.WriteAllTextAsync(Path.Combine(paths.DataDirectory, "legacy.db"), "legacy");

        var externalDirectory = Directory.GetParent(paths.RootDirectory)!.FullName;
        var externalFile = Path.Combine(externalDirectory, "keep.txt");
        await File.WriteAllTextAsync(externalFile, "keep");

        try
        {
            var initializer = new BiliLocalStateInitializer(paths);
            await initializer.InitializeAsync();
            await initializer.InitializeAsync();

            Assert.False(File.Exists(Path.Combine(paths.DataDirectory, "legacy.db")));
            Assert.True(File.Exists(paths.StorageEpochMarkerPath));
            Assert.True(File.Exists(externalFile));
        }
        finally
        {
            File.Delete(externalFile);
        }
    }

    [Fact]
    public async Task AES_GCM使用可见的每安装随机密钥并拒绝篡改()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var protector = new AesGcmCredentialProtector(new InstallationKeyStore(paths));
        var plaintext = Encoding.UTF8.GetBytes("SESSDATA=secret-value");

        try
        {
            var first = protector.Protect(plaintext);
            var second = protector.Protect(plaintext);

            Assert.False(first.Nonce.SequenceEqual(second.Nonce));
            Assert.Equal(plaintext, protector.Unprotect(first));

            var keyLines = await File.ReadAllLinesAsync(paths.CredentialKeyPath);
            Assert.Equal("BILIKEY1", keyLines[0]);
            Assert.Equal(32, Convert.FromBase64String(keyLines[1]).Length);

            first.Tag[0] ^= 0x01;
            Assert.Throws<AuthenticationTagMismatchException>(() => protector.Unprotect(first));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    [Fact]
    public async Task 凭据SQLite只保存密文且可正确恢复Cookie()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var protector = new AesGcmCredentialProtector(new InstallationKeyStore(paths));
        var store = new BiliCredentialStore(paths, protector);

        await store.SaveSessionAsync(new BiliCredentialSession(
        [
            new("SESSDATA", "top-secret-session"),
            new("bili_jct", "top-secret-csrf"),
        ], "encrypted-user", "https://example.test/encrypted-avatar"));

        var loaded = Assert.IsType<BiliCredentialSession>(await store.LoadSessionAsync());
        Assert.Contains(loaded.Cookies, cookie =>
            cookie.Name == "SESSDATA" && cookie.Value == "top-secret-session");
        Assert.Contains(loaded.Cookies, cookie =>
            cookie.Name == "bili_jct" && cookie.Value == "top-secret-csrf");
        Assert.Equal("encrypted-user", loaded.UserName);

        SqliteConnection.ClearAllPools();
        var databaseBytes = await File.ReadAllBytesAsync(paths.CredentialDatabasePath);
        var databaseText = Encoding.UTF8.GetString(databaseBytes);
        Assert.DoesNotContain("SESSDATA", databaseText, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret-session", databaseText, StringComparison.Ordinal);
        Assert.DoesNotContain("bili_jct", databaseText, StringComparison.Ordinal);
        Assert.DoesNotContain("encrypted-user", databaseText, StringComparison.Ordinal);
        Assert.DoesNotContain("encrypted-avatar", databaseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 版本1凭据可读取并在保存账号资料时升级为版本2()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var protector = new AesGcmCredentialProtector(new InstallationKeyStore(paths));
        var store = new BiliCredentialStore(paths, protector);
        await store.InitAsync();

        var legacyPlaintext = Encoding.UTF8.GetBytes(
            """{"Version":1,"Cookies":[{"Name":"SESSDATA","Value":"legacy-session"}]}""");
        try
        {
            var envelope = protector.Protect(legacyPlaintext);
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = paths.CredentialDatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
            }.ToString();
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO credential_store (id, version, nonce, ciphertext, tag)
                VALUES (1, $version, $nonce, $ciphertext, $tag);
                """;
            command.Parameters.AddWithValue("$version", envelope.Version);
            command.Parameters.AddWithValue("$nonce", envelope.Nonce);
            command.Parameters.AddWithValue("$ciphertext", envelope.Ciphertext);
            command.Parameters.AddWithValue("$tag", envelope.Tag);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(legacyPlaintext);
        }

        var legacySession = Assert.IsType<BiliCredentialSession>(await store.LoadSessionAsync());
        Assert.Null(legacySession.UserName);
        Assert.Contains(legacySession.Cookies, cookie => cookie.Value == "legacy-session");

        var sessionApi = new StubBiliSessionApi
        {
            ValidationResult = new LoginValidationResult(
                LoginValidationStatus.Valid,
                "upgraded-user",
                "upgraded-avatar"),
        };
        var state = new BiliLoginStateService(
            store,
            sessionApi,
            new IsolatedHostEventBus());
        await state.RestoreSavedSessionAsync();
        state.StartBackgroundValidation();
        await state.StopAsync();

        var upgradedSession = Assert.IsType<BiliCredentialSession>(await store.LoadSessionAsync());
        Assert.Equal("upgraded-user", upgradedSession.UserName);
        Assert.Equal("upgraded-avatar", upgradedSession.UserAvatar);
    }

    [Fact]
    public async Task 新进程可以仅从本地密文恢复登录状态()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var firstStore = new BiliCredentialStore(
            paths,
            new AesGcmCredentialProtector(new InstallationKeyStore(paths)));
        await firstStore.SaveSessionAsync(new BiliCredentialSession(
        [
            new("SESSDATA", "persistent-session"),
            new("bili_jct", "persistent-csrf"),
        ], "cached-user", "cached-avatar"));

        var reloadedStore = new BiliCredentialStore(
            paths,
            new AesGcmCredentialProtector(new InstallationKeyStore(paths)));
        var state = new BiliLoginStateService(
            reloadedStore,
            new StubBiliSessionApi(),
            new IsolatedHostEventBus());

        await state.RestoreSavedSessionAsync();

        Assert.True(state.IsLoggedIn);
        Assert.True(state.IsPersistentLogin);
        Assert.Equal("cached-user", state.UserName);
        Assert.Contains(
            "SESSDATA=persistent-session",
            new BiliCredentialProvider(state).GetCookieHeader(),
            StringComparison.Ordinal);
        Assert.Equal("正在验证已保存的登录状态…", state.StatusMessage);
    }

    [Fact]
    public async Task 全新任务表不再包含Cookie列()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DownloadTaskDatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(download_tasks);";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.DoesNotContain(columns, column =>
            string.Equals(column, "cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void 任务消息和任务模型不暴露Cookie属性()
    {
        Assert.Null(typeof(SubmitDownloadTaskMessage).GetProperty("Cookie"));
        Assert.Null(typeof(DownloadTaskRecord).GetProperty("Cookie"));
    }

    [Fact]
    public async Task 任务存储边界会脱敏错误摘要和资源URL()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var record = new DownloadTaskRecord
        {
            TaskId = "task-1",
            DocumentId = "doc-1",
            ErrorMessage = "Cookie: SESSDATA=insert-secret",
            CoverUrl = "https://i.example.test/cover.jpg?token=cover-secret#fragment",
        };

        await store.InsertBatchAsync([record]);
        await store.UpdateProgressAsync(
            record.TaskId,
            0,
            "failed",
            "https://api.example.test/play?w_rid=error-secret");
        await store.UpdateExtrasResultAsync(
            record.TaskId,
            "Authorization: Bearer extras-secret");

        var stored = Assert.Single(await store.GetByDocumentIdAsync(record.DocumentId));
        Assert.DoesNotContain("error-secret", stored.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("extras-secret", stored.ExtrasResultSummary, StringComparison.Ordinal);
        Assert.Equal("https://i.example.test/cover.jpg", stored.CoverUrl.TrimEnd('/'));
    }
}
