using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>真实旧 Host、助手与新 Host；夹具只替换 UI 平台，文件信号不作为进程退出的替代。</summary>
public sealed class HostRestartProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 新进程禁用再启用并交接布局锁_两种启动形式(bool dotnet)
    {
        using var run = new RunFiles();
        var first = run.Start("roundtrip", dotnet);
        await run.WaitFor(() => File.Exists(Path.Combine(run.Root, "host-2.exit.json")) || run.HasErrors);
        Assert.False(run.HasErrors, run.Errors);
        await run.WaitForOwnedProcesses();
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(3, Directory.GetFiles(run.Root, "host-*.start.json").Length);
        Assert.Equal(2, Directory.GetFiles(run.Root, "helper-*.start.json").Length);
        var pids = new HashSet<int>();
        for (var stage = 0; stage < 3; stage++)
        {
            using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(run.Root, $"state-{stage}.json")));
            Assert.True(pids.Add(state.RootElement.GetProperty("pid").GetInt32()));
            Assert.Equal(stage == 1, state.RootElement.GetProperty("disabled").GetBoolean());
            Assert.True(state.RootElement.GetProperty("locked").GetBoolean());
            Assert.Equal(1, state.RootElement.GetProperty("initialDocuments").GetInt32());
            Assert.Equal(stage == 0 ? 2 : 1, state.RootElement.GetProperty("documentsBeforeClose").GetInt32());
            using var started = JsonDocument.Parse(File.ReadAllText(Path.Combine(run.Root, $"host-{stage}.start.json")));
            Assert.Equal(new[] { "roundtrip", "中文 空格\"&$(data)", run.Root },
                started.RootElement.GetProperty("args").EnumerateArray().Select(item => item.GetString()));
            Assert.Equal(run.Root, started.RootElement.GetProperty("workingDirectory").GetString());
            Assert.Equal(Path.Combine(run.Root, "data"), started.RootElement.GetProperty("dataRoot").GetString());
            using var exited = JsonDocument.Parse(File.ReadAllText(Path.Combine(run.Root, $"host-{stage}.exit.json")));
            Assert.Equal(0, exited.RootElement.GetProperty("code").GetInt32());
        }
        using var writer = new FileStream(Path.Combine(run.Root, "data", "layout-v3.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(File.Exists(Path.Combine(run.Root, "data", "layout-v3.json")));
        run.SaveEvidence();
    }

    [Theory]
    [InlineData("ordinary", false)]
    [InlineData("cancel", false)]
    [InlineData("crash", false)]
    [InlineData("kill", false)]
    [InlineData("bad-exit", true)]
    public async Task 无许可或未成功退出不能产生后继Host(string mode, bool error)
    {
        using var run = new RunFiles();
        var first = run.Start(mode, false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        if (mode == "kill")
        {
            await run.WaitFor(() => File.Exists(Path.Combine(run.Root, "parent-ready.json")) || run.HasErrors);
            Assert.False(run.HasErrors, run.Errors);
            first.Kill();
        }
        await first.WaitForExitAsync(timeout.Token);
        if (mode != "ordinary")
            await run.WaitFor(() => Directory.GetFiles(run.Root, "helper-*.exit.json").Length == 1);
        await run.WaitForOwnedProcesses();
        if (mode == "kill") Assert.NotEqual(0, first.ExitCode);
        else Assert.Equal(error ? 1 : 0, first.ExitCode);
        Assert.Equal(error, run.HasErrors);
        Assert.Single(Directory.GetFiles(run.Root, "host-*.start.json"));
        if (mode == "cancel")
        {
            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(run.Root, "cancelled.json")));
            Assert.True(result.RootElement.GetProperty("visible").GetBoolean());
        }
        run.SaveEvidence();
    }

    [Fact]
    public async Task 最终许可已确认但旧进程仍存活时不能提前拉起()
    {
        using var run = new RunFiles();
        var first = run.Start("delayed-exit", false);
        await run.WaitFor(() => File.Exists(Path.Combine(run.Root, "host-0.exit.json")) || run.HasErrors);
        Assert.False(run.HasErrors, run.Errors);
        Assert.False(first.HasExited);
        Assert.False(File.Exists(Path.Combine(run.Root, "host-1.start.json")));
        File.WriteAllText(Path.Combine(run.Root, "release.json"), "{}");
        await run.WaitFor(() => File.Exists(Path.Combine(run.Root, "host-1.exit.json")) || run.HasErrors);
        Assert.False(run.HasErrors, run.Errors);
        await run.WaitForOwnedProcesses();
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(2, Directory.GetFiles(run.Root, "host-*.start.json").Length);
        Assert.Single(Directory.GetFiles(run.Root, "helper-*.start.json"));
        run.SaveEvidence();
    }

    private sealed class RunFiles : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Host重启 测试", Guid.NewGuid().ToString("N"));
        private readonly List<Process> _owned = [];
        internal bool HasErrors => Directory.GetFiles(Root, "*.error").Length != 0;
        internal string Errors => string.Join(Environment.NewLine, Directory.GetFiles(Root, "*.error").Select(File.ReadAllText));

        internal RunFiles()
        {
            Directory.CreateDirectory(Root);
            Copy(Path.Combine(AppContext.BaseDirectory, "RestartHarness"), Path.Combine(Root, "app"));
            // 使用 Gate 已构建的 MyPlugTest 完整部署闭包；不依赖用户安装目录或邻接业务仓库。
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "MyAvaloniaManagement.sln"))) repo = repo.Parent;
            var config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            Copy(Path.Combine(repo!.FullName, "Host", "MyAvaloniaManagement", "bin", config, "net10.0", "Controls", "MyPlugTest"),
                Path.Combine(Root, "app", "Controls", "MyPlugTest"));
        }

        internal Process Start(string mode, bool dotnet)
        {
            var app = Path.Combine(Root, "app", "MyAvaloniaManagement.RestartHarness");
            var info = new ProcessStartInfo(dotnet ? "dotnet" : app + (OperatingSystem.IsWindows() ? ".exe" : ""))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Root };
            if (dotnet) info.ArgumentList.Add(app + ".dll");
            info.ArgumentList.Add(mode); info.ArgumentList.Add("中文 空格\"&$(data)"); info.ArgumentList.Add(Root);
            info.Environment["MYAVALONIA_DATA_DIRECTORY"] = Path.Combine(Root, "data");
            var process = Process.Start(info)!;
            _owned.Add(process);
            return process;
        }

        internal async Task WaitFor(Func<bool> condition)
        {
            var events = Channel.CreateUnbounded<bool>();
            using var watcher = new FileSystemWatcher(Root) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            watcher.Created += (_, _) => events.Writer.TryWrite(true);
            watcher.Changed += (_, _) => events.Writer.TryWrite(true);
            watcher.EnableRaisingEvents = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while (!condition()) await events.Reader.ReadAsync(timeout.Token);
        }

        internal async Task WaitForOwnedProcesses()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            foreach (var path in Directory.GetFiles(Root, "*.start.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                try
                {
                    var process = Process.GetProcessById(doc.RootElement.GetProperty("pid").GetInt32());
                    if (process.StartTime.ToUniversalTime().Ticks != doc.RootElement.GetProperty("startTicks").GetInt64())
                    { process.Dispose(); continue; }
                    _owned.Add(process);
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                { /* 进程已经退出，或在取得句柄期间完成退出。 */ }
            }
        }

        internal void SaveEvidence()
        {
            var evidence = Path.Combine(AppContext.BaseDirectory, "TestResults", "v14-restart", Path.GetFileName(Root));
            Directory.CreateDirectory(evidence);
            foreach (var path in Directory.GetFiles(Root, "*.json")) File.Copy(path, Path.Combine(evidence, Path.GetFileName(path)), true);
        }

        private static void Copy(string source, string target)
        {
            foreach (var path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var output = Path.Combine(target, Path.GetRelativePath(source, path));
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(path, output, true);
            }
        }

        public void Dispose()
        {
            foreach (var path in Directory.GetFiles(Root, "*.start.json"))
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    var process = Process.GetProcessById(doc.RootElement.GetProperty("pid").GetInt32());
                    if (process.StartTime.ToUniversalTime().Ticks == doc.RootElement.GetProperty("startTicks").GetInt64()) _owned.Add(process);
                    else process.Dispose();
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException) { }
            foreach (var process in _owned)
                try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } } catch (InvalidOperationException) { }
            foreach (var process in _owned) process.Dispose();
            // 只清理本夹具创建且持有的目录，失败时保留收据以便诊断。
            try { Directory.Delete(Root, true); } catch (IOException) { }
        }
    }
}
