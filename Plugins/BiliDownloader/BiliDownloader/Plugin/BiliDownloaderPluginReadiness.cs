namespace BiliDownloader.Plugin;

/// <summary>描述 BiliDownloader 插件内部资源是否已经可安全使用。</summary>
public enum BiliDownloaderReadinessStatus
{
    /// <summary>Lifecycle 尚未开始初始化。</summary>
    NotStarted,
    /// <summary>本地状态、数据库、FFmpeg 与 Coordinator 正在初始化。</summary>
    Initializing,
    /// <summary>全部插件级资源已初始化，Document 与 Tool 可以工作。</summary>
    Ready,
    /// <summary>Lifecycle 正在协作停止后台资源。</summary>
    Stopping,
    /// <summary>后台资源已经全部停止。</summary>
    Stopped,
    /// <summary>初始化或停止失败；具体异常仍由 Lifecycle 原样交给 Host。</summary>
    Faulted,
}

/// <summary>
/// readiness 的不可变投影。消息只表达可展示的阶段，不包含路径、Cookie、URL 或异常正文。
/// </summary>
public sealed record BiliDownloaderReadinessSnapshot(
    BiliDownloaderReadinessStatus Status,
    bool IsReady,
    string Message);

/// <summary>
/// Tool 使用的只读可用性端口。该端口属于插件内部，故意不暴露 Host 生命周期实现。
/// </summary>
public interface IBiliDownloaderPluginReadiness
{
    /// <summary>获取一次原子读取的不可变状态快照。</summary>
    BiliDownloaderReadinessSnapshot Snapshot { get; }

    /// <summary>在快照完成替换后发出通知；订阅者应重新读取 <see cref="Snapshot"/>。</summary>
    event EventHandler? Changed;
}

/// <summary>
/// 线程安全的插件内 readiness 状态。只有 Lifecycle 持有具体类型并能改变状态，
/// 其他消费者通过 <see cref="IBiliDownloaderPluginReadiness"/> 读取快照。
/// </summary>
public sealed class BiliDownloaderPluginReadiness : IBiliDownloaderPluginReadiness
{
    private readonly object _gate = new();
    private BiliDownloaderReadinessSnapshot _snapshot = CreateSnapshot(
        BiliDownloaderReadinessStatus.NotStarted);

    /// <inheritdoc />
    public BiliDownloaderReadinessSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    internal void MarkInitializing() => Set(BiliDownloaderReadinessStatus.Initializing);

    internal void MarkReady() => Set(BiliDownloaderReadinessStatus.Ready);

    internal void MarkStopping() => Set(BiliDownloaderReadinessStatus.Stopping);

    internal void MarkStopped() => Set(BiliDownloaderReadinessStatus.Stopped);

    internal void MarkFaulted() => Set(BiliDownloaderReadinessStatus.Faulted);

    private void Set(BiliDownloaderReadinessStatus status)
    {
        lock (_gate)
        {
            _snapshot = CreateSnapshot(status);
        }

        // 回调放在锁外，避免 UI 消费者读取 Snapshot 时与 Lifecycle 形成锁重入。
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static BiliDownloaderReadinessSnapshot CreateSnapshot(
        BiliDownloaderReadinessStatus status) => status switch
        {
            BiliDownloaderReadinessStatus.NotStarted => new(status, false, "插件尚未初始化。"),
            BiliDownloaderReadinessStatus.Initializing => new(status, false, "插件正在初始化，请稍候。"),
            BiliDownloaderReadinessStatus.Ready => new(status, true, "插件已就绪。"),
            BiliDownloaderReadinessStatus.Stopping => new(status, false, "插件正在停止。"),
            BiliDownloaderReadinessStatus.Stopped => new(status, false, "插件已停止。"),
            BiliDownloaderReadinessStatus.Faulted => new(status, false, "插件当前不可用，请查看 Host 诊断信息。"),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "未知的 readiness 状态。"),
        };
}
