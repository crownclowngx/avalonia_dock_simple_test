namespace BiliDownloader.Models;

/// <summary>
/// 下载任务状态枚举
/// </summary>
public enum DownloadTaskStatus
{
    /// <summary>允许调度器选取执行</summary>
    Ready,

    /// <summary>正在获取 DASH 流元数据</summary>
    FetchingMetadata,

    /// <summary>正在下载视频流</summary>
    DownloadingVideo,

    /// <summary>视频流下载完成，准备下载音频</summary>
    VideoReady,

    /// <summary>正在下载音频流</summary>
    DownloadingAudio,

    /// <summary>音频流下载完成，准备合并</summary>
    AudioReady,

    /// <summary>正在 ffmpeg 合并</summary>
    Merging,

    /// <summary>下载完成</summary>
    Completed,

    /// <summary>用户主动暂停</summary>
    Paused,

    /// <summary>异常退出前的运行中状态（需手动恢复）</summary>
    Interrupted,

    /// <summary>下载失败</summary>
    Failed,

    /// <summary>用户取消</summary>
    Canceled,

    /// <summary>无有效登录态，等待重新登录</summary>
    WaitingForLogin,
}

/// <summary>
/// 状态枚举与 SQLite 存储字符串之间的双向映射
/// </summary>
public static class DownloadTaskStatusMapper
{
    /// <summary>
    /// 将枚举转换为 SQLite 存储字符串（向后兼容现有数据）
    /// </summary>
    public static string ToStorageString(DownloadTaskStatus status) => status switch
    {
        DownloadTaskStatus.Ready => "pending",
        DownloadTaskStatus.FetchingMetadata => "fetching_metadata",
        DownloadTaskStatus.DownloadingVideo => "downloading_video",
        DownloadTaskStatus.VideoReady => "video_ready",
        DownloadTaskStatus.DownloadingAudio => "downloading_audio",
        DownloadTaskStatus.AudioReady => "audio_ready",
        DownloadTaskStatus.Merging => "merging",
        DownloadTaskStatus.Completed => "done",
        DownloadTaskStatus.Paused => "paused",
        DownloadTaskStatus.Interrupted => "interrupted",
        DownloadTaskStatus.Failed => "failed",
        DownloadTaskStatus.Canceled => "canceled",
        DownloadTaskStatus.WaitingForLogin => "waiting_for_login",
        _ => status.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// 将 SQLite 存储字符串解析为枚举（兼容旧数据）
    /// </summary>
    public static DownloadTaskStatus FromStorageString(string status) => status switch
    {
        "pending" => DownloadTaskStatus.Ready,
        "fetching_metadata" => DownloadTaskStatus.FetchingMetadata,
        "downloading_video" => DownloadTaskStatus.DownloadingVideo,
        "video_ready" => DownloadTaskStatus.VideoReady,
        "downloading_audio" => DownloadTaskStatus.DownloadingAudio,
        "audio_ready" => DownloadTaskStatus.AudioReady,
        "merging" => DownloadTaskStatus.Merging,
        "done" => DownloadTaskStatus.Completed,
        "paused" => DownloadTaskStatus.Paused,
        "interrupted" => DownloadTaskStatus.Interrupted,
        "failed" => DownloadTaskStatus.Failed,
        "canceled" => DownloadTaskStatus.Canceled,
        "waiting_for_login" => DownloadTaskStatus.WaitingForLogin,
        _ => DownloadTaskStatus.Ready, // 未知状态回退为 Ready
    };

    /// <summary>
    /// 获取中文显示文本
    /// </summary>
    public static string ToDisplayText(DownloadTaskStatus status) => status switch
    {
        DownloadTaskStatus.Ready => "排队中",
        DownloadTaskStatus.FetchingMetadata => "获取信息",
        DownloadTaskStatus.DownloadingVideo => "下载视频",
        DownloadTaskStatus.VideoReady => "视频就绪",
        DownloadTaskStatus.DownloadingAudio => "下载音频",
        DownloadTaskStatus.AudioReady => "音频就绪",
        DownloadTaskStatus.Merging => "合并中",
        DownloadTaskStatus.Completed => "完成",
        DownloadTaskStatus.Paused => "已暂停",
        DownloadTaskStatus.Interrupted => "已中断",
        DownloadTaskStatus.Failed => "失败",
        DownloadTaskStatus.Canceled => "已取消",
        DownloadTaskStatus.WaitingForLogin => "等待登录",
        _ => status.ToString(),
    };

    /// <summary>
    /// 判断是否为运行中状态（可被调度器选取或中断）
    /// </summary>
    public static bool IsRunning(DownloadTaskStatus status) => status is
        DownloadTaskStatus.FetchingMetadata or
        DownloadTaskStatus.DownloadingVideo or
        DownloadTaskStatus.VideoReady or
        DownloadTaskStatus.DownloadingAudio or
        DownloadTaskStatus.AudioReady or
        DownloadTaskStatus.Merging;
}
