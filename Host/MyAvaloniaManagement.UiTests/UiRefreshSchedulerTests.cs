using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Presentation.Commands;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>使用实际 Dispatcher 验证两种调度时机和迟到回调，不以固定等待模拟 UI 队列。</summary>
public sealed class UiRefreshSchedulerTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UI线程同步模式与排队模式保持不同的合并行为(bool alwaysPost)
    {
        using var consumer = new Consumer(alwaysPost);
        consumer.Scheduler.RequestRefresh();
        consumer.Scheduler.RequestRefresh();
        Assert.Equal(alwaysPost ? 0 : 2, consumer.Notifications);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(alwaysPost ? 1 : 2, consumer.Notifications);
        consumer.Scheduler.RequestRefresh();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(alwaysPost ? 2 : 3, consumer.Notifications);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void 后台连续请求合并且回调在UI线程并在锁外执行(bool alwaysPost)
    {
        using var consumer = new Consumer(alwaysPost);
        // 使用独立线程保证调用不被任务调度器内联到当前 UI 线程；工作线程只投递、不等待 UI。
        RunOnWorker(() =>
        {
            for (var index = 0; index < 20; index++) consumer.Scheduler.RequestRefresh();
        });
        Assert.Equal(0, consumer.Notifications);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, consumer.Notifications);
        Assert.True(consumer.LastWasUiThread);
        Assert.False(consumer.CallbackEnteredWithLock);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void 排队后释放和释放后的请求均不再通知且重复释放安全(bool alwaysPost)
    {
        using var consumer = new Consumer(alwaysPost);
        RunOnWorker(consumer.Scheduler.RequestRefresh);
        consumer.Dispose();
        consumer.Dispose();
        consumer.Scheduler.RequestRefresh();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, consumer.Notifications);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void 发布期间的新失效保留各自重入顺序(bool alwaysPost)
    {
        using var consumer = new Consumer(alwaysPost);
        var trace = new List<string>();
        consumer.OnNotification = number =>
        {
            trace.Add("begin" + number);
            if (number == 1) consumer.Scheduler.RequestRefresh();
            trace.Add("end" + number);
        };
        consumer.Scheduler.RequestRefresh();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(alwaysPost
            ? new[] { "begin1", "end1", "begin2", "end2" }
            : new[] { "begin1", "begin2", "end2", "end1" }, trace);
    }

    [AvaloniaFact]
    public void 回调异常原样交给消费者且不阻断下一次请求()
    {
        using var consumer = new Consumer(false);
        var failure = new InvalidOperationException("原观察者失败策略由消费者负责");
        consumer.OnNotification = _ => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(consumer.Scheduler.RequestRefresh));
        consumer.OnNotification = null;
        consumer.Scheduler.RequestRefresh();
        Assert.Equal(2, consumer.Notifications);
    }

    [AvaloniaFact]
    public void 回调中释放抑制后续请求并且其他消费者不受影响()
    {
        using var first = new Consumer(false);
        using var second = new Consumer(true);
        first.OnNotification = _ =>
        {
            first.Dispose();
            first.Scheduler.RequestRefresh();
            second.Scheduler.RequestRefresh();
        };
        first.Scheduler.RequestRefresh();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, first.Notifications);
        Assert.Equal(1, second.Notifications);
    }

    /// <summary>固定线程边界并把工作线程断言异常交回测试线程，避免误把内联任务当作后台调度。</summary>
    private static void RunOnWorker(Action action)
    {
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                Assert.False(Dispatcher.UIThread.CheckAccess());
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(10)), "工作线程只投递 UI 请求，不应等待 UI 队列。");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>最小事件消费者只记录可观察顺序，调度与合并全部调用生产实现。</summary>
    private sealed class Consumer : IDisposable
    {
        private readonly object _gate = new();
        internal UiRefreshScheduler Scheduler { get; }
        internal int Notifications { get; private set; }
        internal bool LastWasUiThread { get; private set; }
        internal bool CallbackEnteredWithLock { get; private set; }
        internal Action<int>? OnNotification { get; set; }

        internal Consumer(bool alwaysPost) => Scheduler = new(_gate, Dispatcher.UIThread,
            alwaysPost ? UiRefreshMode.AlwaysPost : UiRefreshMode.ImmediateOnUiThread, Publish);

        private void Publish()
        {
            CallbackEnteredWithLock = Monitor.IsEntered(_gate);
            Action<int>? observer;
            lock (_gate)
            {
                if (!Scheduler.TryBeginRefresh()) return;
                observer = OnNotification;
            }
            LastWasUiThread = Dispatcher.UIThread.CheckAccess();
            Notifications++;
            observer?.Invoke(Notifications);
        }

        public void Dispose() => Scheduler.Dispose();
    }
}
