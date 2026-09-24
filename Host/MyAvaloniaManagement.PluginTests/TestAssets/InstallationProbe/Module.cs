using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace InstallationProbe;

/// <summary>真实编译成两个版本的安装夹具；用独立数据根记录实际执行的版本，不以修改 manifest 假装更新。</summary>
public sealed class Module : IPluginModule
{
    public Module() => Evidence("constructed");
    public void Configure(IPluginRegistration registration)
    {
        Evidence("configured");
        registration.UseLifecycle<Lifecycle>();
    }
    internal static string Version => typeof(Module).Assembly.GetName().Version!.ToString(3);
    internal static string? Root => Environment.GetEnvironmentVariable("MYAVALONIA_DATA_DIRECTORY");
    internal static void Evidence(string stage)
    {
        if (Root is not { } root) return;
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "installation-" + stage + ".txt"), Version);
        if (stage is "constructed" or "configured")
            File.WriteAllText(Path.Combine(root, "installation-" + stage + "-apartment.txt"), Thread.CurrentThread.GetApartmentState().ToString());
    }
}

public sealed class Lifecycle : IPluginLifecycle
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 仅测试夹具读取此开关：模拟载荷已加载、尚未确认时的进程中断；没有 Shutdown 或最终日志。
        if (Module.Version == "2.0.0" && Module.Root is { } crashRoot && File.Exists(Path.Combine(crashRoot, "exit-version-2")))
            Environment.Exit(71);
        if (Module.Version == "2.0.0" && Module.Root is { } root && File.Exists(Path.Combine(root, "fail-version-2")))
            throw new InvalidOperationException("安装夹具指定新版本初始化失败。");
        if (Module.Version == "2.0.0" && Module.Root is { } holdRoot && File.Exists(Path.Combine(holdRoot, "hold-version-2")))
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watcher = new FileSystemWatcher(holdRoot, "release-version-2");
            watcher.Created += (_, _) => release.TrySetResult(); watcher.EnableRaisingEvents = true;
            Module.Evidence("initializing");
            if (File.Exists(Path.Combine(holdRoot, "release-version-2"))) release.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        Module.Evidence("ready");
    }
    public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
