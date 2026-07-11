namespace BiliDownloader.Models;

/// <summary>
/// 下载任务 SQLite 持久化记录
/// </summary>
public class DownloadTaskRecord
{
    /// <summary>
    /// 任务唯一标识（对应 BiliVideoItem.ItemId / DownloadItemInfo.ItemId）
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 关联的 Document 实例 ID（用于定向回传进度和按 Document 查询）
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// 系列标题
    /// </summary>
    public string SeriesTitle { get; set; } = string.Empty;

    /// <summary>
    /// 单个视频标题
    /// </summary>
    public string ItemTitle { get; set; } = string.Empty;

    public long Aid { get; set; }
    public string Bvid { get; set; } = string.Empty;
    public long Cid { get; set; }

    /// <summary>
    /// 用户选择的清晰度
    /// </summary>
    public int QualityId { get; set; }

    /// <summary>
    /// 输出目录
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 下载时的 Cookie
    /// </summary>
    public string Cookie { get; set; } = string.Empty;

    /// <summary>
    /// 最新进度 0~100
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// 当前状态：pending/downloading_video/downloading_audio/merging/done/failed
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// 错误信息（仅 failed 时有值）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 临时文件目录路径（用于断点续传和清理）
    /// </summary>
    public string TempDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 视频流已下载字节数（用于断点续传）
    /// </summary>
    public long VideoBytesDownloaded { get; set; }

    /// <summary>
    /// 音频流已下载字节数（用于断点续传）
    /// </summary>
    public long AudioBytesDownloaded { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
