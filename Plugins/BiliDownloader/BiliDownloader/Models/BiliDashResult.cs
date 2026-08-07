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

    /// <summary>
    /// DASH 清单声明的 RFC 6381 编码字符串，例如 <c>avc1.640032</c>、
    /// <c>hev1.1.6.L150.90</c>、<c>av01.0.12M.08</c> 或 <c>mp4a.40.2</c>。
    /// </summary>
    public string Codecs { get; set; } = string.Empty;

    /// <summary>DASH 清单声明的 MIME 类型，例如 <c>video/mp4</c> 或 <c>audio/mp4</c>。</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// 根据 MIME 类型得到的源流容器提示。它描述下载输入而非最终输出容器，
    /// 因而不能替代用户选择的 <see cref="OutputContainer"/>。
    /// </summary>
    public DashContainerHint ContainerHint { get; set; }

    /// <summary>
    /// 音频流在播放信息中的来源分类。G7 只消费 <see cref="BiliAudioFeature.Standard"/>，
    /// 但保留杜比和 Hi-Res 分类供 G8 在不改动 API 映射层的前提下扩展。
    /// </summary>
    public BiliAudioFeature AudioFeature { get; set; }
}

/// <summary>DASH 输入流的容器提示；未知值必须保持未知，不能从临时 URL 扩展名猜测。</summary>
public enum DashContainerHint
{
    Unknown,
    Mp4,
    WebM,
    Flac,
}

/// <summary>音频流能力来源。普通音频与 G8 高规格音频在模型层显式分离。</summary>
public enum BiliAudioFeature
{
    Standard,
    Dolby,
    HiRes,
}
