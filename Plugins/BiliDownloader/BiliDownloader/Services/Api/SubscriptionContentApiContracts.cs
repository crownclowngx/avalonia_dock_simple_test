namespace BiliDownloader.Services.Api;

/// <summary>追番或追剧目录中的一个剧集系列。</summary>
public sealed record BiliFollowingSeason(
    long SeasonId,
    long MediaId,
    string Title,
    string? CoverUrl,
    int TotalCount,
    bool IsStarted,
    bool IsPlayable);

public sealed record BiliFollowingPage(
    IReadOnlyList<BiliFollowingSeason> Items,
    string? NextToken,
    bool HasMore);

/// <summary>
/// 番剧剧集的公开目录字段。
/// 设计意图：只传递权限分类所需事实，不把原始 rights JSON 泄漏到 Provider。
/// </summary>
public sealed record BiliBangumiEpisode(
    long Aid,
    string Bvid,
    long Cid,
    long EpisodeId,
    long SeasonId,
    string Title,
    int DurationSeconds,
    string? CoverUrl,
    bool HasStableIdentity,
    bool IsDrmProtected,
    bool IsRegionRestricted,
    bool IsExpired,
    bool IsNotReleased,
    bool IsExplicitlyAvailable);

public sealed record BiliBangumiSeasonDetail(
    long SeasonId,
    string Title,
    string? CoverUrl,
    IReadOnlyList<BiliBangumiEpisode> Episodes);

public interface IBiliFollowingCatalogApi
{
    Task<BiliFollowingPage> GetFollowingAsync(
        long userId,
        bool cinema,
        int pageSize,
        string? continuationToken,
        string cookie,
        CancellationToken cancellationToken);

    Task<BiliBangumiSeasonDetail> GetSeasonAsync(
        long seasonId,
        string cookie,
        CancellationToken cancellationToken);
}

public sealed record BiliCollectedFolder(
    long MediaId,
    string Title,
    string? OwnerName,
    string? CoverUrl,
    int MediaCount,
    bool IsExpired);

public sealed record BiliCollectedFolderPage(
    IReadOnlyList<BiliCollectedFolder> Items,
    string? NextToken,
    bool HasMore);

public interface IBiliCollectedFolderApi
{
    Task<BiliCollectedFolderPage> GetCollectedFoldersAsync(
        long userId,
        int pageSize,
        string? continuationToken,
        string cookie,
        CancellationToken cancellationToken);

    Task<BiliCatalogPage> GetFolderItemsAsync(
        long mediaId,
        int pageSize,
        string? continuationToken,
        string cookie,
        CancellationToken cancellationToken);
}

public sealed record BiliCourseSummary(
    long SeasonId,
    string Title,
    string? CoverUrl,
    int EpisodeCount,
    bool IsExpired);

public sealed record BiliCoursePage(
    IReadOnlyList<BiliCourseSummary> Items,
    string? NextToken,
    bool HasMore);

/// <summary>课程详情只保留目录显示和权限分类需要的字段，不包含价格或订单事实。</summary>
public sealed record BiliCourseDetail(
    long SeasonId,
    string Title,
    string? CoverUrl,
    string? Author,
    bool? IsPurchased,
    bool IsExpired);

public sealed record BiliCourseEpisode(
    long Aid,
    long Cid,
    long EpisodeId,
    string Title,
    int DurationSeconds,
    long PublishedUnixSeconds,
    string? CoverUrl,
    int? AccessStatus,
    bool? IsDrmProtected,
    bool? IsRegionRestricted,
    bool? IsReleased);

public sealed record BiliCourseEpisodePage(
    IReadOnlyList<BiliCourseEpisode> Items,
    string? NextToken,
    bool HasMore);

public interface IBiliCourseCatalogApi
{
    Task<BiliCoursePage> GetMyCoursesAsync(
        int pageSize,
        string? continuationToken,
        string cookie,
        CancellationToken cancellationToken);

    Task<BiliCourseDetail> GetCourseAsync(
        long? seasonId,
        long? episodeId,
        string cookie,
        CancellationToken cancellationToken);

    Task<BiliCourseEpisodePage> GetCourseEpisodesAsync(
        long seasonId,
        int pageSize,
        string? continuationToken,
        string cookie,
        CancellationToken cancellationToken);
}
