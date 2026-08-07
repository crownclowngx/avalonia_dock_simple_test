namespace BiliDownloader.Models;

/// <summary>
/// 与 UI 控件无关的完整可复用下载意图。
/// </summary>
/// <remarks>
/// P1-G4 将后续输出能力先冻结为可持久化配置，但不在本组改变下载执行行为。
/// 新参数均位于末尾并提供兼容默认值，避免破坏 P0 调用方和旧预设反序列化。
/// </remarks>
public sealed record DownloadProfile(
    string QualityPreference,
    int AudioQualityId,
    bool UseGroupFolder,
    bool AddIndexToTitle,
    bool DownloadDanmaku,
    bool DownloadSubtitle,
    bool DownloadCover,
    string NamingTemplate,
    string OutputDirectory,
    FileConflictPolicy ConflictPolicy = FileConflictPolicy.AutoNumber,
    VideoCodecPreference VideoCodecPreference = VideoCodecPreference.AutoCompatibility,
    OutputContainer OutputContainer = OutputContainer.Mp4,
    OutputMediaMode OutputMediaMode = OutputMediaMode.AudioVideo,
    VideoDynamicRangePreference VideoDynamicRangePreference = VideoDynamicRangePreference.Auto,
    AudioFeaturePreference AudioFeaturePreference = AudioFeaturePreference.Auto,
    SubtitleOptions? SubtitleOptions = null,
    DanmakuOptions? DanmakuOptions = null,
    long PerTaskRateLimitBytesPerSecond = 0)
{
    public static DownloadProfile Default { get; } = new(
        "720p", 0, false, true, false, false, false, "{index}.{title}", "",
        FileConflictPolicy.AutoNumber);

    /// <summary>将旧调用方传入的空值规范化为明确的“无字幕”配置。</summary>
    public SubtitleOptions EffectiveSubtitleOptions => SubtitleOptions ?? global::BiliDownloader.Models.SubtitleOptions.None;

    /// <summary>将旧调用方传入的空值规范化为明确的“无弹幕”配置。</summary>
    public DanmakuOptions EffectiveDanmakuOptions => DanmakuOptions ?? global::BiliDownloader.Models.DanmakuOptions.None;
}
