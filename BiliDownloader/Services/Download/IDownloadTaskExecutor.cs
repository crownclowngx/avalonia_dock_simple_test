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
}

/// <summary>
/// 单次任务执行产生的结果。G0 只传递现有字段，不新增数据库结构或恢复语义。
/// </summary>
/// <param name="OutputFilePath">执行器返回的最终输出路径；旧链路无法确定时允许为空。</param>
/// <param name="ExtrasResultSummary">字幕、弹幕、封面等附加资源的执行摘要。</param>
public sealed record DownloadExecutionResult(
    string? OutputFilePath,
    string? ExtrasResultSummary);
