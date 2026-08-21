using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginTests;

public sealed class PluginLifecycleCompatibilityAdapterTests
{
    [Fact]
    public async Task Provider边界把最终Sdk生命周期收窄为内部回调()
    {
        var lifecycle = new SdkLifecycle();
        var callbacks = PluginProviderOwner.CreateLifecycleCallbacks(
            lifecycle,
            lifecycle.GetType());

        await callbacks.InitializeAsync(CancellationToken.None);
        await callbacks.ShutdownAsync(CancellationToken.None);

        Assert.Equal(2, lifecycle.CallCount);
    }

    [Fact]
    public async Task Provider边界仅为G12前业务插件适配Legacy启动关闭接口()
    {
        var lifecycle = new LegacyLifecycle();
        var callbacks = PluginProviderOwner.CreateLifecycleCallbacks(
            lifecycle,
            lifecycle.GetType());

        await callbacks.InitializeAsync(CancellationToken.None);
        await callbacks.ShutdownAsync(CancellationToken.None);

        Assert.Equal(2, lifecycle.CallCount);
    }

    [Fact]
    public void 非生命周期对象被明确拒绝且空参数不进入适配()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PluginProviderOwner.CreateLifecycleCallbacks(null!, typeof(object)));
        Assert.Throws<ArgumentNullException>(() =>
            PluginProviderOwner.CreateLifecycleCallbacks(new object(), null!));
        Assert.Throws<InvalidOperationException>(() =>
            PluginProviderOwner.CreateLifecycleCallbacks(new object(), typeof(object)));
    }

    private sealed class SdkLifecycle : IPluginLifecycle
    {
        internal int CallCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class LegacyLifecycle : MyAvaloniaManagementCommon.Plugin.IPluginLifecycle
    {
        internal int CallCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
