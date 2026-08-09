namespace BiliDownloader.Models;

/// <summary>视频编码选择意图；只有自动模式允许后续执行阶段按兼容顺序选择。</summary>
public enum VideoCodecPreference
{
    AutoCompatibility,
    Avc,
    Hevc,
    Av1,
}

/// <summary>最终输出容器意图；实际合法组合由 P1-G7 的预检策略负责。</summary>
public enum OutputContainer
{
    Mp4,
    Mkv,
    NativeAudio,
}

/// <summary>需要生成的媒体流集合。</summary>
public enum OutputMediaMode
{
    AudioVideo,
    VideoOnly,
    AudioOnly,
}

/// <summary>视频动态范围偏好；Auto 不代表强制选择高规格媒体。</summary>
public enum VideoDynamicRangePreference
{
    Auto,
    Standard,
    Hdr,
    DolbyVision,
}

/// <summary>音频能力偏好；显式值在后续预检中不得被静默降级。</summary>
public enum AudioFeaturePreference
{
    Auto,
    Standard,
    HiRes,
    DolbyAtmos,
}

/// <summary>字幕选择范围。</summary>
public enum SubtitleSelectionMode
{
    None,
    All,
    SelectedLanguages,
}

/// <summary>字幕文本输出格式。</summary>
public enum SubtitleOutputFormat
{
    Srt,
    Ass,
    Vtt,
}

/// <summary>字幕交付方式。</summary>
public enum SubtitleDeliveryMode
{
    External,
    SoftMuxed,
    ExternalAndSoftMuxed,
}

/// <summary>
/// 可复用的字幕配置值对象。
/// </summary>
/// <remarks>
/// 语言使用平台稳定键而不是显示名称；当前功能组只负责保存意图，
/// 字幕获取、转换和封装由 P1-G9 实现。
/// </remarks>
public sealed record SubtitleOptions
{
    public SubtitleSelectionMode SelectionMode { get; init; } = SubtitleSelectionMode.None;
    public IReadOnlyList<string> LanguageKeys { get; init; } = Array.Empty<string>();
    public SubtitleOutputFormat OutputFormat { get; init; } = SubtitleOutputFormat.Srt;
    public SubtitleDeliveryMode DeliveryMode { get; init; } = SubtitleDeliveryMode.External;

    public static SubtitleOptions None => new();

    public static SubtitleOptions LegacyEnabled => new()
    {
        SelectionMode = SubtitleSelectionMode.All,
        OutputFormat = SubtitleOutputFormat.Srt,
        DeliveryMode = SubtitleDeliveryMode.External,
    };

    /// <summary>
    /// 将来自 Document、预设或旧任务的配置归一化为稳定值。
    /// 设计意图是把“去空、去重、固定顺序”集中在值对象内部，避免 UI、提交边界和执行器
    /// 各自生成略有差异的任务快照。语言键只做不区分大小写的比较，不擅自改写平台含义。
    /// </summary>
    public SubtitleOptions Canonicalize()
    {
        if (SelectionMode == SubtitleSelectionMode.None)
            return None;

        var languageKeys = SelectionMode == SubtitleSelectionMode.SelectedLanguages
            ? LanguageKeys
                .Select(static key => key?.Trim() ?? string.Empty)
                .Where(static key => key.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        return this with { LanguageKeys = languageKeys };
    }

    /// <summary>
    /// 校验配置内部一致性。容器与输出模式的兼容性由提交预检负责，值对象只校验自身，
    /// 从而保持单一职责并允许文档离线恢复尚未选择媒体的配置。
    /// </summary>
    public void Validate()
    {
        if (!Enum.IsDefined(SelectionMode)
            || !Enum.IsDefined(OutputFormat)
            || !Enum.IsDefined(DeliveryMode))
            throw new ArgumentException("字幕配置包含未知枚举值。");
        if (SelectionMode == SubtitleSelectionMode.SelectedLanguages
            && Canonicalize().LanguageKeys.Count == 0)
            throw new ArgumentException("按语言选择字幕时至少需要一个稳定语言键。");
    }
}

/// <summary>弹幕外置文件格式；弹幕不在本功能组内嵌到视频轨。</summary>
public enum DanmakuOutputFormat
{
    Xml,
    Ass,
    Json,
}

/// <summary>
/// 可复用的弹幕配置值对象。
/// </summary>
/// <remarks>
/// ASS 样式以稳定 ID 表达，避免 P1-G4 提前冻结尚未实现的字体和排版细节。
/// P1-G9 可以在保持 V3 向前兼容的前提下增加结构化样式字段。
/// </remarks>
public sealed record DanmakuOptions
{
    public IReadOnlyList<DanmakuOutputFormat> Formats { get; init; } = Array.Empty<DanmakuOutputFormat>();
    public string AssStyleId { get; init; } = "default";

    public static DanmakuOptions None => new();

    public static DanmakuOptions LegacyEnabled => new()
    {
        Formats = new[] { DanmakuOutputFormat.Xml },
    };

    /// <summary>统一格式顺序并拒绝重复值，保证 Document 与 SQLite JSON 可以稳定比较。</summary>
    public DanmakuOptions Canonicalize() => this with
    {
        Formats = Formats
            .Where(Enum.IsDefined)
            .Distinct()
            .OrderBy(static value => value)
            .ToArray(),
        AssStyleId = string.IsNullOrWhiteSpace(AssStyleId) ? "default" : AssStyleId.Trim(),
    };

    /// <summary>验证弹幕配置；G9 仅发布内置 default 样式，未知样式不能静默回退。</summary>
    public void Validate()
    {
        if (Formats.Any(static value => !Enum.IsDefined(value)))
            throw new ArgumentException("弹幕配置包含未知输出格式。");
        if (!string.Equals(Canonicalize().AssStyleId, "default", StringComparison.Ordinal))
            throw new ArgumentException("当前版本仅支持内置 default 弹幕 ASS 样式。");
    }
}

/// <summary>输出相关枚举的中文展示集中映射，避免活动任务与历史中心产生不同文案。</summary>
public static class OutputOptionDisplay
{
    public static string ToDisplayText(this VideoCodecPreference value) => value switch
    {
        VideoCodecPreference.AutoCompatibility => "自动兼容",
        VideoCodecPreference.Avc => "AVC/H.264",
        VideoCodecPreference.Hevc => "HEVC/H.265",
        VideoCodecPreference.Av1 => "AV1",
        _ => "未知",
    };

    public static string ToDisplayText(this OutputContainer value) => value switch
    {
        OutputContainer.Mp4 => "MP4",
        OutputContainer.Mkv => "MKV",
        OutputContainer.NativeAudio => "原生音频",
        _ => "未知",
    };

    public static string ToDisplayText(this OutputMediaMode value) => value switch
    {
        OutputMediaMode.AudioVideo => "音视频",
        OutputMediaMode.VideoOnly => "仅视频",
        OutputMediaMode.AudioOnly => "仅音频",
        _ => "未知",
    };

    public static string ActualCodecToDisplayText(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "avc" => "AVC/H.264",
        "hevc" => "HEVC/H.265",
        "av1" => "AV1",
        _ => "未知",
    };

    public static string ToDisplayText(this MediaFeatureFlags? value)
    {
        if (!value.HasValue) return "高规格未知";
        if (value.Value == MediaFeatureFlags.None) return "标准规格";
        var labels = new List<string>();
        if (value.Value.HasFlag(MediaFeatureFlags.DolbyVision)) labels.Add("杜比视界");
        else if (value.Value.HasFlag(MediaFeatureFlags.Hdr)) labels.Add("HDR");
        if (value.Value.HasFlag(MediaFeatureFlags.DolbyAtmos)) labels.Add("杜比全景声");
        else if (value.Value.HasFlag(MediaFeatureFlags.HiResAudio)) labels.Add("Hi-Res");
        return labels.Count == 0 ? "标准规格" : string.Join(" + ", labels);
    }
}
