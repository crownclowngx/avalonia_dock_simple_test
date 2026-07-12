namespace BiliDownloader.Messages;

/// <summary>
/// Tool -> Document：任务状态变更通知（调度器自主操作时广播）
/// 用于状态跳变事件（如 pending->downloading、failed->pending 重试），区别于持续的进度更新
/// </summary>
public class DownloadTaskStatusChangedMessage
{
    /// <summary>
    /// 目标 Document 实例 ID
    /// </summary>
    public string TargetDocumentId { get; }

    /// <summary>
    /// 任务 ID
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// 新状态：pending/downloading_video/downloading_audio/merging/done/failed
    /// </summary>
    public string NewStatus { get; }

    /// <summary>
    /// 当前进度
    /// </summary>
    public double Progress { get; }

    public DownloadTaskStatusChangedMessage(
        string targetDocumentId,
        string taskId,
        string newStatus,
        double progress)
    {
        TargetDocumentId = targetDocumentId;
        TaskId = taskId;
        NewStatus = newStatus;
        Progress = progress;
    }
}
