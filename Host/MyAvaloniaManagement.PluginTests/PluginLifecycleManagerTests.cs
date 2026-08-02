using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.PluginTests;

public sealed class PluginLifecycleManagerTests
{
    [Fact]
    public async Task 初始化按顺序执行_关闭按相反顺序执行()
    {
        var calls = new List<string>();
        var manager = new PluginLifecycleManager([
            new RecordingLifecycle("second", 20, calls),
            new RecordingLifecycle("first", 10, calls),
        ]);

        await manager.InitializeAllAsync();
        await manager.ShutdownAllAsync();

        Assert.Equal([
            "init:first",
            "init:second",
            "shutdown:second",
            "shutdown:first",
        ], calls);
    }

    [Fact]
    public async Task 初始化与关闭均为幂等操作()
    {
        var calls = new List<string>();
        var manager = new PluginLifecycleManager([
            new RecordingLifecycle("only", 0, calls),
        ]);

        await Task.WhenAll(manager.InitializeAllAsync(), manager.InitializeAllAsync());
        await Task.WhenAll(manager.ShutdownAllAsync(), manager.ShutdownAllAsync());

        Assert.Equal(["init:only", "shutdown:only"], calls);
    }

    [Fact]
    public async Task 单个插件初始化失败_不会阻止其他插件且失败插件不参与关闭()
    {
        var calls = new List<string>();
        var manager = new PluginLifecycleManager([
            new RecordingLifecycle("broken", 0, calls, failInitialization: true),
            new RecordingLifecycle("healthy", 1, calls),
        ]);

        await manager.InitializeAllAsync();
        await manager.ShutdownAllAsync();

        Assert.Equal([
            "init:broken",
            "init:healthy",
            "shutdown:healthy",
        ], calls);
        Assert.Equal(PluginLifecycleStatus.Failed, manager.GetState("broken")?.Status);
        Assert.Equal(PluginLifecycleStatus.Stopped, manager.GetState("healthy")?.Status);
    }

    [Fact]
    public async Task Avalonia消息循环结束后_异步插件仍能完成关闭()
    {
        var lifecycle = new YieldingShutdownLifecycle();
        var manager = new PluginLifecycleManager([lifecycle]);
        await manager.InitializeAllAsync();

        Exception? shutdownError = null;
        var shutdownCompleted = false;
        var contextCleared = false;
        var thread = new Thread(() =>
        {
            var stoppedUiContext = new NonPumpingSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(stoppedUiContext);
            try
            {
                global::MyAvaloniaManagement.Program.ShutdownPlugins(manager);
                shutdownCompleted = true;
                contextCleared = SynchronizationContext.Current is null;
            }
            catch (Exception ex)
            {
                shutdownError = ex;
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "插件关闭发生死锁。");
        Assert.Null(shutdownError);
        Assert.True(shutdownCompleted);
        Assert.True(contextCleared);
        Assert.True(lifecycle.ShutdownCompleted);
        Assert.Equal(PluginLifecycleStatus.Stopped, manager.GetState("yielding")?.Status);
    }

    private sealed class RecordingLifecycle : IPluginLifecycle
    {
        private readonly List<string> _calls;
        private readonly bool _failInitialization;

        public RecordingLifecycle(
            string pluginId,
            int order,
            List<string> calls,
            bool failInitialization = false)
        {
            PluginId = pluginId;
            Order = order;
            _calls = calls;
            _failInitialization = failInitialization;
        }

        public string PluginId { get; }

        public int Order { get; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _calls.Add($"init:{PluginId}");
            return _failInitialization
                ? Task.FromException(new InvalidOperationException("预期的初始化失败"))
                : Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _calls.Add($"shutdown:{PluginId}");
            return Task.CompletedTask;
        }
    }

    private sealed class YieldingShutdownLifecycle : IPluginLifecycle
    {
        public string PluginId => "yielding";

        public int Order => 0;

        public bool ShutdownCompleted { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken);
            ShutdownCompleted = true;
        }
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // 模拟 StartWithClassicDesktopLifetime 返回后已经停止处理消息的 UI 上下文。
        }
    }
}
