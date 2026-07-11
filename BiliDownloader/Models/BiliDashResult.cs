namespace BiliDownloader.Models;

/// <summary>
/// DASH 播放流解析结果
/// </summary>
public class BiliDashResult
{
    /// <summary>
    /// 可用清晰度列表
    /// </summary>
    public List<BiliQualityOption> AcceptQualities { get; set; } = new();

    /// <summary>
    /// 视频流列表
    /// </summary>
    public List<BiliDashStream> VideoStreams { get; set; } = new();

    /// <summary>
    /// 音频流列表
    /// </summary>
    public List<BiliDashStream> AudioStreams { get; set; } = new();
}

/// <summary>
/// DASH 单条流信息
/// </summary>
public class BiliDashStream
{
    /// <summary>
    /// 画质/音质 ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 主下载 URL
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// 备用 URL 列表
    /// </summary>
    public List<string> BackupUrls { get; set; } = new();

    /// <summary>
    /// 编码 ID（7=AVC/H.264, 12=HEVC/H.265, 13=AV1）
    /// </summary>
    public int Codecid { get; set; }

    /// <summary>
    /// 码率
    /// </summary>
    public long Bandwidth { get; set; }
}
