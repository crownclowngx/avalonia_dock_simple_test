using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using MyAvaloniaManagement.Business.Plugins.Installation;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>补充真实文件的资源边界、占用、元数据和用户确认之间的竞争；不依赖个人安装目录。</summary>
public sealed class PluginInstallationBoundaryTests
{
    [Theory]
    [InlineData("path")] [InlineData("hash")] [InlineData("length")] [InlineData("duplicate-file")]
    [InlineData("extra-file")] [InlineData("missing-file")] [InlineData("invalid-json")]
    public async Task 配套文件集合和每个摘要均严格核对(string change)
    {
        using var files = new PluginInstallTestFiles(); var zip = files.Zip(sidecar: true);
        var path = Path.ChangeExtension(zip, ".manifest.json"); var root = JsonNode.Parse(File.ReadAllText(path))!;
        var list = root["files"]!.AsArray();
        switch (change)
        {
            case "path": list[0]!["path"] = "Controls/Probe/unknown"; break;
            case "hash": list[0]!["sha256"] = new string('0', 64); break;
            case "length": list[0]!["length"] = -1; break;
            case "duplicate-file": list[1] = list[0]!.DeepClone(); break;
            case "extra-file": list.Add(list[0]!.DeepClone()); break;
            case "missing-file": list.RemoveAt(0); break;
        }
        File.WriteAllText(path, change == "invalid-json" ? "{" : root.ToJsonString());
        await Assert.ThrowsAnyAsync<Exception>(() => new PluginPackageInspector(files.Paths).InspectAsync(zip));
        Assert.Empty(Directory.GetFileSystemEntries(files.Paths.PluginsRoot));
    }

    [Theory]
    [InlineData("deep")] [InlineData("manifest-size")] [InlineData("nested-manifest")]
    [InlineData("bad-dll")] [InlineData("bad-zip")] [InlineData("sidecar-directory")]
    public async Task 层级清单大小及坏格式无法绕过预检(string change)
    {
        using var files = new PluginInstallTestFiles(); var zip = files.Zip();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            if (change == "deep") PluginInstallTestFiles.Write(archive, "Controls/Probe/" + string.Concat(Enumerable.Repeat("d/", 33)) + "file", "bad");
            if (change == "nested-manifest") PluginInstallTestFiles.Write(archive, "Controls/Probe/nested/plugin.manifest.json", PluginInstallTestFiles.Manifest(1));
            if (change == "manifest-size")
            {
                archive.GetEntry("Controls/Probe/plugin.manifest.json")!.Delete();
                PluginInstallTestFiles.Write(archive, "Controls/Probe/plugin.manifest.json", PluginInstallTestFiles.Manifest(1) + new string(' ', 65536));
            }
            if (change == "bad-dll") PluginInstallTestFiles.Write(archive, "Controls/Probe/private.dll", "not a PE");
        }
        if (change == "bad-zip") File.WriteAllText(zip, "not a zip");
        if (change == "sidecar-directory") Directory.CreateDirectory(Path.ChangeExtension(zip, ".manifest.json"));
        await Assert.ThrowsAnyAsync<Exception>(() => new PluginPackageInspector(files.Paths).InspectAsync(zip));
        Assert.Empty(Directory.GetFileSystemEntries(files.Paths.PluginsRoot));
    }

    [Fact]
    public async Task 配额等于真实字节通过而一字节不足拒绝()
    {
        using var files = new PluginInstallTestFiles(); var zip = files.Zip(sidecar: true);
        using var archive = ZipFile.OpenRead(zip);
        var limits = new PluginPackageLimits(new FileInfo(zip).Length, archive.Entries.Sum(item => item.Length),
            archive.Entries.Count, (int)new FileInfo(Path.ChangeExtension(zip, ".manifest.json")).Length);
        Assert.True((await new PluginPackageInspector(files.Paths, limits).InspectAsync(zip)).HasReleaseManifest);
        await Assert.ThrowsAsync<PluginInstallException>(() => new PluginPackageInspector(files.Paths, limits with { ExpandedBytes = limits.ExpandedBytes - 1 }).InspectAsync(zip));
    }

    [Fact]
    public async Task 私有图标例外和可选构建信息保留()
    {
        using var files = new PluginInstallTestFiles(); var zip = files.Zip();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, "MyAvaloniaManagement.Icons.dll"), "Controls/Probe/MyAvaloniaManagement.Icons.dll");
            PluginInstallTestFiles.Write(archive, "Controls/Probe/plugin.build.json", "{}");
        }
        var package = await new PluginPackageInspector(files.Paths).InspectAsync(zip);
        Assert.True(File.Exists(Path.Combine(package.PayloadPath, "MyAvaloniaManagement.Icons.dll")));
    }

    [Theory]
    [InlineData("original")] [InlineData("candidate")] [InlineData("archive")]
    public async Task 预览到提交之间的变化必须重新审阅(string location)
    {
        using var files = new PluginInstallTestFiles(); files.Deploy(1);
        var service = new PluginInstallationService(files.Paths, files.Store);
        try
        {
            await service.InspectAsync(files.Zip(2), null);
            var preview = service.Preview!;
            var path = location switch
            {
                "original" => Path.Combine(files.Paths.Target("Probe"), "changed"),
                "candidate" => Path.Combine(preview.Package.PayloadPath, "changed"),
                _ => Path.Combine(files.Paths.Stage(preview.Package.OperationId), "package.zip")
            };
            File.WriteAllText(path, "changed");
            var current = await PluginInstallApplier.HashDirectoryAsync(files.Paths.Target("Probe"), default);
            await Assert.ThrowsAsync<PluginInstallException>(() => service.CommitAsync(false));
            Assert.Null(files.Store.ReadOperation());
            Assert.Equal(current, await PluginInstallApplier.HashDirectoryAsync(files.Paths.Target("Probe"), default));
        }
        finally { await service.StopAsync(); }
    }

    [Fact]
    public async Task 写待办后返回失败的服务收尾仍保留事务载荷()
    {
        using var files = new PluginInstallTestFiles();
        var service = new PluginInstallationService(files.Paths, new(files.Paths, new FailAfterWrite()));
        await service.InspectAsync(files.Zip(), null);
        var stage = files.Paths.Stage(service.Preview!.Package.OperationId);
        await Assert.ThrowsAsync<IOException>(() => service.CommitAsync(false));
        await service.StopAsync();
        Assert.Equal(PluginInstallPhase.Staged, files.Store.ReadOperation()!.Phase);
        Assert.True(File.Exists(Path.Combine(stage, "package.zip")));
        await new PluginInstallApplier(files.Paths, files.Store).ApplyAsync(files.Store.ReadOperation()!, default);
        Assert.True(Directory.Exists(files.Paths.Target("Probe")));
    }

    [Fact]
    public async Task 取消落盘后清理被占用也不能复活待办()
    {
        using var files = new PluginInstallTestFiles(); var operation = await files.StageAsync(1);
        var service = new PluginInstallationService(files.Paths, files.Store);
        using (var held = new FileStream(Path.Combine(files.Paths.Stage(operation.OperationId), "package.zip"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await service.CancelPendingAsync();
            Assert.Equal(PluginInstallPhase.Cancelled, files.Store.ReadOperation()!.Phase);
            Assert.Contains("清理未完成", service.Status.Message);
        }
        await service.StopAsync();
        using var startup = new PluginInstallationSession(files.Paths.PluginsRoot);
        await startup.PrepareAsync(default);
        Assert.Empty(Directory.GetFileSystemEntries(files.Paths.PluginsRoot));
    }

    [Fact]
    public async Task 冻结安装准入可撤销且取消检查不产生待办()
    {
        using var files = new PluginInstallTestFiles(); var zip = files.Zip();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PluginPackageInspector(files.Paths).InspectAsync(zip, cancellationToken: new(true)));
        var service = new PluginInstallationService(files.Paths, files.Store);
        using (await service.PauseForRestartAsync(default))
            await Assert.ThrowsAsync<PluginInstallException>(() => service.InspectAsync(zip, null));
        await service.InspectAsync(zip, null); Assert.NotNull(service.Preview);
        await service.StopAsync(); Assert.Null(files.Store.ReadOperation());
    }

    [Fact]
    public async Task 现有只读锁文件仍可协作且不同根相互独立()
    {
        using var files = new PluginInstallTestFiles();
        using (PluginInstallationLease.Runtime(files.Paths, false)) { }
        using (await PluginInstallationLease.OperationsAsync(files.Paths, default)) { }
        var locks = new[] { "runtime.lock", "operations.lock" }.Select(name => Path.Combine(files.Paths.ManagementRoot, name)).ToArray();
        try
        {
            foreach (var path in locks) File.SetAttributes(path, FileAttributes.ReadOnly);
            using var runtime = PluginInstallationLease.Runtime(files.Paths, false);
            using var another = PluginInstallationLease.Runtime(new(files.Paths.PluginsRoot.ToUpperInvariant()), false);
            Assert.Throws<IOException>(() => PluginInstallationLease.Runtime(files.Paths, true));
            using var writer = await PluginInstallationLease.OperationsAsync(files.Paths, default);
            var different = new PluginInstallPaths(Path.Combine(files.Root, "other", "Controls"));
            using var independent = PluginInstallationLease.Runtime(different, true);
        }
        finally { foreach (var path in locks) File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Theory]
    [InlineData("directory")] [InlineData("missing-with-backup")] [InlineData("oversize")]
    public void 损坏登记不能误作新安装(string change)
    {
        using var files = new PluginInstallTestFiles(); var path = Path.Combine(files.Paths.ManagementRoot, "installed-v1.json");
        if (change == "directory") Directory.CreateDirectory(path);
        else if (change == "missing-with-backup") File.WriteAllText(path + ".previous", "{}");
        else File.WriteAllText(path, new string(' ', 4 * 1024 * 1024 + 1));
        Assert.Throws<PluginInstallException>(() => files.Store.ReadIndex());
    }

    [Fact]
    public async Task 检查后管理父目录换成Junction会被再次拒绝()
    {
        using var files = new PluginInstallTestFiles(); var zip = files.Zip();
        var outside = Path.Combine(files.Root, "outside"); Directory.CreateDirectory(outside);
        var saved = Path.Combine(files.Root, "saved-management"); Directory.Move(files.Paths.ManagementRoot, saved);
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", files.Paths.ManagementRoot, outside }) start.ArgumentList.Add(arg);
        using var link = Process.Start(start)!; await link.WaitForExitAsync(); Assert.Equal(0, link.ExitCode);
        try
        {
            await Assert.ThrowsAsync<PluginInstallException>(() => new PluginPackageInspector(files.Paths).InspectAsync(zip));
            Assert.Empty(Directory.GetFileSystemEntries(outside));
        }
        finally
        {
            // 只移除本测试创建的链接本身，再归还原管理目录；不沿链接递归清理。
            Assert.True((File.GetAttributes(files.Paths.ManagementRoot) & FileAttributes.ReparsePoint) != 0);
            Directory.Delete(files.Paths.ManagementRoot); Directory.Move(saved, files.Paths.ManagementRoot);
        }
    }

    private sealed class FailAfterWrite : IPluginInstallFileCommit
    {
        private readonly PluginInstallFileCommit _real = new();
        public void WriteAtomic(string path, byte[] bytes) { _real.WriteAtomic(path, bytes); throw new IOException("模拟提交后返回失败。"); }
        public void MoveDirectory(string source, string destination) => _real.MoveDirectory(source, destination);
    }
}
