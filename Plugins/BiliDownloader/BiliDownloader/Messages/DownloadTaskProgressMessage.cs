namespace BiliDownloader.Messages;

/// <summary>
/// Tool -> Document：下载任务进度回传消息
/// </summary>
public class DownloadTaskProgressMessage
{
    /// <summary>
    /// 目标 Document 实例 ID（用于定向过滤）
    /// </summary>
    public string TargetDocumentId { get; }

    /// <summary>
    /// 任务唯一标识（对应 DownloadItemInfo.ItemId / DownloadTaskRecord.TaskId）
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// 视频标题
    /// </summary>
    public string ItemTitle { get; }

    /// <summary>
    /// 下载进度 0~100
    /// </summary>
    public double Progress { get; }

    /// <summary>
    /// 当前状态：pending/downloading_video/downloading_audio/merging/done/failed
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// 错误信息（仅 failed 时有值）
    /// </summary>
    public string? ErrorMessage { get; }

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

    public DownloadTaskProgressMessage(
        string targetDocumentId,
        string taskId,
        string itemTitle,
        double progress,
        string status,
        string? errorMessage = null,
        double videoProgress = 0,
        double audioProgress = 0,
        double mergeProgress = 0,
        string speedText = "")
    {
        TargetDocumentId = targetDocumentId;
        TaskId = taskId;
        ItemTitle = itemTitle;
        Progress = progress;
        Status = status;
        ErrorMessage = errorMessage;
        VideoProgress = videoProgress;
        AudioProgress = audioProgress;
        MergeProgress = mergeProgress;
        SpeedText = speedText;
    }
}
