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
        Assert.Equal(PluginLifecycleStatus.Failed, manager.GetState(new PluginId("broken"))?.Status);
        Assert.Equal(PluginLifecycleStatus.Stopped, manager.GetState(new PluginId("healthy"))?.Status);
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
        Assert.Equal(PluginLifecycleStatus.Stopped, manager.GetState(new PluginId("yielding"))?.Status);
    }

    [Fact]
    public async Task 显式依赖优先于Order且同层仍使用稳定顺序()
    {
        var calls = new List<string>();
        var manager = new PluginLifecycleManager([
            new RecordingLifecycle("dependent", 0, calls, dependencies: ["foundation"]),
            new RecordingLifecycle("z-independent", 20, calls),
            new RecordingLifecycle("foundation", 100, calls),
            new RecordingLifecycle("a-independent", 20, calls),
        ]);

        await manager.InitializeAllAsync();

        Assert.Equal([
            "init:a-independent",
            "init:z-independent",
            "init:foundation",
            "init:dependent",
        ], calls);
        Assert.Equal(
            ["foundation"],
            manager.GetState(new PluginId("dependent"))?.RequiredPluginIds.Select(id => id.Value));
    }

    [Fact]
    public async Task 缺失依赖和循环依赖被阻塞但独立插件继续初始化()
    {
        var calls = new List<string>();
        var manager = new PluginLifecycleManager([
            new RecordingLifecycle("missing", 0, calls, dependencies: ["not-installed"]),
            new RecordingLifecycle("cycle-a", 0, calls, dependencies: ["cycle-b"]),
            new RecordingLifecycle("cycle-b", 0, calls, dependencies: ["cycle-a"]),
            new RecordingLifecycle("cycle-dependent", 0, calls, dependencies: ["cycle-a"]),
            new RecordingLifecycle("healthy", 0, calls),
        ]);

        await manager.InitializeAllAsync();

        Assert.Equal(["init:healthy"], calls);
        Assert.Equal(PluginLifecycleStatus.Blocked, manager.GetState(new PluginId("missing"))?.Status);
        Assert.Equal(
            "LIFECYCLE_DEPENDENCY_MISSING",
            manager.GetState(new PluginId("missing"))?.ErrorCode);
        Assert.Equal(PluginLifecycleStatus.Blocked, manager.GetState(new PluginId("cycle-a"))?.Status);
        Assert.Equal(
            "LIFECYCLE_DEPENDENCY_CYCLE",
            manager.GetState(new PluginId("cycle-b"))?.ErrorCode);
        Assert.Equal(
            "cycle-a",
            manager.GetState(new PluginId("cycle-dependent"))?.BlockingPluginId?.Value);
    }

    [Fact]
    public async Task 上游初始化失败只阻塞下游且不影响独立分支()
    {
        var calls = new List<string>();
        var manager = new PluginLifecycleManager([
            new RecordingLifecycle("broken", 0, calls, failInitialization: true),
            new RecordingLifecycle("dependent", 0, calls, dependencies: ["broken"]),
            new RecordingLifecycle("healthy", 0, calls),
        ]);

        await manager.InitializeAllAsync();

        Assert.Equal(["init:broken", "init:healthy"], calls);
        Assert.Equal(PluginLifecycleStatus.Failed, manager.GetState(new PluginId("broken"))?.Status);
        Assert.Equal(PluginLifecycleStatus.Blocked, manager.GetState(new PluginId("dependent"))?.Status);
        Assert.Equal("broken", manager.GetState(new PluginId("dependent"))?.BlockingPluginId?.Value);
        Assert.True(manager.GetState(new PluginId("healthy"))?.IsAvailable);
    }

    [Fact]
    public async Task 重复PluginId不会选择任意实例执行()
    {
        var calls = new List<string>();
        var manager = new PluginLifecycleManager([
            new RecordingLifecycle("duplicate", 0, calls),
            new RecordingLifecycle("duplicate", 1, calls),
        ]);

        await manager.InitializeAllAsync();

        Assert.Empty(calls);
        Assert.Equal(PluginLifecycleStatus.Failed, manager.GetState(new PluginId("duplicate"))?.Status);
        Assert.Equal(
            "LIFECYCLE_PLUGIN_ID_DUPLICATE",
            manager.GetState(new PluginId("duplicate"))?.ErrorCode);
    }

    [Fact]
    public async Task 初始化超时阻塞下游但迟到完成不会覆盖超时状态()
    {
        var calls = new List<string>();
        var slow = new ControllableLifecycle("slow", calls);
        var manager = new PluginLifecycleManager(
            [
                slow,
                new RecordingLifecycle("dependent", 1, calls, dependencies: ["slow"]),
                new RecordingLifecycle("healthy", 2, calls),
            ],
            new PluginLifecycleOptions
            {
                InitializationTimeout = TimeSpan.FromMilliseconds(50),
                ShutdownTimeout = TimeSpan.FromMilliseconds(50),
            });

        await manager.InitializeAllAsync();
        slow.CompleteInitialization();
        await Task.Yield();

        Assert.Equal(PluginLifecycleStatus.TimedOut, manager.GetState(new PluginId("slow"))?.Status);
        Assert.Equal("LIFECYCLE_UNRESPONSIVE", manager.GetState(new PluginId("slow"))?.ErrorCode);
        Assert.Equal(PluginLifecycleStatus.Blocked, manager.GetState(new PluginId("dependent"))?.Status);
        Assert.Equal(PluginLifecycleStatus.Ready, manager.GetState(new PluginId("healthy"))?.Status);
        Assert.Contains("init:healthy", calls);
    }

    [Fact]
    public async Task 关闭超时不会阻止更早初始化的插件继续关闭()
    {
        var calls = new List<string>();
        var manager = new PluginLifecycleManager(
            [
                new RecordingLifecycle("first", 0, calls),
                new HangingShutdownLifecycle("second", 1, calls),
            ],
            new PluginLifecycleOptions
            {
                InitializationTimeout = TimeSpan.FromMilliseconds(100),
                ShutdownTimeout = TimeSpan.FromMilliseconds(50),
            });

        await manager.InitializeAllAsync();
        await manager.ShutdownAllAsync();

        Assert.Equal([
            "init:first",
            "init:second",
            "shutdown:second",
            "shutdown:first",
        ], calls);
        Assert.Equal(PluginLifecycleStatus.TimedOut, manager.GetState(new PluginId("second"))?.Status);
        Assert.Equal(PluginLifecycleStatus.Stopped, manager.GetState(new PluginId("first"))?.Status);
    }

    [Fact]
    public async Task 宿主取消会停止调度后续插件并向调用方传播()
    {
        var calls = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var manager = new PluginLifecycleManager([
            new CancellationAwareLifecycle("waiting", calls),
            new RecordingLifecycle("never-started", 1, calls),
        ]);

        var initialization = manager.InitializeAllAsync(cancellation.Token);
        await Task.Delay(20);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialization);
        Assert.DoesNotContain("init:never-started", calls);
        Assert.Equal(
            PluginLifecycleStatus.Failed,
            manager.GetState(new PluginId("waiting"))?.Status);
        Assert.Equal(
            "LIFECYCLE_HOST_CANCELLED",
            manager.GetState(new PluginId("waiting"))?.ErrorCode);
    }

    private sealed class RecordingLifecycle :
        IPluginLifecycle,
        IPluginLifecycleDependencies
    {
        private readonly List<string> _calls;
        private readonly bool _failInitialization;

        public RecordingLifecycle(
            string pluginId,
            int order,
            List<string> calls,
            bool failInitialization = false,
            IReadOnlyCollection<string>? dependencies = null)
        {
            PluginId = new PluginId(pluginId);
            Order = order;
            _calls = calls;
            _failInitialization = failInitialization;
            RequiredPluginIds = (dependencies ?? []).Select(id => new PluginId(id)).ToArray();
        }

        public PluginId PluginId { get; }

        public int Order { get; }

        public IReadOnlyCollection<PluginId> RequiredPluginIds { get; }

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

    private sealed class ControllableLifecycle(
        string pluginId,
        List<string> calls) : IPluginLifecycle
    {
        private readonly TaskCompletionSource _initialization = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public PluginId PluginId { get; } = new(pluginId);

        public int Order => 0;

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            calls.Add($"init:{PluginId}");
            return _initialization.Task;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            calls.Add($"shutdown:{PluginId}");
            return Task.CompletedTask;
        }

        public void CompleteInitialization() => _initialization.TrySetResult();
    }

    private sealed class HangingShutdownLifecycle(
        string pluginId,
        int order,
        List<string> calls) : IPluginLifecycle
    {
        public PluginId PluginId { get; } = new(pluginId);

        public int Order => order;

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            calls.Add($"init:{PluginId}");
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            calls.Add($"shutdown:{PluginId}");
            return new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }

    private sealed class CancellationAwareLifecycle(
        string pluginId,
        List<string> calls) : IPluginLifecycle
    {
        public PluginId PluginId { get; } = new(pluginId);

        public int Order => 0;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            calls.Add($"init:{PluginId}");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task ShutdownAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class YieldingShutdownLifecycle : IPluginLifecycle
    {
        public PluginId PluginId { get; } = new("yielding");

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
