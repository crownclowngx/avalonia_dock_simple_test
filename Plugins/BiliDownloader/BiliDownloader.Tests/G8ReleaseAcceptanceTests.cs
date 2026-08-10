using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BiliDownloader.Models;
using BiliDownloader.ReleaseAcceptance;
using BiliDownloader.Services.Download;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.Tests;

/// <summary>
/// G8 发布设施自身的回归测试。发布脚本不是可信根；扫描器、清单验证器和恢复统计
/// 必须能被确定性构造的泄漏与篡改样本证明会失败。
/// </summary>
public sealed class G8ReleaseAcceptanceTests
{
    [Fact]
    public async Task P1限速发布门禁在真实单调时钟上通过()
    {
        using var sandbox = new AcceptanceSandbox();
        var result = await new BandwidthLimitGate().ExecuteAsync(
            new ReleaseGateContext(sandbox.Root, null, null), default);

        Assert.True(result.Passed, result.Summary);
        Assert.True((bool)result.Metrics!["hotUpdateReleasedWaiter"]!);
        Assert.True((bool)result.Metrics["cancellationObserved"]!);
    }

    [Fact]
    public async Task 敏感扫描器同时发现文本二进制和SQLite泄漏()
    {
        using var sandbox = new AcceptanceSandbox();
        const string secret = "SESSDATA=g8-super-secret; bili_jct=csrf-secret";
        await File.WriteAllTextAsync(
            Path.Combine(sandbox.Root, "leak.log"),
            """
            Authorization: Bearer live-token
            SESSDATA=reusable-value
            https://example.invalid/media?quality=16&w_rid=signed-value
            """);
        await File.WriteAllBytesAsync(
            Path.Combine(sandbox.Root, "payload.bin"),
            // 二进制里只泄漏单个 Cookie 对，证明扫描不是仅匹配完整环境变量字符串。
            Encoding.UTF8.GetBytes("prefix:bili_jct=csrf-secret:suffix"));

        var database = Path.Combine(sandbox.Root, "leak.db");
        await using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE state(id INTEGER, Cookie TEXT); INSERT INTO state VALUES(1, 'x');";
            await command.ExecuteNonQueryAsync();
        }

        var result = await new SensitiveEvidenceScanner().ScanAsync([sandbox.Root], secret, default);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, issue => issue.Rule == "authorization-header");
        Assert.Contains(result.Issues, issue => issue.Rule == "reusable-cookie-value");
        Assert.Contains(result.Issues, issue => issue.Rule == "signed-url-query");
        Assert.Contains(result.Issues, issue => issue.Rule == "exact-live-secret");
        Assert.Contains(result.Issues, issue => issue.Rule.StartsWith("sensitive-column:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 敏感扫描器允许密文与明确脱敏文本()
    {
        using var sandbox = new AcceptanceSandbox();
        const string secret = "SESSDATA=must-not-appear";
        await File.WriteAllTextAsync(
            Path.Combine(sandbox.Root, "safe.json"),
            "{\"message\":\"Authorization=[REDACTED]，普通错误\"}");
        await File.WriteAllBytesAsync(
            Path.Combine(sandbox.Root, "cipher.bin"),
            RandomNumberGenerator.GetBytes(128));

        var result = await new SensitiveEvidenceScanner().ScanAsync([sandbox.Root], secret, default);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task 真实持久化证据以密文保存Cookie且扫描任务库日志与Document()
    {
        using var sandbox = new AcceptanceSandbox();
        const string secret = "SESSDATA=g8-secret; bili_jct=g8-csrf";
        var paths = new AcceptanceDataPaths(Path.Combine(sandbox.Root, "live"));
        var context = new ReleaseGateContext(sandbox.Root, "BV1xx411c7mD", secret);
        context.Items["data-paths"] = paths;

        var gate = await new LivePersistenceEvidenceGate().ExecuteAsync(context, default);
        var scan = await new SensitiveEvidenceScanner().ScanAsync([sandbox.Root], secret, default);

        Assert.True(gate.Passed);
        Assert.True(scan.Passed);
        Assert.True(File.Exists(paths.DownloadTaskDatabasePath));
        Assert.True(File.Exists(paths.CredentialDatabasePath));
        Assert.True(File.Exists(Path.Combine(paths.DataDirectory, "g8-document-v2.json")));
        Assert.True(File.Exists(Path.Combine(paths.LogDirectory, "g8-acceptance.log")));
    }

    [Fact]
    public async Task 包验证器接受封闭win_x64清单并拒绝额外RID()
    {
        using var sandbox = new AcceptanceSandbox();
        var validPackage = await CreatePackageAsync(sandbox.Root, includeLinuxRid: false);
        var valid = await new ReleaseGatePipeline([new PackageVerificationGate(validPackage)])
            .ExecuteAsync(new ReleaseGateContext(Path.Combine(sandbox.Root, "valid-check"), null, null), default);
        Assert.True(valid.Passed);

        var invalidPackage = await CreatePackageAsync(sandbox.Root, includeLinuxRid: true);
        var invalid = await new ReleaseGatePipeline([new PackageVerificationGate(invalidPackage)])
            .ExecuteAsync(new ReleaseGateContext(Path.Combine(sandbox.Root, "invalid-check"), null, null), default);
        Assert.False(invalid.Passed);
    }

    [Theory]
    [InlineData("undeclared")]
    [InlineData("hash")]
    [InlineData("shared")]
    [InlineData("traversal")]
    public async Task 包验证器拒绝封闭集合摘要宿主边界和路径穿越破坏(string mutation)
    {
        using var sandbox = new AcceptanceSandbox();
        var package = await CreatePackageAsync(
            sandbox.Root,
            includeLinuxRid: false,
            mutation: mutation);

        var result = await new ReleaseGatePipeline([new PackageVerificationGate(package)])
            .ExecuteAsync(new ReleaseGateContext(Path.Combine(sandbox.Root, "check"), null, null), default);

        Assert.False(result.Passed);
    }

    [Fact]
    public async Task 一百个离线恢复样本全部以磁盘事实修正字节数()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var recovery = new DownloadRecoveryService(repository);
        using var sandbox = new AcceptanceSandbox();

        for (var index = 0; index < 100; index++)
        {
            var taskRoot = Path.Combine(sandbox.Root, index.ToString("D3"));
            Directory.CreateDirectory(taskRoot);
            var expectedVideo = 32 + index;
            var expectedAudio = 16 + index % 7;
            if (index % 2 == 0)
            {
                await File.WriteAllBytesAsync(Path.Combine(taskRoot, "video.tmp"), new byte[expectedVideo]);
            }
            else
            {
                await File.WriteAllBytesAsync(Path.Combine(taskRoot, "video.tmp.chunk0"), new byte[index]);
                await File.WriteAllBytesAsync(
                    Path.Combine(taskRoot, "video.tmp.chunk1"),
                    new byte[expectedVideo - index]);
            }
            await File.WriteAllBytesAsync(Path.Combine(taskRoot, "audio.tmp"), new byte[expectedAudio]);

            var task = new DownloadTaskRecord
            {
                TaskId = $"g8-{index:D3}",
                TempDirectory = taskRoot,
                ExpectedVideoBytes = expectedVideo,
                ExpectedAudioBytes = expectedAudio,
                VideoBytesDownloaded = index + 500,
                AudioBytesDownloaded = index + 500,
            };
            repository.Seed(task);

            await recovery.ReconcileAsync(task);

            Assert.Equal(expectedVideo, task.VideoBytesDownloaded);
            Assert.Equal(expectedAudio, task.AudioBytesDownloaded);
        }
    }

    private static async Task<string> CreatePackageAsync(
        string root,
        bool includeLinuxRid,
        string? mutation = null)
    {
        var suffix = mutation ?? (includeLinuxRid ? "invalid" : "valid");
        var stage = Path.Combine(root, $"stage-{suffix}");
        foreach (var relative in PackageVerificationGate.RequiredPayloadFiles)
        {
            var path = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, $"payload:{relative}");
        }
        if (includeLinuxRid)
        {
            Directory.CreateDirectory(Path.Combine(stage, "runtimes", "linux-x64", "native"));
            await File.WriteAllTextAsync(
                Path.Combine(stage, "runtimes", "linux-x64", "native", "libe_sqlite3.so"),
                "linux");
        }
        if (mutation == "shared")
            await File.WriteAllTextAsync(Path.Combine(stage, "Avalonia.Base.dll"), "host-shared");

        var entries = new List<ReleaseFileEntry>();
        foreach (var path in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
        {
            await using var stream = File.OpenRead(path);
            entries.Add(new ReleaseFileEntry(
                Path.GetRelativePath(stage, path).Replace('\\', '/'),
                stream.Length,
                Convert.ToHexString(await SHA256.HashDataAsync(stream))));
        }
        var manifest = new ReleaseManifest(
            1, "BiliDownloader", "p0", "net10.0", "win-x64", "test", false, entries);
        await File.WriteAllTextAsync(
            Path.Combine(stage, "bilidownloader.release.json"),
            JsonSerializer.Serialize(manifest));

        // 先冻结清单再注入破坏，分别证明文件集和摘要校验不信任脚本的 staging 结果。
        if (mutation == "undeclared")
            await File.WriteAllTextAsync(Path.Combine(stage, "undeclared.txt"), "surprise");
        if (mutation == "hash")
            await File.AppendAllTextAsync(Path.Combine(stage, "BiliDownloader.dll"), "tampered");

        var package = Path.Combine(root, $"package-{suffix}.zip");
        ZipFile.CreateFromDirectory(stage, package);
        if (mutation == "traversal")
        {
            using var archive = ZipFile.Open(package, ZipArchiveMode.Update);
            await using var writer = archive.CreateEntry("../escape.txt").Open();
            await writer.WriteAsync("escape"u8.ToArray());
        }
        return package;
    }

    private sealed class AcceptanceSandbox : IDisposable
    {
        public AcceptanceSandbox()
        {
            Root = Path.Combine(Path.GetTempPath(), "BiliDownloader-G8", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
