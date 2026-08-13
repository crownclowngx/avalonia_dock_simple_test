using BiliDownloader.Models.ContentSources;

namespace BiliDownloader.Models;

/// <summary>
/// Document V3 使用的来源白名单 DTO。
/// </summary>
/// <remarks>
/// 设计意图：持久层不直接序列化 Provider 的运行时对象或任意参数字典。
/// 当前所有 Provider 唯一需要保存的公开参数是课程来源的 AutoOpen 标志。
/// Kind 使用字符串，使已知 V3 文件即使来自缺失或较新的 Provider 也能原样查看和另存。
/// </remarks>
public sealed class SourceDescriptorSaveData
{
    public string Kind { get; set; } = string.Empty;
    public string StableSourceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int CapabilityVersion { get; set; } = 1;
    public bool AutoOpen { get; set; }
}

/// <summary>筛选规则的持久化 DTO，与可变 UI 控件和 Provider 查询计划解耦。</summary>
public sealed class SourceFilterRulesSaveData
{
    public string? Keyword { get; set; }
    public DateTimeOffset? PublishedFrom { get; set; }
    public DateTimeOffset? PublishedTo { get; set; }
    public List<ContentSourceItemType> MediaTypes { get; set; } = [];
    public ContentSourceSortOrder SortOrder { get; set; } = ContentSourceSortOrder.ProviderDefault;
}

/// <summary>增量边界项目的白名单键，不保存标题、封面或远端页面内容。</summary>
public sealed class ContentItemKeySaveData
{
    public string SourceKind { get; set; } = string.Empty;
    public string NativeId { get; set; } = string.Empty;
}

/// <summary>
/// 轻量增量基线。P1-G4 只保存和恢复该意图，不执行增量比较。
/// </summary>
public sealed class IncrementalBaselineSaveData
{
    public const int CurrentVersion = 1;
    public const int MaximumBoundaryItemCount = ContentPageRequest.MaxPageSize;

    public int BaselineVersion { get; set; } = CurrentVersion;
    public DateTimeOffset? LastCompletedCheckAtUtc { get; set; }
    public string? SnapshotToken { get; set; }
    public List<ContentItemKeySaveData> BoundaryItemKeys { get; set; } = [];
}

/// <summary>
/// BiliDownloader Document V3 的稳定保存契约。
/// </summary>
/// <remarks>
/// V3 使用平铺字段和确定默认值，便于当前格式校验与缺失可选字段的安全处理。
/// 本 DTO 只表达用户意图和轻量基线，不得加入下载线程、完整任务状态、远端页面、
/// ContinuationToken、跨页勾选、Cookie、请求头或临时媒体地址。
/// </remarks>
public sealed class DocumentSaveDataV3
{
    public string DocumentId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string DownloadInfo { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public bool UseGroupFolder { get; set; }
    public bool AddIndexToTitle { get; set; } = true;
    public string PresetId { get; set; } = BuiltInPresets.CompatId;
    public string NamingTemplate { get; set; } = "{index}.{title}";
    public int QualityId { get; set; }
    public int AudioQualityId { get; set; }
    public bool DownloadDanmaku { get; set; }
    public bool DownloadSubtitle { get; set; }
    public bool DownloadCover { get; set; }
    public FileConflictPolicy ConflictPolicy { get; set; } = FileConflictPolicy.AutoNumber;

    public SourceDescriptorSaveData? Source { get; set; }
    public SourceFilterRulesSaveData Filters { get; set; } = new();
    public IncrementalBaselineSaveData Baseline { get; set; } = new();

    public VideoCodecPreference VideoCodecPreference { get; set; } = VideoCodecPreference.AutoCompatibility;
    public OutputContainer OutputContainer { get; set; } = OutputContainer.Mp4;
    public OutputMediaMode OutputMediaMode { get; set; } = OutputMediaMode.AudioVideo;
    public VideoDynamicRangePreference VideoDynamicRangePreference { get; set; } = VideoDynamicRangePreference.Auto;
    public AudioFeaturePreference AudioFeaturePreference { get; set; } = AudioFeaturePreference.Auto;
    public SubtitleOptions SubtitleOptions { get; set; } = SubtitleOptions.None;
    public DanmakuOptions DanmakuOptions { get; set; } = DanmakuOptions.None;
    public long PerTaskRateLimitBytesPerSecond { get; set; }
}
