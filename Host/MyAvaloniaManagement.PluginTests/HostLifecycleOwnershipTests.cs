using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;
using Xunit;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>用真实 DI 释放探针和实际 Runtime 回滚边界验证所有权，而不是只断言超时状态。</summary>
public sealed class HostLifecycleOwnershipTests
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Done(CancellationToken _) => Task.CompletedTask;

    [Fact]
    public async Task 调用方取消Shutdown等待_当前项使用剩余宽限而后续项不再调度()
    {
        var cancelled = Signal();
        var finish = Signal();
        var calls = new List<string>();
        using var cancellation = new CancellationTokenSource();
        using var fixture = new Fixture(calls,
            ("a", Done, _ => { calls.Add("stop:a"); return Task.CompletedTask; }),
            ("b", Done, token => { token.Register(() => cancelled.TrySetResult()); return finish.Task; }));
        await fixture.Lifecycles.InitializeAllAsync();
        var close = fixture.Lifecycles.ShutdownAllAsync(cancellation.Token);
        cancellation.Cancel();
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Time.WaitForTimerAsync(Grace);
            Assert.False(close.IsCompleted);
        }
        finally { finish.TrySetResult(); }
        var result = await close.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.CanReleaseProviders);
        Assert.DoesNotContain("stop:a", calls);
        Assert.All(result.Retentions, item => Assert.Equal("a", item.PluginId.Value));
        Assert.Equal(PluginLifecycleRetentionReason.ShutdownNotExecuted, Assert.Single(result.Retentions).Reason);
    }

    [Fact]
    public async Task 插件组合间接创建Workflow管理器_回滚使用已登记实例而不创建Workspace()
    {
        var participants = new HostShutdownParticipants();
        var services = new ServiceCollection();
        var owners = new PluginProviderOwner();
        var scopes = new DocumentScopeRegistry();
        services.AddApplicationServices(documentScopes: scopes, pluginProviders: owners, shutdownParticipants: participants);
        var workspaceFactories = 0;
        services.AddSingleton<WorkspaceSession>(_ => { workspaceFactories++; throw new InvalidOperationException("禁止冷解析"); });
        using var provider = services.BuildServiceProvider();
        var pluginServices = new ServiceCollection();
        var registration = new PluginRegistration(new PluginId("myavalonia.plugin.ownership-test"),
            pluginServices, new PluginRegistryBuilder());
        registration.UseWorkflowActionGateway();
        registration.Seal();
        PluginServiceCommitGuard.ValidateAndCommit(pluginServices, registration, provider);
        var actual = provider.GetRequiredService<WorkflowActionRunManager>();
        var snapshot = participants.Freeze();
        Assert.Same(actual, snapshot.Workflow);
        Assert.Null(snapshot.Workspace);
        var retention = new HostResourceRetention();
        var shutdown = new HostRuntimeShutdown(owners, provider, scopes.CloseAll, participants, retention, null);
        Assert.False((await shutdown.RunAsync()).ResourcesRetained);
        Assert.Equal(0, workspaceFactories);
        var rejected = Assert.Throws<InvalidOperationException>(() => actual.CreateRun(new PluginId("test")));
        Assert.Contains("关闭", rejected.Message);
    }

    [Fact]
    public async Task 关闭诊断出口等待正在报告的调用_关闭后不访问已结束会话()
    {
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        var closeEntered = Signal();
        var sink = new BlockingSink(entered, release);
        var reporter = new PluginLifecycleDiagnosticReporter(sink);
        var report = Task.Run(() => reporter.Report(new HostDiagnosticDraft(
            HostDiagnosticCodes.LifecycleCancellationFailed, HostDiagnosticPhase.PluginLifecycle)));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var close = Task.Run(() => { closeEntered.SetResult(); reporter.Close(); });
        try
        {
            await closeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(close.IsCompleted);
        }
        finally { release.Set(); }
        await Task.WhenAll(report, close).WaitAsync(TimeSpan.FromSeconds(10));
        sink.Disposed = true;
        reporter.Report(new HostDiagnosticDraft(HostDiagnosticCodes.LifecycleCancellationFailed, HostDiagnosticPhase.PluginLifecycle));
        Assert.Equal(1, sink.Count);
    }

    [Fact]
    public async Task 迟到取消异常在出口关闭后只被观察_不再写入日志()
    {
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        var finish = Signal();
        var sink = new CountingSink();
        var reporter = new PluginLifecycleDiagnosticReporter(sink);
        var time = new ManualLifecycleTimeProvider();
        PluginLifecycleOperation? operation = null;
        var run = new PluginLifecycleOperationRunner(Grace, time).RunAsync(token =>
        {
            token.Register(() => { entered.TrySetResult(); release.Wait(); throw new IOException("迟到异常"); });
            return finish.Task;
        }, ShutdownTimeout, CancellationToken.None, _ => reporter.Report(new HostDiagnosticDraft(
            HostDiagnosticCodes.LifecycleCancellationFailed, HostDiagnosticPhase.PluginLifecycle)), value => operation = value);
        try
        {
            time.Advance(ShutdownTimeout);
            await run;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            reporter.Close();
        }
        finally { finish.TrySetResult(); release.Set(); }
        await operation!.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void 初始化部分成功后解析下一项失败_回滚只关闭已成功项()
    {
        var calls = new List<string>();
        var registry = new PluginRegistry([], [], [], [
            new PluginLifecycleDeclaration(new PluginId("a"), typeof(Fixture)),
            new PluginLifecycleDeclaration(new PluginId("b"), typeof(Fixture))]);
        var states = new PluginLifecycleStateStore(registry);
        var coordinator = new PluginLifecycleCoordinator(registry, new PartialResolver(calls), states);
        var participants = new HostShutdownParticipants();
        participants.Record(coordinator);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var shutdown = new HostRuntimeShutdown(new Probe("plugin", calls), provider, () => { }, participants,
            new HostResourceRetention(), null);
        var runtime = new HostRuntime(provider, shutdown);
        var failure = Assert.Throws<InvalidOperationException>(() => HostRuntime.Initialize(runtime,
            () => coordinator.InitializeAllAsync().GetAwaiter().GetResult(), new RecordingSink()));
        Assert.Equal("resolve:b", failure.Message);
        Assert.Equal(["init:a", "stop:a", "plugin"], calls);
    }

    [Fact]
    public void 生命周期开始前启动失败_不误调Shutdown且完整释放()
    {
        var calls = new List<string>();
        using var fixture = new Fixture(calls, ("a", Done, _ => { calls.Add("stop:a"); return Task.CompletedTask; }));
        Assert.Throws<IOException>(() => HostRuntime.Initialize(fixture.Runtime, () => throw new IOException("compose"), fixture.Sink));
        Assert.Equal(["scopes", "plugin", "host"], calls);
    }

    [Fact]
    public async Task 正常关闭先停生命周期再释放两个真实容器_并发请求共用结果()
    {
        var calls = new List<string>();
        using var fixture = new Fixture(calls, ("a", Done, _ => { calls.Add("stop:a"); return Task.CompletedTask; }),
            ("b", Done, _ => { calls.Add("stop:b"); return Task.CompletedTask; }));
        await fixture.Lifecycles.InitializeAllAsync();
        var first = fixture.Shutdown.RunAsync();
        var second = fixture.Shutdown.RunAsync();
        Assert.Same(first, second);
        Assert.False((await first).ResourcesRetained);
        Assert.Equal(["scopes", "stop:b", "stop:a", "plugin", "host"], calls);
        Assert.Equal(0, fixture.Retention.Count);
    }

    [Fact]
    public async Task Shutdown超时宽限内成功_真实任务仍访问服务时不释放()
    {
        var finish = Signal();
        var cancelled = Signal();
        using var fixture = new Fixture([], ("a", Done, token =>
        {
            token.Register(() => cancelled.TrySetResult());
            return finish.Task;
        }));
        await fixture.Lifecycles.InitializeAllAsync();
        var close = fixture.Shutdown.RunAsync();
        fixture.Time.Advance(ShutdownTimeout);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(fixture.PluginProbe.Disposed);
        Assert.False(fixture.HostProbe.Disposed);
        finish.SetResult();
        Assert.False((await close.WaitAsync(TimeSpan.FromSeconds(10))).ResourcesRetained);
        Assert.True(fixture.PluginProbe.Disposed);
        Assert.Equal(PluginLifecycleStatus.ShutdownTimedOut, fixture.States.GetState(new PluginId("a"))!.Status);
    }

    [Fact]
    public async Task Shutdown持续挂起_宽限后保留且迟到成功不自动释放()
    {
        var finish = Signal();
        using var fixture = new Fixture([], ("a", Done, _ => finish.Task));
        await fixture.Lifecycles.InitializeAllAsync();
        var close = fixture.Shutdown.RunAsync();
        fixture.Time.Advance(ShutdownTimeout);
        await fixture.Time.WaitForTimerAsync(Grace);
        fixture.Time.Advance(Grace);
        var result = await close.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.ResourcesRetained);
        Assert.Contains(result.Lifecycles!.Retentions, item => item.Reason == PluginLifecycleRetentionReason.OperationRunning);
        Assert.Equal(1, fixture.Retention.Count);
        finish.SetResult();
        Assert.Same(result, await fixture.Shutdown.RunAsync());
        Assert.False(fixture.PluginProbe.Disposed);
        Assert.False(fixture.HostProbe.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown已失败或取消_保守政策保留但不能误报仍在运行(bool cancelled)
    {
        using var fixture = new Fixture([], ("a", Done, _ => cancelled
            ? Task.FromCanceled(new CancellationToken(true)) : Task.FromException(new IOException("flush"))));
        await fixture.Lifecycles.InitializeAllAsync();
        var result = await fixture.Shutdown.RunAsync();
        Assert.True(result.ResourcesRetained);
        Assert.Equal(PluginLifecycleRetentionReason.ShutdownFailed, Assert.Single(result.Lifecycles!.Retentions).Reason);
        Assert.False(fixture.PluginProbe.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 原任务已结束但取消通知仍阻塞_不能释放取消资源或Provider(bool hostCancellation)
    {
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        var finish = Signal();
        using var cancellation = new CancellationTokenSource();
        PluginLifecycleOperation? operation = null;
        var clock = new ManualLifecycleTimeProvider();
        var runner = new PluginLifecycleOperationRunner(Grace, clock);
        var run = runner.RunAsync(token =>
        {
            token.Register(() => { entered.TrySetResult(); release.Wait(); });
            return finish.Task;
        }, ShutdownTimeout, cancellation.Token, track: value => operation = value);
        try
        {
            if (hostCancellation) cancellation.Cancel();
            else clock.Advance(ShutdownTimeout);
            if (hostCancellation) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            else Assert.Equal(PluginLifecycleOperationOutcome.TimedOut, (await run).Outcome);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            finish.SetResult();
            var drain = operation!.WaitForCompletionAsync();
            await clock.WaitForTimerAsync(Grace);
            clock.Advance(Grace);
            Assert.False(await drain);
            Assert.False(operation.Completion.IsCompleted);
        }
        finally
        {
            finish.TrySetResult();
            release.Set();
            await operation!.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.True(await operation.WaitForCompletionAsync());
    }

    [Fact]
    public async Task 宽限截止时间不会因重复请求重新计算()
    {
        var finish = Signal();
        var clock = new ManualLifecycleTimeProvider();
        PluginLifecycleOperation? operation = null;
        var runner = new PluginLifecycleOperationRunner(Grace, clock);
        var run = runner.RunAsync(_ => finish.Task, ShutdownTimeout, CancellationToken.None, track: value => operation = value);
        clock.Advance(ShutdownTimeout);
        await run;
        clock.Advance(Grace);
        Assert.False(await operation!.WaitForCompletionAsync());
        operation.RequestCancellation();
        Assert.False(await operation.WaitForCompletionAsync());
        finish.SetResult();
        await operation.Completion.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task 初始化迟到成功在关闭位置纳入_仍按声明逆序且不可用()
    {
        var finish = Signal();
        var calls = new List<string>();
        using var fixture = new Fixture(calls,
            ("a", _ => finish.Task, _ => { calls.Add("stop:a"); return Task.CompletedTask; }),
            ("b", Done, _ => { calls.Add("stop:b"); return Task.CompletedTask; }));
        var init = fixture.Lifecycles.InitializeAllAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(30));
        await init;
        var close = fixture.Shutdown.RunAsync();
        await fixture.Time.WaitForTimerAsync(Grace);
        finish.SetResult();
        var result = await close.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.ResourcesRetained);
        Assert.Equal(["scopes", "stop:b", "stop:a", "plugin", "host"], calls);
        Assert.False(fixture.States.IsAvailable(new PluginId("a")));
    }

    [Fact]
    public async Task 已越过关闭位置的初始化随后成功_最终检查不能误放行()
    {
        var late = Signal();
        var stopA = Signal();
        var calls = new List<string>();
        using var fixture = new Fixture(calls,
            ("a", Done, _ => { stopA.TrySetResult(); return Task.CompletedTask; }),
            ("b", _ => late.Task, _ => { calls.Add("stop:b"); return Task.CompletedTask; }));
        var init = fixture.Lifecycles.InitializeAllAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(30));
        await init;
        fixture.Time.Advance(Grace);
        var close = fixture.Shutdown.RunAsync();
        await stopA.Task.WaitAsync(TimeSpan.FromSeconds(10));
        late.SetResult();
        Assert.True((await close).ResourcesRetained);
        Assert.DoesNotContain("stop:b", calls);
        Assert.Same(close, fixture.Shutdown.RunAsync());
        Assert.False(fixture.PluginProbe.Disposed);
    }

    [Fact]
    public void 启动后半程失败_真实Runtime回滚调用已启动项并保留原始异常()
    {
        var calls = new List<string>();
        using var fixture = new Fixture(calls,
            ("a", Done, _ => { calls.Add("stop:a"); return Task.CompletedTask; }),
            ("b", Done, _ => { calls.Add("stop:b"); return Task.CompletedTask; }));
        var original = new InvalidOperationException("Workspace 工厂失败");
        Assert.Same(original, Assert.Throws<InvalidOperationException>(() => HostRuntime.Initialize(fixture.Runtime, () =>
        {
            fixture.Lifecycles.InitializeAllAsync().GetAwaiter().GetResult();
            throw original;
        }, fixture.Sink)));
        Assert.Equal(["scopes", "stop:b", "stop:a", "plugin", "host"], calls);
        Assert.Throws<ObjectDisposedException>(() => fixture.Runtime.BuildAvaloniaApp());
    }

    [Fact]
    public void 回滚Shutdown及诊断再次失败_不得覆盖启动异常或释放容器()
    {
        using var fixture = new Fixture([], ("a", Done, _ => Task.FromException(new IOException("stop"))));
        var original = new InvalidOperationException("startup");
        Assert.Same(original, Assert.Throws<InvalidOperationException>(() => HostRuntime.Initialize(fixture.Runtime, () =>
        {
            fixture.Lifecycles.InitializeAllAsync().GetAwaiter().GetResult();
            throw original;
        }, new ThrowingSink())));
        Assert.Equal(1, fixture.Retention.Count);
        Assert.False(fixture.HostProbe.Disposed);
    }

    [Fact]
    public async Task Scope清理失败仍尝试Shutdown_但保留对象图()
    {
        var stops = 0;
        using var fixture = new Fixture([], ("a", Done, _ => { stops++; return Task.CompletedTask; }));
        await fixture.Lifecycles.InitializeAllAsync();
        var shutdown = new HostRuntimeShutdown(fixture.PluginProvider, fixture.Provider,
            () => throw new IOException("scope"), fixture.Participants, fixture.Retention, fixture.Sink);
        Assert.True((await shutdown.RunAsync()).ResourcesRetained);
        Assert.Equal(1, stops);
        Assert.False(fixture.PluginProbe.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 业务调用未排空时不关闭生命周期也不释放Provider(bool command)
    {
        var stops = 0;
        using var fixture = new Fixture([], ("a", Done, _ => { stops++; return Task.CompletedTask; }));
        var participant = new BusinessParticipant { Drained = false };
        if (command) fixture.Participants.Record((IWorkbenchCommandShutdownParticipant)participant);
        else fixture.Participants.Record((IWorkflowActionShutdownParticipant)participant);
        await fixture.Lifecycles.InitializeAllAsync();
        Assert.True((await fixture.Shutdown.RunAsync()).ResourcesRetained);
        Assert.True(participant.Stopping);
        Assert.Equal(0, stops);
        Assert.False(fixture.PluginProbe.Disposed);
    }

    [Fact]
    public async Task 入口关闭异常仍尝试其他入口_不能放行Provider()
    {
        using var fixture = new Fixture([]);
        var commands = new BusinessParticipant();
        fixture.Participants.Record((IWorkflowActionShutdownParticipant)new BusinessParticipant { ThrowBegin = true });
        fixture.Participants.Record((IWorkbenchCommandShutdownParticipant)commands);
        Assert.True((await fixture.Shutdown.RunAsync()).ResourcesRetained);
        Assert.True(commands.Stopping);
        Assert.False(fixture.HostProbe.Disposed);
    }

    [Fact]
    public async Task 容器释放异常不阻止另一个容器释放_并汇总结果()
    {
        var host = new Probe("host", []);
        var shutdown = new HostRuntimeShutdown(new ThrowingDisposable(), host, () => { },
            new HostShutdownParticipants(), new HostResourceRetention(), null);
        var result = await shutdown.RunAsync();
        Assert.False(result.ResourcesRetained);
        Assert.Single(result.Failures);
        Assert.True(host.Disposed);
    }

    [Fact]
    public async Task 委托保持调用线程_自身TimeoutException应归为失败()
    {
        var runner = new PluginLifecycleOperationRunner();
        var caller = Environment.CurrentManagedThreadId;
        var actual = -1;
        var result = await runner.RunAsync(_ =>
        {
            actual = Environment.CurrentManagedThreadId;
            throw new TimeoutException("插件自身异常");
        }, ShutdownTimeout, CancellationToken.None);
        Assert.Equal(caller, actual);
        Assert.Equal(PluginLifecycleOperationOutcome.Failed, result.Outcome);
    }

    [Fact]
    public void 冻结参与者后拒绝新交付_重复冻结不创建服务()
    {
        var participants = new HostShutdownParticipants();
        var first = participants.Freeze();
        Assert.Same(first, participants.Freeze());
        Assert.Throws<InvalidOperationException>(() => participants.Record((IWorkflowActionShutdownParticipant)new BusinessParticipant()));
    }

    private sealed class Fixture : IDisposable
    {
        internal Fixture(List<string> calls,
            params (string Id, Func<CancellationToken, Task> Init, Func<CancellationToken, Task> Stop)[] callbacks)
        {
            PluginProbe = new Probe("plugin", calls);
            HostProbe = new Probe("host", calls);
            PluginProvider = new ServiceCollection().AddSingleton(_ => PluginProbe).BuildServiceProvider();
            Provider = new ServiceCollection().AddSingleton(_ => HostProbe).BuildServiceProvider();
            _ = PluginProvider.GetRequiredService<Probe>();
            _ = Provider.GetRequiredService<Probe>();
            var registry = new PluginRegistry([], [], [], callbacks.Select(item =>
                new PluginLifecycleDeclaration(new PluginId(item.Id), typeof(Fixture))).ToArray());
            States = new PluginLifecycleStateStore(registry);
            Lifecycles = new PluginLifecycleCoordinator(registry, new Resolver(callbacks.ToDictionary(
                item => new PluginId(item.Id), item => new PluginLifecycleCallbacks(item.Init, item.Stop))),
                States, Sink, new PluginLifecycleTimeouts(TimeSpan.FromSeconds(30), ShutdownTimeout)
                { CancellationGrace = Grace }, Time);
            Participants.Record(States);
            Participants.Record(Lifecycles);
            Shutdown = new HostRuntimeShutdown(PluginProvider, Provider, () => calls.Add("scopes"), Participants, Retention, Sink);
            Runtime = new HostRuntime(Provider, Shutdown);
        }

        internal ManualLifecycleTimeProvider Time { get; } = new();
        internal HostShutdownParticipants Participants { get; } = new();
        internal HostResourceRetention Retention { get; } = new();
        internal RecordingSink Sink { get; } = new();
        internal ServiceProvider Provider { get; }
        internal ServiceProvider PluginProvider { get; }
        internal Probe PluginProbe { get; }
        internal Probe HostProbe { get; }
        internal PluginLifecycleStateStore States { get; }
        internal PluginLifecycleCoordinator Lifecycles { get; }
        internal HostRuntimeShutdown Shutdown { get; }
        internal HostRuntime Runtime { get; }
        public void Dispose()
        {
            // 所有挂起假任务由用例先解除，再清理测试私有容器；不触碰进程保留所有者。
            Lifecycles.CloseDiagnostics();
            PluginProvider.Dispose();
            Provider.Dispose();
        }
    }

    private sealed class Resolver(Dictionary<PluginId, PluginLifecycleCallbacks> callbacks) : IPluginLifecycleResolver
    {
        public PluginLifecycleCallbacks GetRequiredLifecycle(PluginId pluginId, Type implementationType) => callbacks[pluginId];
    }
    private sealed class Probe(string name, List<string> calls) : IDisposable
    {
        internal bool Disposed { get; private set; }
        public void Dispose() { if (Disposed) return; Disposed = true; calls.Add(name); }
    }
    private sealed class BusinessParticipant : IWorkflowActionShutdownParticipant, IWorkbenchCommandShutdownParticipant
    {
        internal bool Drained { get; init; } = true;
        internal bool ThrowBegin { get; init; }
        internal bool Stopping { get; private set; }
        public TimeSpan ShutdownGrace => TimeSpan.FromSeconds(1);
        public void BeginShutdown() { Stopping = true; if (ThrowBegin) throw new IOException("begin"); }
        public Task<bool> WaitForDrainAsync(TimeSpan timeout) => Task.FromResult(Drained);
    }
    private sealed class RecordingSink : IHostDiagnosticSink
    {
        public HostDiagnosticRecord Report(HostDiagnosticDraft draft) =>
            HostDiagnosticRedactionPolicy.Create(Guid.NewGuid(), draft, DateTimeOffset.UtcNow);
    }
    private sealed class ThrowingSink : IHostDiagnosticSink
    {
        public HostDiagnosticRecord Report(HostDiagnosticDraft draft) => throw new IOException("diagnostics");
    }
    private sealed class ThrowingDisposable : IDisposable
    {
        public void Dispose() => throw new IOException("dispose");
    }
    private sealed class BlockingSink(TaskCompletionSource entered, ManualResetEventSlim release) : IHostDiagnosticSink
    {
        internal bool Disposed { get; set; }
        internal int Count { get; private set; }
        public HostDiagnosticRecord Report(HostDiagnosticDraft draft)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Count++;
            entered.TrySetResult();
            release.Wait();
            return HostDiagnosticRedactionPolicy.Create(Guid.NewGuid(), draft, DateTimeOffset.UtcNow);
        }
    }
    private sealed class CountingSink : IHostDiagnosticSink
    {
        internal int Count { get; private set; }
        public HostDiagnosticRecord Report(HostDiagnosticDraft draft)
        {
            Count++;
            return HostDiagnosticRedactionPolicy.Create(Guid.NewGuid(), draft, DateTimeOffset.UtcNow);
        }
    }
    private sealed class PartialResolver(List<string> calls) : IPluginLifecycleResolver
    {
        public PluginLifecycleCallbacks GetRequiredLifecycle(PluginId pluginId, Type implementationType)
        {
            if (pluginId.Value == "b") throw new InvalidOperationException("resolve:b");
            return new(_ => { calls.Add("init:a"); return Task.CompletedTask; },
                _ => { calls.Add("stop:a"); return Task.CompletedTask; });
        }
    }
}
