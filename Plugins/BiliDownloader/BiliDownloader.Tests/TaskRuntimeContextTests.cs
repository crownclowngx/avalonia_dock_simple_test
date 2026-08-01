using BiliDownloader.Services.Download;

namespace BiliDownloader.Tests;

/// <summary>
/// TaskRuntimeContext 纯单元测试：验证 per-task 控制原语的语义正确性。
/// 不依赖 Coordinator，直接测试暂停/恢复/取消/链接传播。
/// </summary>
public sealed class TaskRuntimeContextTests
{
    [Fact]
    public void 创建后Token未取消且暂停门控已打开()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);

        Assert.False(context.Token.IsCancellationRequested);
        // WaitIfPaused 不应阻塞（门控初始为 open）
        context.WaitIfPaused(); // 如果阻塞则测试超时失败
    }

    [Fact]
    public void RequestPause后WaitIfPaused抛出OperationCanceledException()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);
        context.RequestPause();

        Assert.Throws<OperationCanceledException>(() => context.WaitIfPaused());
    }

    [Fact]
    public void Resume后WaitIfPaused不抛异常()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);
        context.RequestPause();
        context.Resume();

        // Resume 后 IsPaused=false，WaitIfPaused 不抛异常
        var exception = Xunit.Record.Exception(() => context.WaitIfPaused());
        Assert.Null(exception);
    }

    [Fact]
    public void RequestCancellation取消Token()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);

        context.RequestCancellation();

        Assert.True(context.Token.IsCancellationRequested);
    }

    [Fact]
    public void 暂停状态下RequestCancellation不死锁()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);
        context.RequestPause();

        // 取消应正常工作，不抛额外异常
        var exception = Xunit.Record.Exception(() => context.RequestCancellation());
        Assert.Null(exception);
        Assert.True(context.Token.IsCancellationRequested);
    }

    [Fact]
    public void 全局父Token取消传播到子Token()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);

        parentCts.Cancel();

        Assert.True(context.Token.IsCancellationRequested);
    }

    [Fact]
    public void 单任务取消不影响父Token()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);

        context.RequestCancellation();

        Assert.True(context.Token.IsCancellationRequested);
        Assert.False(parentCts.Token.IsCancellationRequested);
    }

    [Fact]
    public void Dispose后资源释放不抛异常()
    {
        using var parentCts = new CancellationTokenSource();
        var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);

        context.Dispose();
        // 二次 Dispose 不应抛出（CancellationTokenSource.Dispose 是幂等的）
        var exception = Record.Exception(() => context.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void IsPaused属性正确反映状态()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);

        Assert.False(context.IsPaused);
        context.RequestPause();
        Assert.True(context.IsPaused);
        context.Resume();
        Assert.False(context.IsPaused);
    }

    [Fact]
    public void 多次Pause幂等()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);

        context.RequestPause();
        context.RequestPause(); // 第二次不应抛异常

        Assert.True(context.IsPaused);
        Assert.True(context.Token.IsCancellationRequested);
        Assert.Throws<OperationCanceledException>(() => context.WaitIfPaused());
    }

    [Fact]
    public void 多次Resume幂等()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);

        // ManualResetEventSlim.Set() 多次调用不抛异常
        var exception = Record.Exception(() =>
        {
            context.Resume();
            context.Resume();
        });
        Assert.Null(exception);
    }
}
