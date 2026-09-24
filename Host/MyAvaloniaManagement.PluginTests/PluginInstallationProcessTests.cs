using System.Diagnostics;
using System.Text.Json;
using MyAvaloniaManagement.Business.Plugins.Installation;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>复用真实生产入口、加载器、生命周期和重启助手；只用 Headless 替代显示设备。</summary>
public sealed class PluginInstallationProcessTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task 新进程真实更新并根据启用状态确认(bool dotnet, bool disabled)
    {
        using var run = new Run();
        run.Files.Deploy(1);
        await run.Files.StageAsync(2);
        if (disabled)
        {
            Directory.CreateDirectory(run.Data);
            File.WriteAllText(Path.Combine(run.Data, "plugin-enablement-v1.json"), JsonSerializer.Serialize(new
            { schemaVersion = 1, disabledPluginIds = new[] { PluginInstallTestFiles.Id } }));
        }
        await run.ExitAsync(run.Start("installation-run", dotnet));
        Assert.Equal(PluginInstallPhase.Committed, run.Files.Store.ReadOperation()!.Phase);
        Assert.Equal(disabled ? "installedDisabled" : "startupConfirmed", Assert.Single(run.Files.Store.ReadIndex().Plugins).Validation);
        if (disabled) Assert.False(File.Exists(Path.Combine(run.Data, "installation-constructed.txt")));
        else
        {
            Assert.Equal("2.0.0", File.ReadAllText(Path.Combine(run.Data, "installation-ready.txt")));
            Assert.Equal("STA", File.ReadAllText(Path.Combine(run.Data, "installation-constructed-apartment.txt")));
            Assert.Equal("STA", File.ReadAllText(Path.Combine(run.Data, "installation-configured-apartment.txt")));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task 初始化失败在下一真实进程恢复旧版或撤销新装(bool fresh, bool abrupt)
    {
        using var run = new Run();
        if (!fresh) run.Files.Deploy(1);
        await run.Files.StageAsync(2);
        Directory.CreateDirectory(run.Data);
        File.WriteAllText(Path.Combine(run.Data, abrupt ? "exit-version-2" : "fail-version-2"), "fail");
        await run.ExitAsync(run.Start("installation-run"), abrupt ? 71 : 0);
        Assert.Equal("2.0.0", File.ReadAllText(Path.Combine(run.Data, "installation-constructed.txt")));
        Assert.False(File.Exists(Path.Combine(run.Data, "installation-ready.txt")));
        Assert.Equal(abrupt ? PluginInstallPhase.AwaitingStartup : PluginInstallPhase.RecoveryRequired, run.Files.Store.ReadOperation()!.Phase);
        await run.ExitAsync(run.Start("installation-run"));
        Assert.Equal(PluginInstallPhase.RolledBack, run.Files.Store.ReadOperation()!.Phase);
        Assert.Empty(run.Files.Store.ReadIndex().Plugins);
        if (fresh) Assert.False(Directory.Exists(run.Files.Paths.Target("Probe")));
        else Assert.Equal("1.0.0", File.ReadAllText(Path.Combine(run.Data, "installation-ready.txt")));
    }

    [Fact]
    public async Task 运行租约持续到真实进程退出且独立数据根不能绕过()
    {
        using var run = new Run();
        run.Files.Deploy(1);
        var first = run.Start("installation-hold");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        string? line;
        do { line = await first.StandardOutput.ReadLineAsync(timeout.Token); }
        while (line is not null && line != "installation-held");
        Assert.Equal("installation-held", line);
        Assert.False(first.HasExited);
        Assert.Throws<IOException>(() => PluginInstallationLease.Runtime(run.Files.Paths, true));
        await run.Files.StageAsync(2);
        var otherData = Path.Combine(run.Files.Root, "另一个数据目录");
        await run.ExitAsync(run.Start("installation-run", data: otherData));
        Assert.Equal(PluginInstallPhase.Staged, run.Files.Store.ReadOperation()!.Phase);
        Assert.Equal("1.0.0", File.ReadAllText(Path.Combine(otherData, "installation-ready.txt")));
        await first.StandardInput.WriteLineAsync("release");
        await first.StandardInput.FlushAsync();
        await run.ExitAsync(first);
        await run.ExitAsync(run.Start("installation-run"));
        Assert.Equal(PluginInstallPhase.Committed, run.Files.Store.ReadOperation()!.Phase);
        Assert.Equal("2.0.0", File.ReadAllText(Path.Combine(run.Data, "installation-ready.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 看板确认经过真实助手重启后运行新版本(bool dotnet)
    {
        using var run = new Run();
        run.Files.Deploy(1);
        var first = run.Start("installation-dashboard", dotnet, zip: run.Files.Zip(2, sidecar: true));
        await run.ExitAsync(first);
        // 助手的后继进程不是父测试直接创建的，使用真实退出收据定位 PID，并核对其启动时间后等待。
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var exit = Path.Combine(run.Files.Root, "host-1.exit.json");
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(run.Files.Root, "host-1.exit.json");
        watcher.Created += (_, _) => completed.TrySetResult(); watcher.EnableRaisingEvents = true;
        if (File.Exists(exit)) completed.TrySetResult();
        await completed.Task.WaitAsync(timeout.Token);
        await run.WaitForChildrenAsync(timeout.Token);
        run.AssertNoErrors();
        Assert.Equal(2, Directory.GetFiles(run.Files.Root, "host-*.start.json").Length);
        Assert.Single(Directory.GetFiles(run.Files.Root, "helper-*.start.json"));
        Assert.Equal(PluginInstallPhase.Committed, run.Files.Store.ReadOperation()!.Phase);
        Assert.Equal("2.0.0", File.ReadAllText(Path.Combine(run.Data, "installation-ready.txt")));
    }

    [Fact]
    public async Task 试启动期间另一实例不能加载候选且终止所有者后可恢复()
    {
        using var run = new Run(); run.Files.Deploy(1); await run.Files.StageAsync(2);
        Directory.CreateDirectory(run.Data); File.WriteAllText(Path.Combine(run.Data, "hold-version-2"), "hold");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(run.Data, "installation-initializing.txt");
        watcher.Created += (_, _) => entered.TrySetResult(); watcher.EnableRaisingEvents = true;
        var owner = run.Start("installation-run"); await entered.Task.WaitAsync(timeout.Token);
        var pending = run.Files.Store.ReadOperation()!;
        Assert.Equal(PluginInstallPhase.AwaitingStartup, pending.Phase);
        Assert.True(PluginInstallApplier.OwnerAlive(pending)); Assert.Equal(owner.Id, pending.OwnerPid);
        var otherData = Path.Combine(run.Files.Root, "other-data");
        await run.ExitAsync(run.Start("startup-failure", data: otherData), 1);
        Assert.False(File.Exists(Path.Combine(otherData, "installation-constructed.txt")));
        Assert.Equal(pending, run.Files.Store.ReadOperation());
        owner.Kill(); await owner.WaitForExitAsync(timeout.Token);
        Assert.False(PluginInstallApplier.OwnerAlive(pending));
        await run.ExitAsync(run.Start("installation-run"));
        Assert.Equal(PluginInstallPhase.RolledBack, run.Files.Store.ReadOperation()!.Phase);
        Assert.Equal("1.0.0", File.ReadAllText(Path.Combine(run.Data, "installation-ready.txt")));
    }

    private sealed class Run : IDisposable
    {
        internal PluginInstallTestFiles Files { get; } = new();
        internal string Data => Path.Combine(Files.Root, "data");
        private readonly List<Process> _owned = [];
        internal Run() => RestartHarnessFiles.Copy(Path.Combine(AppContext.BaseDirectory, "RestartHarness"), Path.Combine(Files.Root, "app"));
        internal Process Start(string mode, bool dotnet = false, string? data = null, string? zip = null)
        {
            var app = Path.Combine(Files.Root, "app", "MyAvaloniaManagement.RestartHarness");
            var info = new ProcessStartInfo(dotnet ? "dotnet" : app + ".exe")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Files.Root,
                RedirectStandardInput = mode == "installation-hold", RedirectStandardOutput = mode == "installation-hold"
            };
            if (dotnet) info.ArgumentList.Add(app + ".dll");
            info.ArgumentList.Add(mode); info.ArgumentList.Add(zip ?? "中文 空格"); info.ArgumentList.Add(Files.Root);
            info.Environment["MYAVALONIA_DATA_DIRECTORY"] = data ?? Data;
            var process = Process.Start(info)!; _owned.Add(process); return process;
        }
        internal async Task ExitAsync(Process process, int expectedCode = 0)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await process.WaitForExitAsync(timeout.Token);
            AssertNoErrors(); Assert.Equal(expectedCode, process.ExitCode);
        }
        internal void AssertNoErrors() => Assert.Empty(Directory.GetFiles(Files.Root, "*.error").Select(File.ReadAllText));
        internal async Task WaitForChildrenAsync(CancellationToken token)
        {
            foreach (var file in Directory.GetFiles(Files.Root, "*.start.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var item = doc.RootElement;
                Process? process;
                try { process = Process.GetProcessById(item.GetProperty("pid").GetInt32()); }
                catch (ArgumentException) { continue; }
                using (process)
                {
                    if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == item.GetProperty("startTicks").GetInt64())
                        await process.WaitForExitAsync(token);
                }
            }
        }
        public void Dispose()
        {
            foreach (var process in _owned)
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
                process.Dispose();
            }
            // 失败清理同样核对 PID 与开始时间，避免碰到已经复用的系统进程。
            foreach (var file in Directory.GetFiles(Files.Root, "*.start.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                try
                {
                    using var process = Process.GetProcessById(doc.RootElement.GetProperty("pid").GetInt32());
                    if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == doc.RootElement.GetProperty("startTicks").GetInt64())
                    { process.Kill(entireProcessTree: true); process.WaitForExit(); }
                }
                catch (ArgumentException) { }
            }
            Files.Dispose();
        }
    }
}
