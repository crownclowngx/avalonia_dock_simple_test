using BiliDownloader.Models;

namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// ffmpeg 运行时来源。该枚举是展示层与探测层之间的稳定契约，
/// UI 不需要根据路径文本猜测当前使用的是用户文件、托管安装还是系统 PATH。
/// </summary>
public enum FfmpegRuntimeSource
{
    /// <summary>没有候选通过运行时验证。</summary>
    None,
    /// <summary>用户选择并保存的自定义可执行文件。</summary>
    Custom,
    /// <summary>由插件可信安装流程管理、通过活动指针选择的版本。</summary>
    Managed,
    /// <summary>随插件目录部署的兼容候选。</summary>
    Plugin,
    /// <summary>从当前进程继承的系统 PATH 中找到的候选。</summary>
    Path,
}

/// <summary>
/// 一次完整探测得到的不可变结果。
/// <paramref name="IsReady"/> 只有在可执行文件通过 <c>-version</c> 进程探测后才为真，
/// 从而把“文件存在”与“运行时真正可用”区分开。
/// </summary>
public sealed record FfmpegRuntimeStatus(
    bool IsReady,
    string? ExecutablePath,
    string? Version,
    FfmpegRuntimeSource Source,
    string Message);

/// <summary>
/// ffmpeg 运行时定位边界，只负责配置、路径发现和可用性探测。
/// 下载、安装与媒体合并不属于本接口，避免设置页或提交预检依赖不需要的副作用。
/// </summary>
public interface IFfmpegRuntimeLocator
{
    /// <summary>用户明确选择的自定义可执行文件；无效值会被探测流程跳过。</summary>
    string? CustomPath { get; set; }

    /// <summary>按既定优先级解析到的现存文件路径；该属性不启动外部进程。</summary>
    string? ResolvedPath { get; }

    /// <summary>最近一次真实进程探测是否成功；路径变化后立即失效为 false。</summary>
    bool IsReady { get; }

    /// <summary>按“自定义、托管、插件目录、PATH”顺序查找第一个现存候选。</summary>
    string? ResolveFfmpegPath();

    /// <summary>通过执行 <c>-version</c> 验证指定路径。</summary>
    Task<bool> ValidatePathAsync(string path, CancellationToken ct = default);

    /// <summary>重新枚举并验证候选，返回可直接展示的结构化状态。</summary>
    Task<FfmpegRuntimeStatus> DetectAsync(CancellationToken ct = default);
}

/// <summary>
/// 媒体封装边界。下载服务只依赖这一项能力，不应知道 ffmpeg 来自何处或如何安装。
/// </summary>
public interface IMediaMuxer
{
    /// <summary>将已验证的视频流和音频流无转码封装到目标文件。</summary>
    Task MergeAsync(string videoPath, string audioPath, string outputPath, CancellationToken ct = default);

    /// <summary>
    /// 按明确输出模式执行无转码封装。默认实现仅兼容历史音视频调用；支持仅视频的生产实现
    /// 必须覆盖此方法并严格校验输入数量。
    /// </summary>
    Task MuxAsync(MediaMuxRequest request, CancellationToken ct = default)
    {
        if (request.OutputMediaMode != OutputMediaMode.AudioVideo
            || string.IsNullOrWhiteSpace(request.VideoPath)
            || string.IsNullOrWhiteSpace(request.AudioPath))
            throw new NotSupportedException("当前媒体封装器只支持兼容音视频合并。");
        return MergeAsync(request.VideoPath, request.AudioPath, request.OutputPath, ct);
    }
}

/// <summary>交给 ffmpeg 的字幕轨描述；InputPath 是暂存文件，LanguageKey 和 Title 写入媒体元数据。</summary>
public sealed record SubtitleMuxTrack(
    string InputPath,
    string LanguageKey,
    string Title,
    SubtitleOutputFormat SourceFormat);

/// <summary>
/// 软字幕封装窄接口。它与初始音视频合并分开，避免普通下载服务被字幕目录和语言策略污染。
/// 实现必须只生成候选文件，原子替换现有主媒体由上层附加资源编排器负责。
/// </summary>
public interface ISubtitleMediaMuxer
{
    Task MuxSubtitlesAsync(
        string inputMediaPath,
        IReadOnlyList<SubtitleMuxTrack> tracks,
        string outputPath,
        OutputContainer outputContainer,
        CancellationToken cancellationToken = default)
        => Task.FromException(new NotSupportedException("当前媒体封装器不支持软字幕。"));
}

/// <summary>软字幕发布前验证边界；必须证明 codec、语言和标题元数据符合预期。</summary>
public interface ISubtitleTrackVerifier
{
    Task VerifyAsync(
        string mediaPath,
        IReadOnlyList<SubtitleMuxTrack> expectedTracks,
        OutputContainer outputContainer,
        CancellationToken cancellationToken = default);
}

/// <summary>ffmpeg muxer 能力读取边界；提交预检只依赖此窄接口。</summary>
public interface IMediaMuxerCapabilityProvider
{
    /// <summary>返回当前已验证运行时的 MP4/Matroska 封装能力。</summary>
    Task<MediaMuxerCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
        => Task.FromResult(new MediaMuxerCapabilities(true, true));
}

/// <summary>
/// 兼容旧构造路径的聚合接口。生产代码应优先依赖上面的窄接口；保留该接口可以让
/// 历史测试替身和第三方构造代码平滑迁移，而不会重新把职责塞回单个消费者。
/// </summary>
public interface IFfmpegService : IFfmpegRuntimeLocator, IMediaMuxer, IMediaMuxerCapabilityProvider, ISubtitleMediaMuxer;

/// <summary>未找到或无法启动 ffmpeg 时抛出的明确异常，错误分类不得再解析中文消息。</summary>
public sealed class FfmpegUnavailableException : Exception
{
    public FfmpegUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>ffmpeg 已启动但媒体合并失败；调用方应保留已验证的输入文件以便仅重试合并。</summary>
public sealed class MediaMergeException : Exception
{
    public MediaMergeException(string message, Exception? inner = null) : base(message, inner) { }
}
