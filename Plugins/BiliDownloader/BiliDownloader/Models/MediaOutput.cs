namespace BiliDownloader.Models;

/// <summary>从 DASH 元数据识别出的实际视频编码；Unknown 永远不能当作某个兼容编码使用。</summary>
public enum VideoCodec
{
    Unknown,
    Avc,
    Hevc,
    Av1,
}

/// <summary>G7 能识别的音频编码。高规格音频只解析元数据，不在本组主动选择。</summary>
public enum AudioCodec
{
    Unknown,
    Aac,
    Flac,
    DolbyDigitalPlus,
}

/// <summary>媒体流选择失败的稳定机器码；中文消息只负责展示，业务分支只判断该枚举。</summary>
public enum MediaSelectionFailureCode
{
    None,
    InvalidOutputCombination,
    VideoStreamUnavailable,
    ExplicitVideoCodecUnavailable,
    AudioStreamUnavailable,
    UnsupportedAudioCodec,
}

/// <summary>纯流选择策略的不可变输入。</summary>
public sealed record MediaSelectionRequest(
    int VideoQualityId,
    int AudioQualityId,
    VideoCodecPreference VideoCodecPreference,
    OutputContainer OutputContainer,
    OutputMediaMode OutputMediaMode);

/// <summary>
/// 可以安全进入预检报告和 SQLite 的媒体输出计划。该类型刻意不持有 URL、Cookie 或请求头，
/// 使预检指纹和任务事实只能包含稳定、可审计的媒体属性。
/// </summary>
public sealed record MediaOutputPlan(
    VideoCodec ActualVideoCodec,
    AudioCodec ActualAudioCodec,
    OutputContainer OutputContainer,
    OutputMediaMode OutputMediaMode,
    string FileExtension,
    long VideoBandwidth,
    long AudioBandwidth)
{
    /// <summary>当前模式是否必须选择并下载视频流。</summary>
    public bool RequiresVideo => OutputMediaMode is OutputMediaMode.AudioVideo or OutputMediaMode.VideoOnly;

    /// <summary>当前模式是否必须选择并下载普通音频流。</summary>
    public bool RequiresAudio => OutputMediaMode is OutputMediaMode.AudioVideo or OutputMediaMode.AudioOnly;

    /// <summary>当前模式是否需要调用 ffmpeg；原生音频发布刻意返回 false。</summary>
    public bool RequiresMuxer => OutputMediaMode != OutputMediaMode.AudioOnly;
}

/// <summary>
/// 纯策略返回值。成功时 SelectedVideo/SelectedAudio 仅在当前内存调用链中使用；
/// 持久化边界只能取 <see cref="OutputPlan"/>，从类型设计上阻止签名 URL 落库。
/// </summary>
public sealed record MediaSelectionResult(
    bool Success,
    BiliDashStream? SelectedVideo,
    BiliDashStream? SelectedAudio,
    MediaOutputPlan? OutputPlan,
    MediaSelectionFailureCode FailureCode,
    string Message,
    IReadOnlyList<VideoCodec> AvailableVideoCodecs)
{
    /// <summary>
    /// 创建结构化失败结果。可用编码集合只包含同一目标画质内已经可靠识别的编码，
    /// 调用方不得据此自动替换用户的显式编码选择。
    /// </summary>
    public static MediaSelectionResult Failed(
        MediaSelectionFailureCode code,
        string message,
        IReadOnlyList<VideoCodec>? available = null)
        => new(false, null, null, null, code, message, available ?? Array.Empty<VideoCodec>());
}

/// <summary>预检分析结果：安全输出计划与模式感知的峰值空间估算。</summary>
public sealed record MediaPreflightAnalysis(MediaOutputPlan OutputPlan, long? EstimatedPeakBytes);

/// <summary>一次网络媒体分析的完整结果；失败也以值返回，避免预检依赖中文异常文本分支。</summary>
public sealed record MediaPreflightResult(MediaSelectionResult Selection, long? EstimatedPeakBytes);

/// <summary>ffmpeg 当前运行时可提供的无转码封装能力。</summary>
public sealed record MediaMuxerCapabilities(bool SupportsMp4, bool SupportsMkv)
{
    /// <summary>
    /// 判断指定输出容器是否可用。NativeAudio 不经过 ffmpeg，因而不受 muxer 列表影响；
    /// 组合是否合法仍须由 <c>IOutputArtifactPolicy</c> 单独校验。
    /// </summary>
    public bool Supports(OutputContainer container) => container switch
    {
        OutputContainer.Mp4 => SupportsMp4,
        OutputContainer.Mkv => SupportsMkv,
        OutputContainer.NativeAudio => true,
        _ => false,
    };
}

/// <summary>模式化封装请求。调用方必须提供与输出模式严格一致的输入集合。</summary>
public sealed record MediaMuxRequest(
    string? VideoPath,
    string? AudioPath,
    string OutputPath,
    OutputContainer OutputContainer,
    OutputMediaMode OutputMediaMode);

/// <summary>一次成功提交后，Document 会话项与独立任务事实之间的关联。</summary>
public sealed record CommittedTaskReference(string SubmissionItemId, string TaskId);
