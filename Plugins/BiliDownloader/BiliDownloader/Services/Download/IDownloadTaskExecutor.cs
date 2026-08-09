using BiliDownloader.Models;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 单个下载任务的可替换执行边界。
/// <para>
/// Coordinator 只负责任务编排、状态持久化和取消控制，不应了解 Bilibili API、
/// HTTP 下载、ffmpeg 或附加资源处理的具体实现。测试可以注入完全内存化的假执行器，
/// 从而验证调度行为时不访问网络、不创建真实媒体文件，也不启动外部进程。
/// </para>
/// </summary>
public interface IDownloadTaskExecutor
{
    /// <summary>
    /// 执行一个任务的完整下载链路，并通过回调上报阶段进度和断点字节数。
    /// 实现必须响应取消令牌，并在取消后尽快释放网络、文件和外部进程资源。
    /// </summary>
    Task<DownloadExecutionResult> ExecuteAsync(
        DownloadTaskRecord task,
        Action<DownloadProgressInfo> onProgress,
        Action<long, long> onBytesChanged,
        CancellationToken cancellationToken);

    /// <summary>
    /// 使用聚合回调执行任务。默认实现兼容 G0-G6 执行器；G7 生产执行器会覆盖此方法，
    /// 在启动 ffmpeg 之前等待媒体检查点持久化完成，确保突然退出后仍可安全判断是否仅重试合并。
    /// </summary>
    Task<DownloadExecutionResult> ExecuteAsync(
        DownloadTaskRecord task,
        DownloadExecutionCallbacks callbacks,
        CancellationToken cancellationToken)
        => ExecuteAsync(task, callbacks.OnProgress, callbacks.OnBytesChanged, cancellationToken);
}

/// <summary>
/// 下载执行回调集合。把同一执行上下文的通知收敛为一个参数，避免以后每增加一个阶段检查点
/// 就破坏所有执行器签名；其中媒体检查点是可等待回调，执行器必须等它落库后才能启动合并。
/// </summary>
public sealed record DownloadExecutionCallbacks(
    Action<DownloadProgressInfo> OnProgress,
    Action<long, long> OnBytesChanged,
    Func<MediaReadyCheckpoint, Task> OnMediaReadyAsync,
    Func<MediaOutputPlan, Task>? OnMediaSelectionResolvedAsync = null)
{
    public Func<MediaOutputPlan, Task> EffectiveMediaSelectionResolvedAsync
        => OnMediaSelectionResolvedAsync ?? (_ => Task.CompletedTask);
}

/// <summary>视频和音频都通过完整性校验后的持久化事实。</summary>
public sealed record MediaReadyCheckpoint(
    long ExpectedVideoBytes,
    long ExpectedAudioBytes,
    bool VideoIntegrityPassed,
    bool AudioIntegrityPassed);

/// <summary>
/// 仅重试合并阶段的窄执行边界。Coordinator 在调用前验证任务状态、临时文件和路径保留，
/// 实现不得重新请求 DASH 或重新下载主媒体。
/// </summary>
public interface IMediaMergeRetryExecutor
{
    Task<DownloadExecutionResult> ExecuteMergeOnlyAsync(
        DownloadTaskRecord task,
        Action<DownloadProgressInfo> onProgress,
        CancellationToken cancellationToken);
}

/// <summary>
/// 已完成任务的附加资源重试边界。实现不得请求 DASH、写入断点字节或重新下载主媒体，
/// 只返回合并后的版本化结果摘要。
/// </summary>
public interface IExtrasRetryExecutor
{
    Task<string?> ExecuteFailedExtrasAsync(
        DownloadTaskRecord task,
        CancellationToken cancellationToken);
}

/// <summary>
/// 单次任务执行产生的结果。G0 只传递现有字段，不新增数据库结构或恢复语义。
/// </summary>
/// <param name="OutputFilePath">执行器返回的最终输出路径；旧链路无法确定时允许为空。</param>
/// <param name="ExtrasResultSummary">字幕、弹幕、封面等附加资源的执行摘要。</param>
public sealed record DownloadExecutionResult(
    string? OutputFilePath,
    string? ExtrasResultSummary,
    DownloadTransferResult? VideoTransfer = null,
    DownloadTransferResult? AudioTransfer = null,
    MediaFeatureFlags? ActualMediaFeatures = null);
