namespace BiliDownloader.Services.Api;

/// <summary>个人来源 API 返回的公开列表项，不包含 Cookie 或临时播放地址。</summary>
public sealed record BiliCatalogItem(
    long Aid,
    string Bvid,
    string Title,
    string? Author,
    long PublishedUnixSeconds,
    string? CoverUrl);

public sealed record BiliCatalogPage(
    IReadOnlyList<BiliCatalogItem> Items,
    string? NextToken,
    bool HasMore);

public sealed record BiliFavoriteFolder(long MediaId, string Title, int MediaCount);

public interface IBiliUploaderCatalogApi
{
    Task<BiliCatalogPage> GetUploaderVideosAsync(
        long uploaderId, int pageSize, string? continuationToken, string cookie,
        CancellationToken cancellationToken);
}

public interface IBiliFavoriteCatalogApi
{
    Task<IReadOnlyList<BiliFavoriteFolder>> GetFavoriteFoldersAsync(
        long userId, string cookie, CancellationToken cancellationToken);

    Task<BiliCatalogPage> GetFavoriteItemsAsync(
        long mediaId, int pageSize, string? continuationToken, string cookie,
        CancellationToken cancellationToken);
}

public interface IBiliWatchLaterCatalogApi
{
    Task<IReadOnlyList<BiliCatalogItem>> GetWatchLaterAsync(
        string cookie, CancellationToken cancellationToken);
}

public interface IBiliHistoryCatalogApi
{
    Task<BiliCatalogPage> GetHistoryAsync(
        int pageSize, string? continuationToken, string cookie,
        CancellationToken cancellationToken);
}
