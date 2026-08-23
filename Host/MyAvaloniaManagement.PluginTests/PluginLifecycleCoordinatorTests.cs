using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using System.Collections.Concurrent;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 生命周期测试只通过 Registry 声明、解析端口、协调器和状态存储协作，避免重新制造已经删除的
/// public Manager 门面。这样能直接约束 G8 的 SOLID 边界，而不是只验证一个测试替身。
/// </summary>
public sealed class PluginLifecycleCoordinatorTests
{
    private static readonly PluginLifecycleTimeouts ShortTimeouts = new(
        TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(80));

    [Fact]
    public async Task 初始化按规范PluginId正序_关闭按实际成功顺序反向执行()
    {
        var calls = new List<string>();
        var second = new RecordingLifecycle("second", calls);
        var first = new RecordingLifecycle("first", calls);
        var fixture = CreateFixture(("second", second), ("first", first));

        await fixture.Coordinator.InitializeAllAsync();
        await fixture.Coordinator.ShutdownAllAsync();

        Assert.Equal([
            "init:first",
            "init:second",
            "shutdown:second",
            "shutdown:first",
        ], calls);
        Assert.Equal(
            PluginLifecycleStatus.Stopped,
            fixture.States.GetState(new PluginId("first"))?.Status);
        Assert.False(fixture.Availability.IsAvailable(new PluginId("first")));
    }

    [Fact]
    public async Task 初始化与关闭的并发重复调用均为幂等操作()
    {
        var calls = new List<string>();
        var fixture = CreateFixture(("only", new RecordingLifecycle("only", calls)));

        await Task.WhenAll(
            fixture.Coordinator.InitializeAllAsync(),
            fixture.Coordinator.InitializeAllAsync());
        await Task.WhenAll(
            fixture.Coordinator.ShutdownAllAsync(),
            fixture.Coordinator.ShutdownAllAsync());

        Assert.Equal(["init:only", "shutdown:only"], calls);
    }

    [Fact]
    public async Task 单项初始化失败只隔离当前插件_后续插件继续且失败项不参与关闭()
    {
        var calls = new List<string>();
        var fixture = CreateFixture(
            ("broken", new RecordingLifecycle("broken", calls, failInitialization: true)),
            ("healthy", new RecordingLifecycle("healthy", calls)));

        await fixture.Coordinator.InitializeAllAsync();
        await fixture.Coordinator.ShutdownAllAsync();

        Assert.Equal([
            "init:broken",
            "init:healthy",
            "shutdown:healthy",
        ], calls);
        Assert.False(fixture.Availability.IsAvailable(new PluginId("broken")));
        Assert.Equal(
            HostDiagnosticCodes.LifecycleInitializeFailed,
            fixture.States.GetState(new PluginId("broken"))?.ErrorCode);
    }

    [Fact]
    public async Task 初始化超时请求协作取消_迟到完成不得覆盖已提交状态()
    {
        var slow = new ControllableLifecycle();
        var fixture = CreateFixture(("slow", slow));

        await fixture.Coordinator.InitializeAllAsync();
        await WaitUntilAsync(() => slow.CancellationRequested);
        slow.CompleteInitialization();
        await Task.Yield();

        var state = fixture.States.GetState(new PluginId("slow"));
        Assert.Equal(PluginLifecycleStatus.InitializationTimedOut, state?.Status);
        Assert.Equal(HostDiagnosticCodes.LifecycleInitializeTimeout, state?.ErrorCode);
        Assert.False(fixture.Availability.IsAvailable(new PluginId("slow")));
    }

    [Fact]
    public async Task 关闭失败和超时都不阻断后续反向清理()
    {
        var calls = new List<string>();
        var hangingShutdown = new HangingShutdownLifecycle("third", calls);
        var fixture = CreateFixture(
            ("first", new RecordingLifecycle("first", calls)),
            ("second", new RecordingLifecycle("second", calls, failShutdown: true)),
            ("third", hangingShutdown));

        await fixture.Coordinator.InitializeAllAsync();
        await fixture.Coordinator.ShutdownAllAsync();
        await WaitUntilAsync(() => hangingShutdown.CancellationRequested);

        Assert.Equal([
            "init:first", "init:second", "init:third",
            "shutdown:third", "shutdown:second", "shutdown:first",
        ], calls);
        Assert.Equal(
            PluginLifecycleStatus.ShutdownTimedOut,
            fixture.States.GetState(new PluginId("third"))?.Status);
        Assert.Equal(
            PluginLifecycleStatus.ShutdownFailed,
            fixture.States.GetState(new PluginId("second"))?.Status);
        Assert.Equal(
            PluginLifecycleStatus.Stopped,
            fixture.States.GetState(new PluginId("first"))?.Status);
    }

    [Fact]
    public async Task 宿主取消停止后续调度_记录固定状态并向调用方传播()
    {
        var calls = new List<string>();
        var waiting = new CancellationAwareLifecycle("a-waiting", calls);
        var fixture = CreateFixture(
            ("a-waiting", waiting),
            ("z-never-started", new RecordingLifecycle("z-never-started", calls)));
        using var cancellation = new CancellationTokenSource();

        var initialization = fixture.Coordinator.InitializeAllAsync(cancellation.Token);
        await waiting.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialization);
        Assert.DoesNotContain("init:z-never-started", calls);
        var state = fixture.States.GetState(new PluginId("a-waiting"));
        Assert.Equal(PluginLifecycleStatus.HostCancelled, state?.Status);
        Assert.Equal(HostDiagnosticCodes.LifecycleHostCancelled, state?.ErrorCode);
    }

    [Fact]
    public async Task 执行器把同步抛错和空Task归类为失败()
    {
        var runner = new PluginLifecycleOperationRunner();

        var synchronous = await runner.RunAsync(
            _ => throw new InvalidOperationException("同步失败正文"),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        var nullTask = await runner.RunAsync(
            _ => null!,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(PluginLifecycleOperationOutcome.Failed, synchronous.Outcome);
        Assert.IsType<InvalidOperationException>(synchronous.Exception);
        Assert.Equal(PluginLifecycleOperationOutcome.Failed, nullTask.Outcome);
        Assert.IsType<InvalidOperationException>(nullTask.Exception);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.RunAsync(
            _ => Task.CompletedTask,
            TimeSpan.Zero,
            CancellationToken.None));
    }

    [Fact]
    public void 默认期限构造与非法期限边界保持稳定()
    {
        var registry = new PluginRegistry([], []);
        var states = new PluginLifecycleStateStore(registry);
        var resolver = new DictionaryLifecycleResolver(
            new Dictionary<PluginId, IPluginLifecycle>());

        _ = new PluginLifecycleCoordinator(registry, resolver, states);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PluginLifecycleCoordinator(
            registry,
            resolver,
            states,
            diagnostics: null,
            timeouts: new PluginLifecycleTimeouts(TimeSpan.Zero, TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public async Task 超时取消回调异常只形成脱敏诊断_不改变超时提交状态()
    {
        var sink = new RecordingDiagnosticSink();
        var fixture = CreateFixture(
            sink,
            ("throwing-cancel", new ThrowingCancellationLifecycle()));

        await fixture.Coordinator.InitializeAllAsync();
        var cancellationFailure = await sink.WaitForAsync(
            record => record.Code == HostDiagnosticCodes.LifecycleCancellationFailed,
            TimeSpan.FromSeconds(10));

        Assert.Equal(
            PluginLifecycleStatus.InitializationTimedOut,
            fixture.States.GetState(new PluginId("throwing-cancel"))?.Status);
        Assert.Equal("stage=Initialization", cancellationFailure.TechnicalDetail);
    }

    [Fact]
    public async Task 消息循环停止后的同步关闭不依赖UI上下文泵送()
    {
        var lifecycle = new YieldingShutdownLifecycle();
        var fixture = CreateFixture(("yielding", lifecycle));
        await fixture.Coordinator.InitializeAllAsync();
        Exception? failure = null;
        var completed = false;
        var contextCleared = false;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new NonPumpingSynchronizationContext());
            try
            {
                // HostRuntime 在真实退出路径先执行同样的清除，再同步等待 internal 协调器。
                SynchronizationContext.SetSynchronizationContext(null);
                fixture.Coordinator.ShutdownAllAsync().GetAwaiter().GetResult();
                completed = true;
                contextCleared = SynchronizationContext.Current is null;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };

        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(2)), "生命周期关闭发生死锁。");
        Assert.Null(failure);
        Assert.True(completed);
        Assert.True(contextCleared);
        Assert.True(lifecycle.ShutdownCompleted);
    }

    [Fact]
    public void 无生命周期插件立即可用且Availability不认识Host身份_停止阶段禁止新激活()
    {
        var owner = new PluginId("myavalonia.plugin.lifecycle-ready");
        var registry = new PluginRegistry(
            [],
            [new PluginToolRegistration(
                owner,
                new ToolDescriptor(
                    new ToolTypeId("myavalonia.plugin.lifecycle-ready.tool.sample"),
                    "示例",
                    "示例",
                    ToolDockSide.Left,
                    ToolCloseBehavior.Hide),
                typeof(object),
                typeof(Avalonia.Controls.UserControl),
                static () => new Avalonia.Controls.UserControl())]);
        var states = new PluginLifecycleStateStore(registry);
        var availability = new PluginAvailabilityReadModel(states);

        Assert.True(availability.IsAvailable(owner));
        Assert.False(availability.IsAvailable(new PluginId("myavalonia.host")));

        states.BeginShutdown();

        Assert.False(availability.IsAvailable(owner));
    }

    [Fact]
    public async Task 诊断只保留稳定码阶段耗时和异常类型_不泄漏异常正文()
    {
        const string secret = "G8-private-token-and-local-path";
        var sink = new RecordingDiagnosticSink();
        var fixture = CreateFixture(
            sink,
            ("sensitive", new SensitiveFailureLifecycle(secret)));

        await fixture.Coordinator.InitializeAllAsync();

        var record = Assert.Single(sink.Records);
        Assert.Equal(HostDiagnosticCodes.LifecycleInitializeFailed, record.Code);
        Assert.Contains("stage=Initialization", record.TechnicalDetail, StringComparison.Ordinal);
        Assert.Contains("durationMs=", record.TechnicalDetail, StringComparison.Ordinal);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
        Assert.DoesNotContain(
            secret,
            System.Text.Json.JsonSerializer.Serialize(record),
            StringComparison.Ordinal);
    }

    private static LifecycleFixture CreateFixture(
        params (string PluginId, IPluginLifecycle Lifecycle)[] lifecycles) =>
        CreateFixture(null, lifecycles);

    private static LifecycleFixture CreateFixture(
        IHostDiagnosticSink? diagnostics,
        params (string PluginId, IPluginLifecycle Lifecycle)[] lifecycles)
    {
        var declarations = lifecycles
            .Select(item => new PluginLifecycleDeclaration(
                new PluginId(item.PluginId),
                item.Lifecycle.GetType()))
            .ToArray();
        var registry = new PluginRegistry([], [], [], declarations);
        var states = new PluginLifecycleStateStore(registry);
        var resolver = new DictionaryLifecycleResolver(lifecycles.ToDictionary(
            item => new PluginId(item.PluginId),
            item => item.Lifecycle));
        var coordinator = new PluginLifecycleCoordinator(
            registry,
            resolver,
            states,
            diagnostics,
            ShortTimeouts);
        return new LifecycleFixture(
            coordinator,
            states,
            new PluginAvailabilityReadModel(states));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // 覆盖率采集和两轮隔离门禁会显著增加同机 I/O 与线程调度压力。
        // 这里等待的是已经提交的异步诊断，而不是业务超时本身；放宽观察窗口不会
        // 改变生命周期的短超时配置或最终状态断言，只避免机器负载造成测试假阴性。
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed record LifecycleFixture(
        PluginLifecycleCoordinator Coordinator,
        PluginLifecycleStateStore States,
        PluginAvailabilityReadModel Availability);

    private sealed class DictionaryLifecycleResolver(
        IReadOnlyDictionary<PluginId, IPluginLifecycle> lifecycles) : IPluginLifecycleResolver
    {
        public PluginLifecycleCallbacks GetRequiredLifecycle(
            PluginId pluginId,
            Type implementationType)
        {
            var lifecycle = lifecycles[pluginId];
            return new PluginLifecycleCallbacks(
                lifecycle.InitializeAsync,
                lifecycle.ShutdownAsync);
        }
    }

    private sealed class RecordingLifecycle(
        string pluginId,
        List<string> calls,
        bool failInitialization = false,
        bool failShutdown = false) : IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            calls.Add($"init:{pluginId}");
            return failInitialization
                ? Task.FromException(new InvalidOperationException("预期初始化失败"))
                : Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            calls.Add($"shutdown:{pluginId}");
            return failShutdown
                ? Task.FromException(new InvalidOperationException("预期关闭失败"))
                : Task.CompletedTask;
        }
    }

    private sealed class HangingShutdownLifecycle(
        string pluginId,
        List<string> calls) : IPluginLifecycle
    {
        internal bool CancellationRequested { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            calls.Add($"init:{pluginId}");
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            calls.Add($"shutdown:{pluginId}");
            cancellationToken.Register(() => CancellationRequested = true);
            return new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }

    private sealed class ControllableLifecycle : IPluginLifecycle
    {
        private readonly TaskCompletionSource _initialization = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationRequested { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => CancellationRequested = true);
            return _initialization.Task;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void CompleteInitialization() => _initialization.TrySetResult();
    }

    private sealed class CancellationAwareLifecycle(
        string pluginId,
        List<string> calls) : IPluginLifecycle
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            calls.Add($"init:{pluginId}");
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SensitiveFailureLifecycle(string secret) : IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(secret));

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class YieldingShutdownLifecycle : IPluginLifecycle
    {
        public bool ShutdownCompleted { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(10, cancellationToken);
            ShutdownCompleted = true;
        }
    }

    private sealed class ThrowingCancellationLifecycle : IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.Register(static () =>
                throw new InvalidOperationException("取消回调敏感正文"));
            return new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // 模拟 Avalonia 消息循环结束后不再处理队列的 UI 上下文。
        }
    }

    private sealed class RecordingDiagnosticSink : IHostDiagnosticSink
    {
        private readonly ConcurrentQueue<HostDiagnosticRecord> _records = new();
        private readonly SemaphoreSlim _recordAvailable = new(0);

        /// <summary>
        /// 返回当前记录的稳定快照。取消回调由后台线程报告，普通 List 在一边追加、一边
        /// 枚举时既没有跨线程可见性保证，也可能破坏枚举器；快照让普通断言只读取已发布事实。
        /// </summary>
        internal IReadOnlyList<HostDiagnosticRecord> Records => _records.ToArray();

        /// <summary>
        /// 等待一条满足条件的诊断，而不是按固定间隔轮询共享集合。
        /// SemaphoreSlim 同时承担唤醒和内存屏障职责：每次报告后唤醒观察方，观察方再对完整
        /// 快照做条件判断。这样仍保留明确的测试截止时间，但不会把调度速度误当成业务结果。
        /// </summary>
        internal async Task<HostDiagnosticRecord> WaitForAsync(
            Func<HostDiagnosticRecord, bool> predicate,
            TimeSpan timeout)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            using var cancellation = new CancellationTokenSource(timeout);
            while (true)
            {
                var matched = _records.FirstOrDefault(predicate);
                if (matched is not null)
                {
                    return matched;
                }

                await _recordAvailable.WaitAsync(cancellation.Token);
            }
        }

        public HostDiagnosticRecord Report(HostDiagnosticDraft diagnostic)
        {
            var record = HostDiagnosticRedactionPolicy.Create(
                Guid.NewGuid(),
                diagnostic,
                DateTimeOffset.UtcNow);
            _records.Enqueue(record);
            _recordAvailable.Release();
            return record;
        }
    }
}
