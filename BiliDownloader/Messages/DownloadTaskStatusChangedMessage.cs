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

    /// <summary>
    /// 视频下载进度 0~100
    /// </summary>
    public double VideoProgress { get; }

    /// <summary>
    /// 音频下载进度 0~100
    /// </summary>
    public double AudioProgress { get; }

    /// <summary>
    /// 合成进度 0~100
    /// </summary>
    public double MergeProgress { get; }

    /// <summary>
    /// 下载速度文本
    /// </summary>
    public string SpeedText { get; }

    public DownloadTaskStatusChangedMessage(
        string targetDocumentId,
        string taskId,
        string newStatus,
        double progress,
        double videoProgress = 0,
        double audioProgress = 0,
        double mergeProgress = 0,
        string speedText = "")
    {
        TargetDocumentId = targetDocumentId;
        TaskId = taskId;
        NewStatus = newStatus;
        Progress = progress;
        VideoProgress = videoProgress;
        AudioProgress = audioProgress;
        MergeProgress = mergeProgress;
        SpeedText = speedText;
    }
}
