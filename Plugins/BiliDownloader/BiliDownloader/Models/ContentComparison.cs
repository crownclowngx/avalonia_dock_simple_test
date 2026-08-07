using System.Security.Cryptography;
using System.Text;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.ContentSources;

namespace BiliDownloader.Models;

/// <summary>增量比较的五种互斥主状态；枚举顺序不表达优先级，优先级由分类策略统一维护。</summary>
public enum ContentComparisonStatus
{
    New,
    Downloaded,
    InProgress,
    Invalid,
    RuleExcluded,
}

/// <summary>
/// 生成输出版本身份所需的最小不可变参数。
/// 设计意图：只纳入真正改变主媒体输出的维度；Document、来源、命名、目录和附加资源
/// 都不能让同一输出版本绕过跨来源去重。
/// </summary>
public sealed record RenditionSpecification(
    int VideoQualityId,
    int AudioQualityId,
    VideoCodecPreference VideoCodecPreference,
    OutputContainer OutputContainer,
    OutputMediaMode OutputMediaMode)
{
    public void Validate()
    {
        if (OutputMediaMode != OutputMediaMode.AudioOnly && VideoQualityId <= 0)
            throw new ArgumentOutOfRangeException(nameof(VideoQualityId), "包含视频的输出必须具有正数视频质量 ID。");
        if (OutputMediaMode == OutputMediaMode.AudioOnly && VideoQualityId != 0)
            throw new ArgumentOutOfRangeException(nameof(VideoQualityId), "仅音频输出的规范化视频质量 ID 必须为 0。");
        if (AudioQualityId < 0)
            throw new ArgumentOutOfRangeException(nameof(AudioQualityId), "音频质量 ID 不能为负数。");
        if (!Enum.IsDefined(VideoCodecPreference))
            throw new ArgumentOutOfRangeException(nameof(VideoCodecPreference));
        if (!Enum.IsDefined(OutputContainer))
            throw new ArgumentOutOfRangeException(nameof(OutputContainer));
        if (!Enum.IsDefined(OutputMediaMode))
            throw new ArgumentOutOfRangeException(nameof(OutputMediaMode));
    }

    /// <summary>
    /// 清除当前输出模式不会消费的质量维度，避免隐藏设置变化制造内容完全相同的新版本。
    /// </summary>
    public RenditionSpecification Canonicalize() => OutputMediaMode switch
    {
        OutputMediaMode.AudioOnly => this with
        {
            VideoQualityId = 0,
            VideoCodecPreference = VideoCodecPreference.AutoCompatibility,
            OutputContainer = OutputContainer.NativeAudio,
        },
        OutputMediaMode.VideoOnly => this with { AudioQualityId = 0 },
        _ => this,
    };
}

/// <summary>
/// 一个具体输出版本的稳定指纹。值使用版本前缀和 SHA-256，既能建立定长索引，
/// 又不会把未来新增字段与当前算法混淆。
/// </summary>
public readonly record struct RenditionFingerprint
{
    public const string Prefix = "rf1:";

    public RenditionFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith(Prefix, StringComparison.Ordinal) ||
            value.Length != Prefix.Length + 64 ||
            !value.AsSpan(Prefix.Length).ToString().All(Uri.IsHexDigit))
            throw new ArgumentException("输出版本指纹格式无效。", nameof(value));
        Value = value.ToLowerInvariant();
    }

    public string Value { get; }

    public static RenditionFingerprint Create(MediaUnitKey mediaUnitKey, RenditionSpecification specification)
    {
        specification = specification.Canonicalize();
        specification.Validate();
        var canonical = string.Join('|',
            "rf1",
            mediaUnitKey.ToStorageKey(),
            $"vq={specification.VideoQualityId}",
            $"aq={specification.AudioQualityId}",
            $"codec={(int)specification.VideoCodecPreference}",
            $"container={(int)specification.OutputContainer}",
            $"mode={(int)specification.OutputMediaMode}");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new RenditionFingerprint(Prefix + hash);
    }

    public static bool TryParse(string? value, out RenditionFingerprint fingerprint)
    {
        try
        {
            fingerprint = new RenditionFingerprint(value ?? string.Empty);
            return true;
        }
        catch (ArgumentException)
        {
            fingerprint = default;
            return false;
        }
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>比较结果中的结构化提示；Code 供 UI 和测试判断，Message 只负责中文展示。</summary>
public sealed record ContentComparisonWarning(string Code, string Message, bool RequiresConfirmation = false);

/// <summary>
/// 单个媒体单元的比较结果。SourceKeys 保存同批次内的全部来源证据，ResolvedItem 仅存在于内存，
/// 不进入 Document 或 SQLite。
/// </summary>
public sealed record ContentComparisonResult(
    MediaUnitKey? MediaUnitKey,
    RenditionFingerprint? RenditionFingerprint,
    ContentComparisonStatus Status,
    string Title,
    IReadOnlyList<ContentItemKey> SourceKeys,
    BiliVideoItem? ResolvedItem,
    IReadOnlyList<ContentComparisonWarning> Warnings)
{
    public bool IsSelectedByDefault => Status == ContentComparisonStatus.New;
    public bool CanSubmit => Status == ContentComparisonStatus.New && ResolvedItem is not null;
}

/// <summary>一次检查的只读结果；只有 IsComplete=true 时 ProposedBaseline 才可写回 Document。</summary>
public sealed record IncrementalComparisonSnapshot(
    IReadOnlyList<ContentComparisonResult> Results,
    bool IsComplete,
    string ComparisonToken,
    IncrementalBaselineSaveData? ProposedBaseline,
    IReadOnlyList<ContentComparisonWarning> Warnings,
    IncrementalSourceScanSnapshot? SourceSnapshot = null);
