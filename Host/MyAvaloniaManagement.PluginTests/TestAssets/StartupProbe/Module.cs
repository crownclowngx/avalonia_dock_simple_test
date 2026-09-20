using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace StartupProbe;

/// <summary>仅测试夹具：真实生命周期的同步段等待 UI 发出的明确信号，证明启动并未阻塞 UI。</summary>
public sealed class Module : IPluginModule
{
    public void Configure(IPluginRegistration registration) => registration.UseLifecycle<ProbeLifecycle>();
}

public sealed class ProbeLifecycle : IPluginLifecycle
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        var root = Environment.GetEnvironmentVariable("MYAVALONIA_V15_PROBE_ROOT")
            ?? throw new InvalidOperationException("启动夹具缺少隔离目录。");
        using var release = new ManualResetEventSlim();
        using var watcher = new FileSystemWatcher(root, "startup-release.json");
        watcher.Created += (_, _) => release.Set();
        watcher.EnableRaisingEvents = true;
        File.WriteAllText(Path.Combine(root, "startup-entered.json"), JsonSerializer.Serialize(new
        { at = DateTimeOffset.UtcNow, thread = Environment.CurrentManagedThreadId, apartment = Thread.CurrentThread.GetApartmentState().ToString() }));
        // 故意阻塞在返回 Task 之前，若 Host 把生命周期放在 UI 线程，本测试将无法由 UI 放行并失败。
        if (!File.Exists(Path.Combine(root, "startup-release.json")) && !release.Wait(TimeSpan.FromSeconds(15), cancellationToken))
            throw new TimeoutException("UI 未能在启动阻塞期间发出放行信号。");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
    public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
