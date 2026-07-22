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

        await store.SaveCookiesAsync(
        [
            ("SESSDATA", "top-secret-session"),
            ("bili_jct", "top-secret-csrf"),
        ]);

        var loaded = await store.LoadCookiesAsync();
        Assert.Equal("top-secret-session", loaded["SESSDATA"]);
        Assert.Equal("top-secret-csrf", loaded["bili_jct"]);

        SqliteConnection.ClearAllPools();
        var databaseBytes = await File.ReadAllBytesAsync(paths.CredentialDatabasePath);
        var databaseText = Encoding.UTF8.GetString(databaseBytes);
        Assert.DoesNotContain("SESSDATA", databaseText, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret-session", databaseText, StringComparison.Ordinal);
        Assert.DoesNotContain("bili_jct", databaseText, StringComparison.Ordinal);
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
