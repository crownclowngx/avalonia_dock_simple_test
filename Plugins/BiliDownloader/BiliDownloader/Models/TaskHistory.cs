namespace BiliDownloader.Models;

/// <summary>历史中心的文件存在性状态。该状态只属于当前会话，不进入任务事实表。</summary>
public enum FilePresenceStatus
{
    Unknown,
    Exists,
    Missing,
    Inaccessible,
}

/// <summary>历史导出格式。</summary>
public enum TaskHistoryExportFormat
{
    Csv,
    Json,
}

/// <summary>
/// 历史查询条件。终态集合为空表示完成、失败和取消三类历史全部参与查询。
/// CreatedFrom 使用本地时间语义，与现有 SQLite created_at 字段保持兼容。
/// </summary>
public sealed record TaskHistoryQuery(
    string? Title = null,
    string? DocumentId = null,
    IReadOnlySet<DownloadTaskStatus>? Statuses = null,
    DateTime? CreatedFrom = null,
    VideoCodecPreference? SelectedVideoCodec = null,
    OutputContainer? OutputContainer = null,
    OutputMediaMode? OutputMediaMode = null,
    bool IncludeUnknownVideoCodec = false,
    bool IncludeUnknownOutputContainer = false,
    bool IncludeUnknownOutputMode = false);

/// <summary>不暴露 SQL 偏移量的历史分页请求；Cursor 只允许原样回传。</summary>
public sealed record TaskHistoryPageRequest(int PageSize = 100, string? Cursor = null);

/// <summary>历史列表使用的稳定 Document 选项。</summary>
public sealed record TaskHistoryDocumentOption(string Id, string Label);

/// <summary>
/// 历史任务的不可变只读投影。它不包含 Cookie、临时目录、路径保留键和原始 Extras 文本；
/// CoverUrl 仅用于重新下载映射且已经在入库时去除查询签名，导出白名单不会包含该字段。
/// </summary>
public sealed record TaskHistoryEntry(
    string TaskId,
    string RedownloadedFromTaskId,
    string DocumentId,
    string SourceDocumentTitle,
    string SeriesTitle,
    string ItemTitle,
    long Aid,
    string Bvid,
    long Cid,
    string MediaUnitKey,
    string MediaType,
    long EpId,
    long SeasonId,
    int DurationSeconds,
    int VideoQualityId,
    int AudioQualityId,
    VideoCodecPreference? SelectedVideoCodec,
    string ActualVideoCodec,
    OutputContainer? OutputContainer,
    OutputMediaMode? OutputMediaMode,
    VideoDynamicRangePreference? VideoDynamicRangePreference,
    AudioFeaturePreference? AudioFeaturePreference,
    MediaFeatureFlags? RequestedMediaFeatures,
    MediaFeatureFlags? ExpectedMediaFeatures,
    MediaFeatureFlags? ActualMediaFeatures,
    string OutputFilePath,
    string Status,
    string? ErrorType,
    string? ErrorMessage,
    bool IsRetryable,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    int SubmissionSnapshotVersion,
    FileConflictPolicy ConflictPolicy,
    string OutputDirectory,
    bool UseGroupFolder,
    bool AddIndexToTitle,
    string NamingTemplate,
    string? PresetId,
    int ExtrasConfig,
    string CoverUrl)
{
    public bool HasExactSubmissionSnapshot => SubmissionSnapshotVersion >= 1;
    public bool HasExactHighSpecificationSnapshot => SubmissionSnapshotVersion >= 2
        && VideoDynamicRangePreference.HasValue && AudioFeaturePreference.HasValue;

    public static TaskHistoryEntry FromRecord(DownloadTaskRecord record) => new(
        record.TaskId,
        record.RedownloadedFromTaskId,
        record.DocumentId,
        record.SourceDocumentTitle,
        record.SeriesTitle,
        record.ItemTitle,
        record.Aid,
        record.Bvid,
        record.Cid,
        record.MediaUnitKey,
        record.MediaType,
        record.EpId,
        record.SeasonId,
        record.DurationSeconds,
        record.QualityId,
        record.AudioQualityId,
        record.SelectedVideoCodec,
        record.ActualVideoCodec,
        record.SelectedOutputContainer,
        record.SelectedOutputMediaMode,
        record.SelectedVideoDynamicRangePreference,
        record.SelectedAudioFeaturePreference,
        record.RequestedMediaFeatures,
        record.ExpectedMediaFeatures,
        record.ActualMediaFeatures,
        record.OutputFilePath,
        record.Status,
        record.ErrorType,
        record.ErrorMessage,
        record.IsRetryable,
        record.CreatedAt,
        record.LastUpdatedAt,
        record.SubmissionSnapshotVersion,
        record.ConflictPolicy,
        record.OutputDirectory,
        record.UseGroupFolder,
        record.AddIndexToTitle,
        record.NamingTemplate,
        record.PresetId,
        record.ExtrasConfig,
        record.CoverUrl);
}

/// <summary>历史分页结果。</summary>
public sealed record TaskHistoryPage(
    IReadOnlyList<TaskHistoryEntry> Items,
    string? NextCursor,
    bool HasMore);

/// <summary>导出范围；TaskIds 非空时优先于 Query。</summary>
public sealed record TaskHistoryExportRequest(
    string DestinationPath,
    TaskHistoryExportFormat Format,
    TaskHistoryQuery Query,
    IReadOnlyCollection<string>? TaskIds = null,
    IReadOnlyDictionary<string, FilePresenceStatus>? KnownFileStatuses = null);

public sealed record TaskHistoryExportResult(int ExportedCount, string DestinationPath);

/// <summary>历史中心重新下载前生成的计划。</summary>
public sealed record TaskHistoryRedownloadPlan(
    DownloadSubmission Submission,
    IReadOnlyList<string> CompatibilityWarnings)
{
    public bool RequiresCompatibilityConfirmation => CompatibilityWarnings.Count > 0;
}
