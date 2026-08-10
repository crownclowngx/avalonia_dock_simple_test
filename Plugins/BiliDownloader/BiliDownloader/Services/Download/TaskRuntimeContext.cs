namespace BiliDownloader.Services.Download;

/// <summary>
/// 单次任务执行被终止的显式原因。父级停止和宿主关闭仍通过父令牌及协调器生命周期判断；
/// 此枚举只描述用户针对单个任务发出的命令。
/// </summary>
internal enum TaskStopReason
{
    None,
    Pause,
    Cancel,
    Restart,
    Delete,
}

/// <summary>
/// 单个下载任务的运行时控制上下文。
/// 封装独立的取消令牌和不可变停止原因，使 Coordinator 能精确控制单个任务，
/// 而不影响其他并发执行的任务。
/// <para>
/// 暂停采用“取消当前尝试并保留断点”的语义。取消令牌不可恢复，因此恢复任务时必须由
/// Coordinator 创建全新的运行上下文，不能尝试复用本实例。
/// </para>
/// </summary>
internal sealed class TaskRuntimeContext : IDisposable
{
    private readonly CancellationTokenSource _linkedCts;
    private readonly CancellationToken _parentToken;
    private int _stopReason;
    private int _disposeState;

    public string TaskId { get; }

    /// <summary>per-task 取消令牌，链接了全局父令牌</summary>
    public CancellationToken Token => _linkedCts.Token;

    /// <summary>当前单任务停止原因；第一次命令获胜，后续命令不能改写取消语义。</summary>
    public TaskStopReason StopReason => (TaskStopReason)Volatile.Read(ref _stopReason);

    /// <summary>全局父令牌是否已取消（用于区分全局停止与单任务取消）</summary>
    public bool IsParentCancelled => _parentToken.IsCancellationRequested;

    private TaskRuntimeContext(string taskId, CancellationTokenSource linkedCts, CancellationToken parentToken)
    {
        TaskId = taskId;
        _linkedCts = linkedCts;
        _parentToken = parentToken;
    }

    /// <summary>
    /// 创建链接全局取消的 per-task 上下文。
    /// 全局停止会传播到所有任务，单任务取消只影响自己。
    /// </summary>
    public static TaskRuntimeContext CreateLinked(string taskId, CancellationToken parentToken)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        return new TaskRuntimeContext(taskId, linkedCts, parentToken);
    }

    /// <summary>
    /// 以明确原因终止本次执行。停止原因只允许从 None 写入一次，防止恢复或后续命令
    /// 在取消异常尚未完成分类时改变旧执行的最终状态。
    /// </summary>
    public void RequestStop(TaskStopReason reason)
    {
        if (reason == TaskStopReason.None)
            throw new ArgumentOutOfRangeException(nameof(reason));

        Interlocked.CompareExchange(
            ref _stopReason,
            (int)reason,
            (int)TaskStopReason.None);
        try
        {
            _linkedCts.Cancel();
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposeState) != 0)
        {
            // 控制命令可能刚取得 ActiveTaskRun，执行却恰好完成并清理了上下文。
            // 此时停止命令已经没有可取消的执行，按幂等的“已停止”处理。
        }
    }

    /// <summary>在进入执行器前确认本次执行尚未被命令或父级生命周期取消。</summary>
    public void ThrowIfCancellationRequested() => Token.ThrowIfCancellationRequested();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
            _linkedCts.Dispose();
    }
}
