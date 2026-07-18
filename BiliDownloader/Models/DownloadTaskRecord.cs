using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliDownloader.Models;

/// <summary>
/// 下载任务 SQLite 持久化记录
/// </summary>
public partial class DownloadTaskRecord : ObservableObject
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
    /// 用户选择的音频流 ID（0 表示使用最高码率）
    /// </summary>
    public int AudioQualityId { get; set; }

    /// <summary>
    /// 输出目录
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 分组子文件夹名称（UseGroupFolder 时为 SeriesTitle 的合法文件名，否则为空）
    /// </summary>
    public string SubFolder { get; set; } = string.Empty;

    /// <summary>
    /// 下载时的 Cookie（已废弃：运行时从 IBiliCredentialProvider 获取）
    /// </summary>
    [Obsolete("使用 IBiliCredentialProvider 获取运行时凭据，不再存储 Cookie")]
    public string Cookie { get; set; } = string.Empty;

    /// <summary>
    /// 最新进度 0~100
    /// </summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// 视频下载进度 0~100
    /// </summary>
    [ObservableProperty]
    private double _videoProgress;

    /// <summary>
    /// 音频下载进度 0~100
    /// </summary>
    [ObservableProperty]
    private double _audioProgress;

    /// <summary>
    /// 合成进度 0~100
    /// </summary>
    [ObservableProperty]
    private double _mergeProgress;

    /// <summary>
    /// 下载速度文本，如 "2.5 MB/s"
    /// </summary>
    [ObservableProperty]
    private string _speedText = "";

    /// <summary>
    /// 当前状态（存储为字符串，兼容 SQLite）
    /// </summary>
    [ObservableProperty]
    private string _status = DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Ready);

    /// <summary>
    /// 错误信息（仅 failed 时有值）
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

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

    /// <summary>
    /// 预期视频文件大小（字节），用于完整性验证
    /// </summary>
    public long ExpectedVideoBytes { get; set; }

    /// <summary>
    /// 预期音频文件大小（字节），用于完整性验证
    /// </summary>
    public long ExpectedAudioBytes { get; set; }

    /// <summary>
    /// 视频完整性验证是否通过
    /// </summary>
    public bool VideoIntegrityPassed { get; set; }

    /// <summary>
    /// 音频完整性验证是否通过
    /// </summary>
    public bool AudioIntegrityPassed { get; set; }

    /// <summary>
    /// 最终输出文件路径
    /// </summary>
    public string OutputFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 错误分类（network/cdn/ffmpeg/auth/unknown），仅 failed 时有值
    /// </summary>
    public string? ErrorType { get; set; }

    /// <summary>
    /// 是否可重试
    /// </summary>
    public bool IsRetryable { get; set; }
}
