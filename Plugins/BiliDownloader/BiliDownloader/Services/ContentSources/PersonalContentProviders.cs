using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;

namespace BiliDownloader.Services.ContentSources;

/// <summary>把公开目录项解析为现有下载集合，确保所有来源复用同一解析边界。</summary>
public interface IContentSourceItemResolver
{
    Task<BiliVideoCollection> ResolveAsync(ContentSourceItem item, CancellationToken cancellationToken);
}

public sealed class ContentSourceItemResolver : IContentSourceItemResolver
{
    private readonly IBiliContentSourceApi _api;
    private readonly IBiliCredentialProvider _credentials;

    public ContentSourceItemResolver(IBiliContentSourceApi api, IBiliCredentialProvider credentials)
    {
        _api = api;
        _credentials = credentials;
    }

    public async Task<BiliVideoCollection> ResolveAsync(ContentSourceItem item, CancellationToken cancellationToken)
    {
        try
        {
            BiliVideoCollection collection;
            if (!string.IsNullOrWhiteSpace(item.Bvid))
                collection = await _api.GetVideoCollectionAsync(item.Bvid, true, _credentials.GetCookieHeader(), cancellationToken);
            else if (item.Aid is > 0)
                collection = await _api.GetVideoCollectionAsync($"av{item.Aid}", false, _credentials.GetCookieHeader(), cancellationToken);
            else
                throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "列表项缺少可解析的视频标识。");

            return ContentCollectionAdapter.Normalize(collection);
        }
        catch (Download.MediaAuthorizationException)
        {
            throw new ContentSourceException(ContentSourceErrorCode.LoginRequired, "此内容需要登录后才能访问。");
        }
    }
}

internal static class ContentCollectionAdapter
{
    public static BiliVideoCollection Normalize(BiliVideoCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var index = 1;
        foreach (var video in collection.Items)
        {
            if (video.Aid <= 0 || video.Cid <= 0)
                throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "内容源返回了无效的 Aid/Cid。");
            video.Index = index++;
            video.OriginalTitle = video.Title;
            video.CoverUrl = collection.Cover;
            video.MediaUnitKey = new MediaUnitKey(video.Aid, video.Cid);
        }
        return collection;
    }
}

/// <summary>个人来源 Provider 的模板方法基类，只承载描述符校验、映射和项目解析三个稳定共性。</summary>
public abstract class PersonalContentSourceProviderBase : IContentSourceProvider
{
    private readonly IContentSourceItemResolver _resolver;

    protected PersonalContentSourceProviderBase(IContentSourceItemResolver resolver) => _resolver = resolver;

    public abstract ContentSourceKind Kind { get; }
    public abstract ContentSourceCapabilities Capabilities { get; }
    public int CapabilityVersion => 1;
    public abstract ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken);
    public abstract Task<ContentPage> GetPageAsync(ContentSourceDescriptor descriptor, ContentPageRequest request, CancellationToken cancellationToken);

    public Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor, ContentSourceItem item, CancellationToken cancellationToken)
    {
        ValidateDescriptor(descriptor);
        if (item.Key.SourceKind != Kind)
            throw Protocol();
        return _resolver.ResolveAsync(item, cancellationToken);
    }

    protected void ValidateDescriptor(ContentSourceDescriptor descriptor)
    {
        if (descriptor.Kind != Kind || descriptor.CapabilityVersion != CapabilityVersion)
            throw Protocol();
    }

    protected ContentSourceItem Map(BiliCatalogItem item)
    {
        var nativeId = item.Aid > 0 ? $"aid:{item.Aid}" : $"bvid:{item.Bvid}";
        return new ContentSourceItem(
            new ContentItemKey(Kind, nativeId), item.Title, ContentSourceItemType.Video,
            item.Author, item.PublishedUnixSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(item.PublishedUnixSeconds) : null,
            item.CoverUrl, item.Aid > 0 ? item.Aid : null, item.Bvid);
    }

    protected static long ParsePositiveId(string input, string marker)
    {
        if (string.IsNullOrWhiteSpace(input)) throw InvalidInput();
        var match = Regex.Match(input.Trim(), $@"(?:{Regex.Escape(marker)}[:=/]?|^)(\d+)", RegexOptions.IgnoreCase);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out var id) || id <= 0) throw InvalidInput();
        return id;
    }

    protected static ContentSourceException InvalidInput() =>
        new(ContentSourceErrorCode.InvalidInput, "无法识别来源 ID 或链接。");
    protected static ContentSourceException Protocol() =>
        new(ContentSourceErrorCode.ProtocolViolation, "来源描述符与 Provider 契约不一致。");
}

public sealed class UploaderSourceProvider : PersonalContentSourceProviderBase
{
    private readonly IBiliUploaderCatalogApi _api;
    private readonly IBiliAccountContext _account;

    public UploaderSourceProvider(IBiliUploaderCatalogApi api, IBiliAccountContext account, IContentSourceItemResolver resolver)
        : base(resolver) { _api = api; _account = account; }

    public override ContentSourceKind Kind => ContentSourceKind.Uploader;
    public override ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPaging;

    public override ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = ParsePositiveId(input, "space.bilibili.com/");
        return ValueTask.FromResult(new ContentSourceDescriptor(Kind, $"uploader:{id}", $"UP 主 {id}", null, CapabilityVersion));
    }

    public override async Task<ContentPage> GetPageAsync(ContentSourceDescriptor descriptor, ContentPageRequest request, CancellationToken cancellationToken)
    {
        ValidateDescriptor(descriptor);
        var id = ParsePositiveId(descriptor.StableSourceId, "uploader:");
        var page = await _api.GetUploaderVideosAsync(id, request.PageSize, request.ContinuationToken, _account.GetCookieHeader(), cancellationToken);
        return new(page.Items.Select(Map).ToArray(), page.NextToken, page.HasMore);
    }
}

public sealed class FavoriteSourceProvider : PersonalContentSourceProviderBase, IFavoriteSourceDiscoveryService
{
    private readonly IBiliFavoriteCatalogApi _api;
    private readonly IBiliAccountContext _account;

    public FavoriteSourceProvider(IBiliFavoriteCatalogApi api, IBiliAccountContext account, IContentSourceItemResolver resolver)
        : base(resolver) { _api = api; _account = account; }

    public override ContentSourceKind Kind => ContentSourceKind.Favorite;
    public override ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPaging;

    public override ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = ParsePositiveId(input, input.Contains("ml", StringComparison.OrdinalIgnoreCase) ? "ml" : "media_id");
        return ValueTask.FromResult(Descriptor(id, $"收藏夹 {id}"));
    }

    public async Task<IReadOnlyList<ContentSourceDescriptor>> GetMyFoldersAsync(CancellationToken cancellationToken)
    {
        if (!_account.IsLoggedIn || _account.UserId is not > 0)
            throw new ContentSourceException(ContentSourceErrorCode.LoginRequired, "查看自己的收藏夹需要登录。");
        var folders = await _api.GetFavoriteFoldersAsync(_account.UserId.Value, _account.GetCookieHeader(), cancellationToken);
        return folders.Select(folder => Descriptor(folder.MediaId, $"{folder.Title}（{folder.MediaCount}）")).ToArray();
    }

    public override async Task<ContentPage> GetPageAsync(ContentSourceDescriptor descriptor, ContentPageRequest request, CancellationToken cancellationToken)
    {
        ValidateDescriptor(descriptor);
        var id = ParsePositiveId(descriptor.StableSourceId, "favorite:");
        var page = await _api.GetFavoriteItemsAsync(id, request.PageSize, request.ContinuationToken, _account.GetCookieHeader(), cancellationToken);
        return new(page.Items.Select(Map).ToArray(), page.NextToken, page.HasMore);
    }

    private ContentSourceDescriptor Descriptor(long id, string name) =>
        new(Kind, $"favorite:{id}", name, null, CapabilityVersion);
}

public sealed class WatchLaterSourceProvider : PersonalContentSourceProviderBase
{
    private readonly IBiliWatchLaterCatalogApi _api;
    private readonly IBiliAccountContext _account;
    private readonly BoundedContentSnapshotStore _snapshots;

    public WatchLaterSourceProvider(IBiliWatchLaterCatalogApi api, IBiliAccountContext account,
        IContentSourceItemResolver resolver, BoundedContentSnapshotStore snapshots) : base(resolver)
    { _api = api; _account = account; _snapshots = snapshots; }

    public override ContentSourceKind Kind => ContentSourceKind.WatchLater;
    public override ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresLogin | ContentSourceCapabilities.SupportsPaging;

    public override ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireAccount();
        return ValueTask.FromResult(new ContentSourceDescriptor(Kind, $"watchlater:{_account.UserId}", "稍后再看", null, CapabilityVersion));
    }

    public override async Task<ContentPage> GetPageAsync(ContentSourceDescriptor descriptor, ContentPageRequest request, CancellationToken cancellationToken)
    {
        ValidateDescriptor(descriptor); RequireAccount();
        string snapshotId; int offset; IReadOnlyList<BiliCatalogItem> all;
        if (request.ContinuationToken is null)
        {
            all = await _api.GetWatchLaterAsync(_account.GetCookieHeader(), cancellationToken);
            snapshotId = _snapshots.Create(all);
            offset = 0;
        }
        else
        {
            (snapshotId, offset) = WatchLaterToken.Decode(request.ContinuationToken);
            all = _snapshots.Get(snapshotId);
        }
        var items = all.Skip(offset).Take(request.PageSize).Select(Map).ToArray();
        var nextOffset = offset + items.Length;
        var hasMore = nextOffset < all.Count;
        return new(items, hasMore ? WatchLaterToken.Encode(snapshotId, nextOffset) : null, hasMore, snapshotId);
    }

    private void RequireAccount()
    {
        if (!_account.IsLoggedIn || _account.UserId is not > 0)
            throw new ContentSourceException(ContentSourceErrorCode.LoginRequired, "稍后再看需要登录。");
    }
}

public sealed class HistorySourceProvider : PersonalContentSourceProviderBase
{
    private readonly IBiliHistoryCatalogApi _api;
    private readonly IBiliAccountContext _account;

    public HistorySourceProvider(IBiliHistoryCatalogApi api, IBiliAccountContext account, IContentSourceItemResolver resolver)
        : base(resolver) { _api = api; _account = account; }

    public override ContentSourceKind Kind => ContentSourceKind.History;
    public override ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresLogin | ContentSourceCapabilities.SupportsPaging;

    public override ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); RequireAccount();
        return ValueTask.FromResult(new ContentSourceDescriptor(Kind, $"history:{_account.UserId}", "历史记录", null, CapabilityVersion));
    }

    public override async Task<ContentPage> GetPageAsync(ContentSourceDescriptor descriptor, ContentPageRequest request, CancellationToken cancellationToken)
    {
        ValidateDescriptor(descriptor); RequireAccount();
        var page = await _api.GetHistoryAsync(request.PageSize, request.ContinuationToken, _account.GetCookieHeader(), cancellationToken);
        return new(page.Items.Select(Map).ToArray(), page.NextToken, page.HasMore);
    }

    private void RequireAccount()
    {
        if (!_account.IsLoggedIn || _account.UserId is not > 0)
            throw new ContentSourceException(ContentSourceErrorCode.LoginRequired, "历史记录需要登录。");
    }
}

/// <summary>稍后再看的有界内存快照，避免翻页期间远端列表变化导致重复或漏项。</summary>
public sealed class BoundedContentSnapshotStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeSpan _ttl;
    private readonly int _capacity;

    public BoundedContentSnapshotStore() : this(TimeSpan.FromMinutes(15), 32) { }
    internal BoundedContentSnapshotStore(TimeSpan ttl, int capacity) { _ttl = ttl; _capacity = capacity; }

    public string Create(IReadOnlyList<BiliCatalogItem> items)
    {
        var now = DateTimeOffset.UtcNow;
        Prune(now);
        while (_entries.Count >= _capacity)
        {
            var oldest = _entries.OrderBy(pair => pair.Value.CreatedAt).FirstOrDefault();
            if (oldest.Key is null) break;
            _entries.TryRemove(oldest.Key, out _);
        }
        var id = Guid.NewGuid().ToString("N");
        _entries[id] = new(items.Take(2000).ToArray(), now);
        return id;
    }

    public IReadOnlyList<BiliCatalogItem> Get(string id)
    {
        var now = DateTimeOffset.UtcNow;
        Prune(now);
        return _entries.TryGetValue(id, out var entry)
            ? entry.Items
            : throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "稍后再看分页快照已过期，请刷新来源。");
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var pair in _entries.Where(pair => now - pair.Value.CreatedAt >= _ttl))
            _entries.TryRemove(pair.Key, out _);
    }

    private sealed record Entry(IReadOnlyList<BiliCatalogItem> Items, DateTimeOffset CreatedAt);
}

internal static class WatchLaterToken
{
    public static string Encode(string snapshotId, int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"1:{snapshotId}:{offset}"));

    public static (string SnapshotId, int Offset) Decode(string token)
    {
        try
        {
            if (token.Length > 256) throw new FormatException();
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split(':');
            if (parts is not ["1", var id, var offsetText] || !Guid.TryParseExact(id, "N", out _)
                || !int.TryParse(offsetText, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 0)
                throw new FormatException();
            return (id, offset);
        }
        catch
        {
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "稍后再看分页游标无效。");
        }
    }
}
