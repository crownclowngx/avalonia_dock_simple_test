using BiliDownloader.Models;

namespace BiliDownloader.Services.Download;

/// <summary>从 DASH 可用流中选择用户明确授权的输入流。</summary>
public interface IMediaStreamSelectionPolicy
{
    /// <summary>
    /// 在目标画质和模式范围内选择流。该方法必须无副作用，且显式编码不可用时只能返回失败，
    /// 禁止跨画质或跨编码静默降级。
    /// </summary>
    MediaSelectionResult Select(BiliDashResult dash, MediaSelectionRequest request);
}

/// <summary>输出组合、扩展名与无关身份维度规范化的唯一业务规则源。</summary>
public interface IOutputArtifactPolicy
{
    /// <summary>判断模式与容器是否属于产品允许的组合。</summary>
    bool IsValidCombination(OutputMediaMode mode, OutputContainer container);

    /// <summary>返回指定模式可向 UI 展示和预检接受的容器集合。</summary>
    IReadOnlyList<OutputContainer> GetAllowedContainers(OutputMediaMode mode);

    /// <summary>根据已验证的模式、容器及音频编码给出最终文件扩展名。</summary>
    string GetFileExtension(OutputMediaMode mode, OutputContainer container, AudioCodec audioCodec = AudioCodec.Unknown);

    /// <summary>清除当前输出模式不消费的配置维度，生成稳定的 rendition 身份。</summary>
    RenditionSpecification Canonicalize(RenditionSpecification specification);
}

/// <summary>
/// P1-G7 流选择策略。实现保持纯函数特性，不访问网络、磁盘或 ffmpeg，
/// 从而能够穷举测试显式编码、自动兼容顺序和非法降级路径。
/// </summary>
public sealed class MediaStreamSelectionPolicy(IOutputArtifactPolicy outputPolicy) : IMediaStreamSelectionPolicy
{
    private static readonly VideoCodec[] CompatibilityOrder = [VideoCodec.Avc, VideoCodec.Hevc, VideoCodec.Av1];

    /// <inheritdoc />
    public MediaSelectionResult Select(BiliDashResult dash, MediaSelectionRequest request)
    {
        if (!outputPolicy.IsValidCombination(request.OutputMediaMode, request.OutputContainer))
            return MediaSelectionResult.Failed(
                MediaSelectionFailureCode.InvalidOutputCombination,
                "输出模式与容器组合不合法。音视频/仅视频只能使用 MP4 或 MKV，仅音频只能使用原生音频。");

        BiliDashStream? video = null;
        var available = Array.Empty<VideoCodec>();
        if (request.OutputMediaMode is OutputMediaMode.AudioVideo or OutputMediaMode.VideoOnly)
        {
            var candidates = dash.VideoStreams.Where(stream => stream.Id == request.VideoQualityId)
                .Select(stream => (Stream: stream, Codec: DetectVideoCodec(stream)))
                .Where(candidate => candidate.Codec != VideoCodec.Unknown)
                .ToArray();
            available = candidates.Select(candidate => candidate.Codec)
                .Distinct().OrderBy(codec => Array.IndexOf(CompatibilityOrder, codec)).ToArray();
            if (candidates.Length == 0)
                return MediaSelectionResult.Failed(
                    MediaSelectionFailureCode.VideoStreamUnavailable,
                    $"画质 Q{request.VideoQualityId} 没有可识别的视频流。", available);

            var requestedCodec = request.VideoCodecPreference switch
            {
                VideoCodecPreference.Avc => VideoCodec.Avc,
                VideoCodecPreference.Hevc => VideoCodec.Hevc,
                VideoCodecPreference.Av1 => VideoCodec.Av1,
                _ => CompatibilityOrder.FirstOrDefault(available.Contains),
            };
            video = candidates.Where(candidate => candidate.Codec == requestedCodec)
                .OrderByDescending(candidate => candidate.Stream.Bandwidth)
                .Select(candidate => candidate.Stream).FirstOrDefault();
            if (video is null)
            {
                var text = available.Length == 0 ? "无" : string.Join("、", available.Select(ToDisplayText));
                return MediaSelectionResult.Failed(
                    MediaSelectionFailureCode.ExplicitVideoCodecUnavailable,
                    $"画质 Q{request.VideoQualityId} 不提供 {ToDisplayText(requestedCodec)}；可用编码：{text}。",
                    available);
            }
        }

        BiliDashStream? audio = null;
        if (request.OutputMediaMode is OutputMediaMode.AudioVideo or OutputMediaMode.AudioOnly)
        {
            var standardAudio = dash.AudioStreams
                .Where(stream => stream.AudioFeature == BiliAudioFeature.Standard)
                .ToArray();
            audio = (request.AudioQualityId > 0
                    ? standardAudio.Where(stream => stream.Id == request.AudioQualityId)
                        .OrderByDescending(stream => stream.Bandwidth).FirstOrDefault()
                    : null)
                ?? standardAudio.OrderByDescending(stream => stream.Bandwidth).FirstOrDefault();
            if (audio is null)
                return MediaSelectionResult.Failed(
                    MediaSelectionFailureCode.AudioStreamUnavailable,
                    "没有可用的普通音频流；Hi-Res 与杜比音频将在 P1-G8 提供显式选择。");
            if (DetectAudioCodec(audio) != AudioCodec.Aac)
                return MediaSelectionResult.Failed(
                    MediaSelectionFailureCode.UnsupportedAudioCodec,
                    "当前普通音频流不是 G7 支持的 AAC/MP4A，已阻止未知格式输出。");
        }

        var videoCodec = video is null ? VideoCodec.Unknown : DetectVideoCodec(video);
        var audioCodec = audio is null ? AudioCodec.Unknown : DetectAudioCodec(audio);
        var extension = outputPolicy.GetFileExtension(request.OutputMediaMode, request.OutputContainer, audioCodec);
        return new MediaSelectionResult(
            true,
            video,
            audio,
            new MediaOutputPlan(
                videoCodec,
                audioCodec,
                request.OutputContainer,
                request.OutputMediaMode,
                extension,
                video?.Bandwidth ?? 0,
                audio?.Bandwidth ?? 0),
            MediaSelectionFailureCode.None,
            string.Empty,
            available);
    }

    internal static VideoCodec DetectVideoCodec(BiliDashStream stream)
    {
        var byId = stream.Codecid switch { 7 => VideoCodec.Avc, 12 => VideoCodec.Hevc, 13 => VideoCodec.Av1, _ => VideoCodec.Unknown };
        var codec = stream.Codecs.Trim().ToLowerInvariant();
        var byText = codec switch
        {
            _ when codec.StartsWith("avc1", StringComparison.Ordinal) || codec.StartsWith("avc3", StringComparison.Ordinal) => VideoCodec.Avc,
            _ when codec.StartsWith("hev1", StringComparison.Ordinal) || codec.StartsWith("hvc1", StringComparison.Ordinal) => VideoCodec.Hevc,
            _ when codec.StartsWith("av01", StringComparison.Ordinal) => VideoCodec.Av1,
            _ => VideoCodec.Unknown,
        };
        return byId != VideoCodec.Unknown && byText != VideoCodec.Unknown && byId != byText
            ? VideoCodec.Unknown
            : byId != VideoCodec.Unknown ? byId : byText;
    }

    internal static AudioCodec DetectAudioCodec(BiliDashStream stream)
    {
        var codec = stream.Codecs.Trim().ToLowerInvariant();
        if (codec.StartsWith("mp4a", StringComparison.Ordinal) || codec.StartsWith("aac", StringComparison.Ordinal)) return AudioCodec.Aac;
        if (codec.StartsWith("flac", StringComparison.Ordinal) || stream.MimeType.Equals("audio/flac", StringComparison.OrdinalIgnoreCase)) return AudioCodec.Flac;
        if (codec.StartsWith("ec-3", StringComparison.Ordinal) || codec.StartsWith("eac3", StringComparison.Ordinal)) return AudioCodec.DolbyDigitalPlus;
        // B 站旧响应的普通音频可能只提供 codecid=0 和 audio/mp4；该组合是既有 AAC 流的稳定兼容表示。
        return stream.AudioFeature == BiliAudioFeature.Standard
               && (stream.MimeType.Equals("audio/mp4", StringComparison.OrdinalIgnoreCase)
                   || (stream.Codecid == 0 && string.IsNullOrWhiteSpace(stream.MimeType)
                       && string.IsNullOrWhiteSpace(stream.Codecs)))
            ? AudioCodec.Aac
            : AudioCodec.Unknown;
    }

    private static string ToDisplayText(VideoCodec codec) => codec switch
    {
        VideoCodec.Avc => "AVC/H.264",
        VideoCodec.Hevc => "HEVC/H.265",
        VideoCodec.Av1 => "AV1",
        _ => "未知编码",
    };
}

/// <summary>固定的 G7 输出矩阵；UI、预检、路径和指纹必须共同使用这一实现。</summary>
public sealed class OutputArtifactPolicy : IOutputArtifactPolicy
{
    private static readonly OutputContainer[] VideoContainers = [OutputContainer.Mp4, OutputContainer.Mkv];
    private static readonly OutputContainer[] AudioContainers = [OutputContainer.NativeAudio];

    /// <inheritdoc />
    public bool IsValidCombination(OutputMediaMode mode, OutputContainer container) => mode switch
    {
        OutputMediaMode.AudioVideo or OutputMediaMode.VideoOnly => container is OutputContainer.Mp4 or OutputContainer.Mkv,
        OutputMediaMode.AudioOnly => container == OutputContainer.NativeAudio,
        _ => false,
    };

    /// <inheritdoc />
    public IReadOnlyList<OutputContainer> GetAllowedContainers(OutputMediaMode mode)
        => mode == OutputMediaMode.AudioOnly ? AudioContainers : VideoContainers;

    /// <inheritdoc />
    public string GetFileExtension(OutputMediaMode mode, OutputContainer container, AudioCodec audioCodec = AudioCodec.Unknown)
    {
        if (!IsValidCombination(mode, container))
            throw new ArgumentException("输出模式与容器组合不合法。");
        if (mode == OutputMediaMode.AudioOnly)
        {
            if (audioCodec != AudioCodec.Aac)
                throw new ArgumentException("G7 原生音频只支持 AAC/MP4A。");
            return ".m4a";
        }
        return container == OutputContainer.Mkv ? ".mkv" : ".mp4";
    }

    /// <inheritdoc />
    public RenditionSpecification Canonicalize(RenditionSpecification specification) => specification.Canonicalize();
}
