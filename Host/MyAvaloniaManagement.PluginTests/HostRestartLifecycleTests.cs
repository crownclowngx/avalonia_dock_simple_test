using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Composition;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>重启消费真实关闭结果，特别覆盖“未保留资源但 Dispose 失败”这一不能拉起的状态。</summary>
public sealed class HostRestartLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 关闭结果保留释放异常且重复关闭不重复释放(bool fail)
    {
        var calls = new List<string>();
        var plugin = new Cleanup(() => { calls.Add("plugin"); if (fail) throw new IOException("test"); });
        var host = new Cleanup(() => calls.Add("host"));
        var shutdown = new HostRuntimeShutdown(plugin, host, () => calls.Add("scopes"),
            new HostShutdownParticipants(), new HostResourceRetention(), null);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var runtime = new HostRuntime(provider, shutdown);
        var result = runtime.Shutdown();
        Assert.False(result.ResourcesRetained);
        Assert.Equal(fail ? 1 : 0, result.Failures.Count);
        Assert.Same(result, runtime.Shutdown());
        Assert.Equal(new[] { "scopes", "plugin", "host" }, calls);
        if (fail) Assert.Throws<AggregateException>(runtime.Dispose);
        else runtime.Dispose();
        Assert.Equal(3, calls.Count);
    }

    [Fact]
    public async Task 无法关闭文档Scope时保留Provider且不伪装成功()
    {
        var disposed = 0;
        var retention = new HostResourceRetention();
        var shutdown = new HostRuntimeShutdown(new Cleanup(() => disposed++), new Cleanup(() => disposed++),
            () => throw new InvalidOperationException("test"), new HostShutdownParticipants(), retention, null);
        var result = await shutdown.RunAsync();
        Assert.True(result.ResourcesRetained);
        Assert.Single(result.Failures);
        Assert.Equal(0, disposed);
        Assert.Equal(1, retention.Count);
    }

    private sealed class Cleanup(Action clean) : IDisposable { public void Dispose() => clean(); }
}
