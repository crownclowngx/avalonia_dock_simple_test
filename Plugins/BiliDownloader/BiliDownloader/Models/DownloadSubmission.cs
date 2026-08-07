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
    FileConflictPolicy ConflictPolicy = FileConflictPolicy.AutoNumber,
    VideoCodecPreference VideoCodecPreference = VideoCodecPreference.AutoCompatibility,
    OutputContainer OutputContainer = OutputContainer.Mp4,
    OutputMediaMode OutputMediaMode = OutputMediaMode.AudioVideo)
{
    public RenditionSpecification ToRenditionSpecification() => new RenditionSpecification(
        VideoQualityId,
        AudioQualityId,
        VideoCodecPreference,
        OutputContainer,
        OutputMediaMode).Canonicalize();
}

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
    IReadOnlyList<DownloadSubmissionItem> Items,
    IncrementalSubmissionExpectation? IncrementalExpectation = null,
    string? RedownloadedFromTaskId = null);

/// <summary>
/// 增量预览交给提交边界的最小期望。Coordinator 不信任 UI 状态，只使用该 token 判断
/// “检查时为 New”的事实是否已被其他 Document 改变。
/// </summary>
public sealed record IncrementalSubmissionExpectation(
    string ComparisonToken,
    IReadOnlyList<string> ExpectedNewRenditionFingerprints);
