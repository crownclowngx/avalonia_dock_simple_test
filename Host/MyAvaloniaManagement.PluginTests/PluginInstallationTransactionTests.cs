using System.Text.Json;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Installation;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>在真实临时目录的提交边界注入一次故障，并核对载荷、登记与日志，避免只验证调用顺序。</summary>
public sealed class PluginInstallationTransactionTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task 整目录更新保留旧版并区分禁用与运行确认(bool disabled)
    {
        using var files = new PluginInstallTestFiles(); files.Deploy(1);
        File.WriteAllText(Path.Combine(files.Paths.Target("Probe"), "old-only.txt"), "old data");
        var operation = await files.StageAsync(2);
        var applier = new PluginInstallApplier(files.Paths, files.Store);
        using (PluginInstallationLease.Runtime(files.Paths, true)) await applier.ApplyAsync(operation, default);
        Assert.False(File.Exists(Path.Combine(files.Paths.Target("Probe"), "old-only.txt")));
        Assert.Equal("old data", File.ReadAllText(Path.Combine(files.Paths.Backup(operation.OperationId), "old-only.txt")));
        Assert.Equal(PluginInstallPhase.AwaitingStartup, files.Store.ReadOperation()!.Phase);
        using (PluginInstallationLease.Runtime(files.Paths, false)) await applier.ConfirmAsync(!disabled, !disabled, default);
        var record = Assert.Single(files.Store.ReadIndex().Plugins);
        Assert.Equal("2.0.0", record.Version);
        Assert.Equal(disabled ? "installedDisabled" : "startupConfirmed", record.Validation);
        Assert.Equal("1.0.0", record.BackupVersion);
        Assert.Equal(PluginInstallPhase.Committed, files.Store.ReadOperation()!.Phase);
    }

    [Theory]
    [InlineData(1, false)] [InlineData(1, true)] [InlineData(2, false)] [InlineData(2, true)]
    [InlineData(3, false)] [InlineData(3, true)] [InlineData(4, false)] [InlineData(4, true)]
    [InlineData(5, false)] [InlineData(5, true)] [InlineData(6, false)]
    public async Task 每个应用和确认提交边界中断均可恢复旧版(int step, bool after)
    {
        using var files = new PluginInstallTestFiles(); files.Deploy(1);
        var operation = await files.StageAsync(2);
        var fault = new OneFailure(step, after);
        var store = new PluginInstallationStore(files.Paths, fault);
        var applier = new PluginInstallApplier(files.Paths, store, fault);
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            using (PluginInstallationLease.Runtime(files.Paths, true)) await applier.ApplyAsync(operation, default);
            using (PluginInstallationLease.Runtime(files.Paths, false)) await applier.ConfirmAsync(true, true, default);
        });
        Assert.True(fault.Triggered);
        var actual = files.Store.ReadOperation()!;
        if (actual.Phase != PluginInstallPhase.Staged)
        {
            var recovery = new PluginInstallApplier(files.Paths, files.Store);
            using var exclusive = PluginInstallationLease.Runtime(files.Paths, true);
            await recovery.RecoverAsync(actual, default);
            // 再次用相同恢复意图进入，模拟最终日志写入前再次中断；结果仍不能丢失旧目录。
            await recovery.RecoverAsync(actual with { Phase = PluginInstallPhase.RecoveryRequired }, default);
            Assert.Equal(PluginInstallPhase.RolledBack, files.Store.ReadOperation()!.Phase);
            Assert.Empty(files.Store.ReadIndex().Plugins); // 原来是人工部署，恢复后仍为人工部署。
        }
        Assert.True(PluginManifestReader.TryRead(files.Paths.Target("Probe"), out var manifest, out _, out _));
        Assert.Equal("1.0.0", manifest!.PluginVersion.ToString(3));
        Assert.Equal(operation.PreviousHash, await PluginInstallApplier.HashDirectoryAsync(files.Paths.Target("Probe"), default));
    }

    [Fact]
    public async Task 新装未确认恢复为未安装并保留失败载荷()
    {
        using var files = new PluginInstallTestFiles(); var operation = await files.StageAsync(1);
        var applier = new PluginInstallApplier(files.Paths, files.Store);
        using var lease = PluginInstallationLease.Runtime(files.Paths, true);
        await applier.ApplyAsync(operation, default);
        await applier.RecoverAsync(files.Store.ReadOperation()!, default);
        Assert.False(Directory.Exists(files.Paths.Target("Probe")));
        Assert.True(File.Exists(Path.Combine(files.Paths.Stage(operation.OperationId), "failed-payload", "InstallationProbe.Plugin.dll")));
        Assert.Empty(files.Store.ReadIndex().Plugins);
    }

    [Fact]
    public async Task 已加载失败只记录恢复意图不在本进程替换文件()
    {
        using var files = new PluginInstallTestFiles(); files.Deploy(1); var operation = await files.StageAsync(2);
        var applier = new PluginInstallApplier(files.Paths, files.Store);
        await applier.ApplyAsync(operation, default);
        await applier.ConfirmAsync(true, false, default);
        Assert.Equal(PluginInstallPhase.RecoveryRequired, files.Store.ReadOperation()!.Phase);
        Assert.Equal(operation.ArtifactHash, await PluginInstallApplier.HashDirectoryAsync(files.Paths.Target("Probe"), default));
        Assert.Equal(operation.PreviousHash, await PluginInstallApplier.HashDirectoryAsync(files.Paths.Backup(operation.OperationId), default));
    }

    [Fact]
    public async Task 已确认版本可以预览并恢复到上一版本()
    {
        using var files = new PluginInstallTestFiles(); files.Deploy(1); var operation = await files.StageAsync(2);
        var applier = new PluginInstallApplier(files.Paths, files.Store);
        await applier.ApplyAsync(operation, default); await applier.ConfirmAsync(true, true, default);
        var service = new PluginInstallationService(files.Paths, files.Store);
        await service.PrepareRestoreAsync(PluginInstallTestFiles.Id);
        Assert.Equal(PluginInstallAction.Restore, service.Preview!.Plan.Action);
        await Assert.ThrowsAnyAsync<Exception>(() => service.CommitAsync(false));
        await service.CommitAsync(true);
        var restore = files.Store.ReadOperation()!;
        await applier.ApplyAsync(restore, default); await applier.ConfirmAsync(true, true, default);
        Assert.Equal("1.0.0", Assert.Single(files.Store.ReadIndex().Plugins).Version);
        Assert.Equal("2.0.0", Assert.Single(files.Store.ReadIndex().Plugins).BackupVersion);
        await service.StopAsync();
    }

    [Theory]
    [InlineData("original")] [InlineData("candidate")] [InlineData("archive")]
    public async Task 应用前任一已确认输入变化都保留原目录(string location)
    {
        using var files = new PluginInstallTestFiles(); files.Deploy(1); var operation = await files.StageAsync(2);
        var path = location switch
        {
            "original" => Path.Combine(files.Paths.Target("Probe"), "external.txt"),
            "candidate" => Path.Combine(files.Paths.Payload(operation.OperationId), "external.txt"),
            _ => Path.Combine(files.Paths.Stage(operation.OperationId), "package.zip")
        };
        File.WriteAllText(path, "changed");
        var old = await PluginInstallApplier.HashDirectoryAsync(files.Paths.Target("Probe"), default);
        await Assert.ThrowsAnyAsync<Exception>(() => new PluginInstallApplier(files.Paths, files.Store).ApplyAsync(operation, default));
        Assert.Equal(old, await PluginInstallApplier.HashDirectoryAsync(files.Paths.Target("Probe"), default));
        Assert.Equal(PluginInstallPhase.Staged, files.Store.ReadOperation()!.Phase);
    }

    [Fact]
    public async Task 损坏备份阻断恢复并保留现有载荷()
    {
        using var files = new PluginInstallTestFiles(); files.Deploy(1); var operation = await files.StageAsync(2);
        var applier = new PluginInstallApplier(files.Paths, files.Store); await applier.ApplyAsync(operation, default);
        File.WriteAllText(Path.Combine(files.Paths.Backup(operation.OperationId), "changed.txt"), "changed");
        await Assert.ThrowsAnyAsync<Exception>(() => applier.RecoverAsync(files.Store.ReadOperation()!, default));
        Assert.Equal(operation.ArtifactHash, await PluginInstallApplier.HashDirectoryAsync(files.Paths.Target("Probe"), default));
        Assert.True(Directory.Exists(files.Paths.Backup(operation.OperationId)));
    }

    [Fact]
    public async Task 取消先落盘再清理且两个服务不能覆盖同一个待办()
    {
        using var files = new PluginInstallTestFiles();
        var first = new PluginInstallationService(files.Paths, files.Store);
        var second = new PluginInstallationService(files.Paths, files.Store);
        await first.InspectAsync(files.Zip(), null); await second.InspectAsync(files.Zip(2), null);
        var candidate = first.Preview!.Package.OperationId;
        await first.CommitAsync(false);
        await Assert.ThrowsAnyAsync<Exception>(() => second.CommitAsync(false));
        Assert.Equal(candidate, files.Store.ReadOperation()!.OperationId);
        await first.CancelPendingAsync();
        Assert.Equal(PluginInstallPhase.Cancelled, files.Store.ReadOperation()!.Phase);
        Assert.False(Directory.Exists(files.Paths.Stage(candidate)));
        await first.StopAsync(); await second.StopAsync();
    }

    [Theory]
    [InlineData("invalid-json")] [InlineData("unknown")] [InlineData("duplicate")] [InlineData("path")]
    [InlineData("missing")] [InlineData("future")]
    public async Task 损坏操作日志保留原件且不重置为空(string mutation)
    {
        using var files = new PluginInstallTestFiles(); await files.StageAsync(1);
        var path = Path.Combine(files.Paths.ManagementRoot, "operation-v1.json");
        var original = File.ReadAllText(path);
        var text = mutation switch
        {
            "invalid-json" => "{", "unknown" => original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"unknown\": true"),
            "duplicate" => original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"),
            "future" => original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
            "path" => original.Replace("\"targetDirectory\": \"Probe\"", "\"targetDirectory\": \"../outside\""),
            _ => original.Replace("\"schemaVersion\": 1,", "")
        };
        Assert.NotEqual(original, text); File.WriteAllText(path, text);
        Assert.ThrowsAny<Exception>(() => files.Store.ReadOperation());
        Assert.Equal(text, File.ReadAllText(path));
        Assert.Empty(Directory.GetFileSystemEntries(files.Paths.PluginsRoot));
    }

    private sealed class OneFailure(int step, bool after) : IPluginInstallFileCommit
    {
        private readonly PluginInstallFileCommit _real = new();
        private int _count;
        internal bool Triggered { get; private set; }
        public void WriteAtomic(string path, byte[] bytes) => Around(() => _real.WriteAtomic(path, bytes));
        public void MoveDirectory(string source, string destination) => Around(() => _real.MoveDirectory(source, destination));
        private void Around(Action action)
        {
            var shouldFail = ++_count == step;
            if (shouldFail && !after) { Triggered = true; throw new IOException("测试注入：提交之前中断。"); }
            action();
            if (shouldFail && after) { Triggered = true; throw new IOException("测试注入：提交之后中断。"); }
        }
    }
}
