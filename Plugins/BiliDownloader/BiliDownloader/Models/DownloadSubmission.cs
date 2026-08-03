namespace BiliDownloader.Models;

/// <summary>A stable, immutable snapshot of the settings used by one submitted batch.</summary>
public sealed record DownloadProfileSnapshot(
    int VideoQualityId,
    int AudioQualityId,
    string OutputDirectory,
    bool UseGroupFolder,
    bool AddIndexToTitle,
    bool DownloadDanmaku,
    bool DownloadSubtitle,
    bool DownloadCover,
    string NamingTemplate,
    string? PresetId = null,
    FileConflictPolicy ConflictPolicy = FileConflictPolicy.AutoNumber);

/// <summary>An immutable media item detached from parser and UI state.</summary>
public sealed record DownloadSubmissionItem(
    string ItemId,
    string Title,
    long Aid,
    string Bvid,
    long Cid,
    int Duration,
    BiliMediaType MediaType,
    long EpId,
    long SeasonId,
    string CoverUrl);

/// <summary>The only payload accepted by the download scheduling boundary.</summary>
public sealed record DownloadSubmission(
    string DocumentId,
    string DocumentTitle,
    string SeriesTitle,
    DownloadProfileSnapshot Profile,
    IReadOnlyList<DownloadSubmissionItem> Items);
