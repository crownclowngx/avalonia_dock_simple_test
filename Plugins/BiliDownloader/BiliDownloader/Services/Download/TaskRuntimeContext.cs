namespace BiliDownloader.Services.Download;

/// <summary>
/// 单个下载任务的运行时控制上下文。
/// 封装独立的取消令牌和暂停门控，使 Coordinator 能精确控制单个任务，
/// 而不影响其他并发执行的任务。
/// <para>
/// 设计思考：不使用通用仓储或复杂工厂。每个任务的控制状态只有"运行/暂停/取消"三种，
/// 用一个轻量容器集中管理 CTS 和暂停信号即可，避免在 Coordinator 中维护多个平行字典。
/// </para>
/// </summary>
internal sealed class TaskRuntimeContext : IDisposable
{
    private readonly CancellationTokenSource _linkedCts;
    private readonly ManualResetEventSlim _pauseGate = new(initialState: true);
    private readonly CancellationToken _parentToken;

    public string TaskId { get; }

    /// <summary>per-task 取消令牌，链接了全局父令牌</summary>
    public CancellationToken Token => _linkedCts.Token;

    /// <summary>当前是否处于暂停请求状态</summary>
    public bool IsPaused { get; private set; }

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

    /// <summary>请求暂停：标记暂停状态并取消 CTS，使执行器在下一个取消点退出。
    /// 与 RequestCancellation 的区别：IsPaused 保持 true，使 Coordinator 能区分暂停与永久取消。</summary>
    public void RequestPause()
    {
        IsPaused = true;
        _pauseGate.Reset();
        if (!_linkedCts.IsCancellationRequested)
        {
            _linkedCts.Cancel();
        }
    }

    /// <summary>恢复执行：释放暂停门控，从断点继续</summary>
    public void Resume()
    {
        IsPaused = false;
        _pauseGate.Set();
    }

    /// <summary>请求取消（不可逆）：取消 CTS，同时释放暂停门控防止死锁</summary>
    public void RequestCancellation()
    {
        _pauseGate.Set(); // 先释放暂停门控，避免取消时死锁
        if (!_linkedCts.IsCancellationRequested)
        {
            _linkedCts.Cancel();
        }
    }

    /// <summary>
    /// 在阶段边界调用。如果已暂停则抛出 OperationCanceledException 使任务退出并被重新调度。
    /// </summary>
    public void WaitIfPaused()
    {
        if (IsPaused)
        {
            throw new OperationCanceledException(_linkedCts.Token);
        }
    }

    public void Dispose()
    {
        _pauseGate.Set(); // 确保没有线程阻塞在 Wait 上
        _linkedCts.Dispose();
        _pauseGate.Dispose();
    }
}
