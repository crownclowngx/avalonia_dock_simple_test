namespace BiliDownloader.Models;

/// <summary>
/// 下载进度详情信息（分段进度 + 速度）
/// </summary>
public class DownloadProgressInfo
{
    /// <summary>
    /// 当前阶段："fetching" / "video" / "audio" / "merging" / "done"
    /// </summary>
    public string Stage { get; set; } = "";

    /// <summary>
    /// 总进度 0~100（加权平均）
    /// </summary>
    public double OverallProgress { get; set; }

    /// <summary>
    /// 视频下载进度 0~100
    /// </summary>
    public double VideoProgress { get; set; }

    /// <summary>
    /// 音频下载进度 0~100
    /// </summary>
    public double AudioProgress { get; set; }

    /// <summary>
    /// 合并进度 0~100
    /// </summary>
    public double MergeProgress { get; set; }

    /// <summary>
    /// 下载速度文本，如 "2.5 MB/s"，非下载阶段为空
    /// </summary>
    public string SpeedText { get; set; } = "";

    /// <summary>Numeric transfer speed used by UI and persistence.</summary>
    public long BytesPerSecond { get; set; }
}
