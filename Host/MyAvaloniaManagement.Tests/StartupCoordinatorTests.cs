using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Startup;
using MyAvaloniaManagement.ViewModels.Startup;

namespace MyAvaloniaManagement.Tests;

/// <summary>用可控任务验证终态竞争和资源交接；不靠固定 sleep 猜测后台进度。</summary>
public sealed class StartupCoordinatorTests
{
    [Fact]
    public async Task 重复启动共享任务_准备完成不等于窗口已经就绪()
    {
        using var runtime = Runtime();
        var release = new TaskCompletionSource<HostRuntime>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var coordinator = new StartupCoordinator((_, _) => { calls++; return release.Task; });
        var first = coordinator.StartAsync();
        Assert.Same(first, coordinator.StartAsync());
        Assert.Equal(StartupState.Loading, coordinator.State);
        Assert.False(coordinator.TryComplete());
        release.SetResult(runtime);
        await first;
        Assert.Equal(1, calls);
        Assert.Equal(StartupState.Prepared, coordinator.State);
        Assert.Same(runtime, coordinator.Runtime);
        Assert.True(coordinator.TryComplete());
        Assert.False(coordinator.RequestCancellation());
        Assert.Equal(StartupStage.Ready, coordinator.Progress.Snapshot.Current.Stage);
    }

    [Fact]
    public async Task 首帧之前取消不执行任何插件工厂()
    {
        using var coordinator = new StartupCoordinator((_, _) => throw new InvalidOperationException("不应执行"));
        Assert.True(coordinator.RequestCancellation());
        Assert.False(coordinator.RequestCancellation());
        await coordinator.StartAsync();
        Assert.Equal(StartupState.Cancelled, coordinator.State);
        Assert.Null(coordinator.Runtime);
        Assert.Null(coordinator.Failure);
    }

    [Fact]
    public async Task 取消与工厂成功竞争仍将Runtime交还进程入口()
    {
        using var runtime = Runtime();
        var release = new TaskCompletionSource<HostRuntime>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new StartupCoordinator((_, _) => release.Task);
        var task = coordinator.StartAsync();
        coordinator.RequestCancellation();
        release.SetResult(runtime);
        await task;
        Assert.Equal(StartupState.Cancelled, coordinator.State);
        Assert.Same(runtime, coordinator.Runtime);
        Assert.False(coordinator.TryComplete());
    }

    [Fact]
    public async Task 准备结束后接受取消禁止交接主窗口()
    {
        using var runtime = Runtime();
        using var coordinator = new StartupCoordinator((_, _) => Task.FromResult(runtime));
        await coordinator.StartAsync();
        Assert.True(coordinator.RequestCancellation());
        Assert.False(coordinator.TryComplete());
        Assert.Same(runtime, coordinator.Runtime);
    }

    [Fact]
    public async Task 工厂失败保留原异常且封闭进度()
    {
        var error = new InvalidOperationException("原始失败");
        using var coordinator = new StartupCoordinator((_, _) => Task.FromException<HostRuntime>(error));
        await coordinator.StartAsync();
        Assert.Same(error, coordinator.Failure);
        Assert.Equal(StartupState.Failed, coordinator.State);
        coordinator.Progress.Report(new(StartupStage.Ready));
        Assert.Equal(StartupStage.Preparing, coordinator.Progress.Snapshot.Current.Stage);
        Assert.False(coordinator.TryComplete());
    }

    [Fact]
    public async Task 工作线程隔离同步阻塞并在Windows保留STA入口()
    {
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var caller = Environment.CurrentManagedThreadId;
        var task = StartupWorker.RunAsync(() =>
        {
            entered.SetResult(Environment.CurrentManagedThreadId);
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return Task.FromResult(Thread.CurrentThread.GetApartmentState());
        });
        try
        {
            Assert.NotEqual(caller, await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(task.IsCompleted);
        }
        finally { release.Set(); }
        var apartment = await task;
        if (OperatingSystem.IsWindows()) Assert.Equal(ApartmentState.STA, apartment);
    }

    [Fact]
    public async Task 工作线程观察异步异常()
    {
        var error = new IOException("后台异常");
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() =>
            StartupWorker.RunAsync<int>(() => Task.FromException<int>(error))));
    }

    [Fact]
    public void 密集进度只保留最近三项但不丢失失败摘要()
    {
        var buffer = new StartupProgressBuffer();
        buffer.Report(new(StartupStage.Loading, "failed", 1, 11, StartupOutcome.Failed));
        for (var i = 0; i < 10; i++) buffer.Report(new(StartupStage.Loading, $"plugin-{i}", i + 2, 11, StartupOutcome.Succeeded));
        var snapshot = buffer.Snapshot;
        Assert.Equal(3, snapshot.Recent.Count);
        Assert.Equal(1, snapshot.FailureCount);
        Assert.Equal(11, snapshot.Current.Completed);
        Assert.Equal(11, snapshot.Current.Sequence);
        buffer.Close();
        buffer.Report(new(StartupStage.Ready));
        Assert.Equal(snapshot.Current, buffer.Snapshot.Current);
    }

    [Fact]
    public void 同插件当前状态替换开始记录_多个阶段失败只计一个插件()
    {
        var buffer = new StartupProgressBuffer();
        buffer.Report(new(StartupStage.Loading, "a"));
        buffer.Report(new(StartupStage.Loading, "a", 1, 1, StartupOutcome.Failed));
        Assert.Single(buffer.Snapshot.Recent);
        buffer.Report(new(StartupStage.Initializing, "a", 1, 1, StartupOutcome.Failed));
        Assert.Equal(1, buffer.Snapshot.FailureCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void 未知总数与空阶段没有假百分比(int? total)
    {
        var buffer = new StartupProgressBuffer();
        var model = new SplashViewModel();
        buffer.Report(new(StartupStage.Initializing, Total: total));
        model.Apply(buffer.Snapshot);
        Assert.True(model.IsIndeterminate);
        Assert.Equal(0, model.ProgressValue);
        Assert.DoesNotContain("0 / 0", model.CountText);
    }

    [Fact]
    public void 计数表达处理结果且阶段变化同步更新()
    {
        var buffer = new StartupProgressBuffer();
        var model = new SplashViewModel();
        buffer.Report(new(StartupStage.Loading, "a", 2, 4, StartupOutcome.Failed));
        model.Apply(buffer.Snapshot);
        Assert.Equal(50, model.ProgressValue);
        Assert.Contains("失败", model.RecentText);
        buffer.Report(new(StartupStage.Initializing, "b", 0, 2));
        model.Apply(buffer.Snapshot);
        Assert.Contains("初始化", model.StageText);
        Assert.Equal(0, model.ProgressValue);
        model.Stop();
        Assert.Contains("停止", model.Title);
    }

    [Fact]
    public void 观察器异常不改变业务结果() =>
        new ThrowingObserver().ReportSafely(new(StartupStage.Loading));

    [Fact]
    public void 迟到旧快照不覆盖当前阶段和插件()
    {
        var model = new SplashViewModel();
        var buffer = new StartupProgressBuffer();
        buffer.Report(new(StartupStage.Loading, "old"));
        var old = buffer.Snapshot;
        buffer.Report(new(StartupStage.Initializing, "current"));
        model.Apply(buffer.Snapshot);
        model.Apply(old);
        Assert.Equal("current", model.PluginText);
        Assert.Contains("初始化", model.StageText);
    }

    [Fact]
    public async Task 展示故障不能被迟到工厂成功覆盖()
    {
        using var runtime = Runtime();
        var release = new TaskCompletionSource<HostRuntime>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new StartupCoordinator((_, _) => release.Task);
        var task = coordinator.StartAsync();
        var failure = new InvalidOperationException("展示失败");
        coordinator.Fail(failure);
        release.SetResult(runtime);
        await task;
        Assert.Equal(StartupState.Failed, coordinator.State);
        Assert.Same(failure, coordinator.Failure);
        Assert.Same(runtime, coordinator.Runtime);
        Assert.False(coordinator.TryComplete());
    }

    private sealed class ThrowingObserver : IStartupProgressSink
    { public void Report(StartupProgress progress) => throw new InvalidOperationException("观察器故障"); }

    private static HostRuntime Runtime()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        return new HostRuntime(provider, new HostRuntimeShutdown(new EmptyDisposable(), provider, () => { },
            new HostShutdownParticipants(), new HostResourceRetention(), null));
    }
    private sealed class EmptyDisposable : IDisposable { public void Dispose() { } }
}
