using BiliDownloader.Services.Download;

namespace BiliDownloader.Tests;

/// <summary>
/// TaskRuntimeContext 纯单元测试：验证单次任务执行的取消传播和停止原因语义。
/// </summary>
public sealed class TaskRuntimeContextTests
{
    [Fact]
    public void 创建后令牌未取消且停止原因为None()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);

        Assert.Equal("task1", context.TaskId);
        Assert.False(context.Token.IsCancellationRequested);
        Assert.Equal(TaskStopReason.None, context.StopReason);
        context.ThrowIfCancellationRequested();
    }

    [Theory]
    [InlineData((int)TaskStopReason.Pause)]
    [InlineData((int)TaskStopReason.Cancel)]
    [InlineData((int)TaskStopReason.Restart)]
    [InlineData((int)TaskStopReason.Delete)]
    public void RequestStop记录原因并取消令牌(int reasonValue)
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);
        var reason = (TaskStopReason)reasonValue;

        context.RequestStop(reason);

        Assert.Equal(reason, context.StopReason);
        Assert.True(context.Token.IsCancellationRequested);
        Assert.Throws<OperationCanceledException>(() => context.ThrowIfCancellationRequested());
    }

    [Fact]
    public void RequestStop拒绝None原因()
    {
        using var context = TaskRuntimeContext.CreateLinked("task1", CancellationToken.None);

        Assert.Throws<ArgumentOutOfRangeException>(() => context.RequestStop(TaskStopReason.None));
        Assert.False(context.Token.IsCancellationRequested);
    }

    [Fact]
    public void 第一次停止原因不会被后续命令改写()
    {
        using var context = TaskRuntimeContext.CreateLinked("task1", CancellationToken.None);

        context.RequestStop(TaskStopReason.Pause);
        context.RequestStop(TaskStopReason.Restart);
        context.RequestStop(TaskStopReason.Cancel);

        Assert.Equal(TaskStopReason.Pause, context.StopReason);
        Assert.True(context.Token.IsCancellationRequested);
    }

    [Fact]
    public void 父令牌取消会传播但不伪造单任务停止原因()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);

        parentCts.Cancel();

        Assert.True(context.IsParentCancelled);
        Assert.True(context.Token.IsCancellationRequested);
        Assert.Equal(TaskStopReason.None, context.StopReason);
    }

    [Fact]
    public void 单任务停止不会取消父令牌()
    {
        using var parentCts = new CancellationTokenSource();
        using var context = TaskRuntimeContext.CreateLinked("task1", parentCts.Token);

        context.RequestStop(TaskStopReason.Cancel);

        Assert.True(context.Token.IsCancellationRequested);
        Assert.False(parentCts.Token.IsCancellationRequested);
    }

    [Fact]
    public void 恢复必须使用新的运行上下文()
    {
        using var pausedContext = TaskRuntimeContext.CreateLinked("task1", CancellationToken.None);
        pausedContext.RequestStop(TaskStopReason.Pause);

        using var resumedContext = TaskRuntimeContext.CreateLinked("task1", CancellationToken.None);

        Assert.True(pausedContext.Token.IsCancellationRequested);
        Assert.Equal(TaskStopReason.Pause, pausedContext.StopReason);
        Assert.False(resumedContext.Token.IsCancellationRequested);
        Assert.Equal(TaskStopReason.None, resumedContext.StopReason);
    }

    [Fact]
    public void Dispose可以重复调用()
    {
        var context = TaskRuntimeContext.CreateLinked("task1", CancellationToken.None);

        context.Dispose();

        var exception = Record.Exception(context.Dispose);
        Assert.Null(exception);
    }

    [Fact]
    public void 清理后到达的停止命令按幂等操作处理()
    {
        var context = TaskRuntimeContext.CreateLinked("task1", CancellationToken.None);
        context.Dispose();

        var exception = Record.Exception(() => context.RequestStop(TaskStopReason.Pause));

        Assert.Null(exception);
        Assert.Equal(TaskStopReason.Pause, context.StopReason);
    }
}
