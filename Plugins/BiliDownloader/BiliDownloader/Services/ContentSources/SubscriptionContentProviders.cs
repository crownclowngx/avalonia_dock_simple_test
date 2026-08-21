using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using System.Text.Json;

namespace BiliDownloader.Services.ContentSources;

/// <summary>
/// 番剧权限分类策略。
/// 设计意图：把远端字段组合规则固定在纯函数中，Provider 和 UI 不各自猜测权限。
/// </summary>
public static class BangumiAccessPolicy
{
    public static ContentAccessState Classify(BiliBangumiEpisode episode)
    {
        if (episode.IsDrmProtected) return ContentAccessState.DrmProtected;
        if (episode.IsRegionRestricted) return ContentAccessState.RegionRestricted;
        if (episode.IsExpired || !episode.HasStableIdentity) return ContentAccessState.Expired;
        if (episode.IsNotReleased) return ContentAccessState.NotReleased;
        return episode.IsExplicitlyAvailable ? ContentAccessState.Available : ContentAccessState.Unknown;
    }
}

/// <summary>课程权限分类策略；未知字段采用拒绝优先，避免把付费内容误判为可用。</summary>
public static class CourseAccessPolicy
{
    public static ContentAccessState Classify(BiliCourseDetail course, BiliCourseEpisode episode)
    {
        if (episode.IsDrmProtected == true) return ContentAccessState.DrmProtected;
        if (episode.IsRegionRestricted == true) return ContentAccessState.RegionRestricted;
        if (course.IsExpired || episode.Aid <= 0 || episode.Cid <= 0 || episode.EpisodeId <= 0)
            return ContentAccessState.Expired;
        if (episode.IsReleased == false ||
            episode.PublishedUnixSeconds > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return ContentAccessState.NotReleased;
        if (episode.AccessStatus == 1) return ContentAccessState.Available;
        if (episode.AccessStatus == 2 && course.IsPurchased == false)
            return ContentAccessState.PurchaseRequired;
        return ContentAccessState.Unknown;
    }
}

/// <summary>
/// 层级来源的有界内存快照。
/// 设计意图：同一次子列表翻页观察同一份顺序，同时通过短 TTL 和容量上限避免长期缓存账号目录。
/// </summary>
public sealed class HierarchicalContentSnapshotStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeSpan _ttl;
    private readonly int _capacity;

    public HierarchicalContentSnapshotStore() : this(TimeSpan.FromMinutes(15), 64) { }

    internal HierarchicalContentSnapshotStore(TimeSpan ttl, int capacity)
    {
        _ttl = ttl;
        _capacity = capacity;
    }

    public string Create(ContentItemKey parentKey, IReadOnlyList<ContentSourceItem> items)
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
        _entries[id] = new(parentKey, items.Take(2000).ToArray(), now);
        return id;
    }

    public IReadOnlyList<ContentSourceItem> Get(string id, ContentItemKey parentKey)
    {
        var now = DateTimeOffset.UtcNow;
        Prune(now);
        if (_entries.TryGetValue(id, out var entry) && entry.ParentKey == parentKey)
            return entry.Items;
        throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "层级分页快照已过期，请重新打开该集合。");
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var pair in _entries.Where(pair => now - pair.Value.CreatedAt >= _ttl))
            _entries.TryRemove(pair.Key, out _);
    }

    private sealed record Entry(
        ContentItemKey ParentKey,
        IReadOnlyList<ContentSourceItem> Items,
        DateTimeOffset CreatedAt);
}

internal static class HierarchyContinuationTokenCodec
{
    private sealed record Token(string Version, string Kind, string ParentId, string SnapshotId, int Offset);

    public static string Encode(string kind, ContentItemKey parentKey, string snapshotId, int offset)
    {
        var json = JsonSerializer.Serialize(new Token("1", kind, parentKey.NativeId, snapshotId, offset));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static (string SnapshotId, int Offset) Decode(
        string value,
        string kind,
        ContentItemKey parentKey)
    {
        try
        {
            if (value.Length > 1024) throw new FormatException();
            var encoded = value.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            var token = JsonSerializer.Deserialize<Token>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            if (token is not { Version: "1" } || token.Kind != kind || token.ParentId != parentKey.NativeId ||
                !Guid.TryParseExact(token.SnapshotId, "N", out _) || token.Offset < 0)
                throw new FormatException();
            return (token.SnapshotId, token.Offset);
        }
        catch
        {
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "层级分页游标无效或不属于当前父集合。");
        }
    }
}

/// <summary>追番和追剧的模板方法基类，仅把分类参数与来源类型留给子类。</summary>
public abstract class FollowingSourceProviderBase : IContentSourceProvider, IContentSourceResolutionProvider
{
    private readonly IBiliFollowingCatalogApi _api;
    private readonly IBiliAccountContext _account;
    private readonly HierarchicalContentSnapshotStore _snapshots;

    protected FollowingSourceProviderBase(
        IBiliFollowingCatalogApi api,
        IBiliAccountContext account,
        HierarchicalContentSnapshotStore snapshots)
    {
        _api = api;
        _account = account;
        _snapshots = snapshots;
    }

    protected abstract bool IsCinema { get; }
    protected abstract string RootName { get; }
    public abstract ContentSourceKind Kind { get; }
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresLogin |
        ContentSourceCapabilities.SupportsPaging |
        ContentSourceCapabilities.SupportsChildPaging |
        ContentSourceCapabilities.SupportsIncremental;
    public int CapabilityVersion => 1;

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireAccount();
        return ValueTask.FromResult(new ContentSourceDescriptor(
            Kind, $"{Kind.ToString().ToLowerInvariant()}:{_account.UserId}", RootName, null, CapabilityVersion));
    }

    public async Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken)
    {
        Validate(descriptor);
        ValidateParentKind(request);
        RequireAccount();
        if (!request.ParentKey.HasValue)
        {
            var page = await _api.GetFollowingAsync(
                _account.UserId!.Value,
                IsCinema,
                request.PageSize,
                request.ContinuationToken,
                _account.GetCookieHeader(),
                cancellationToken);
            var items = page.Items.Select(item => new ContentSourceItem(
                new ContentItemKey(Kind, $"season:{item.SeasonId}"),
                item.Title,
                IsCinema ? ContentSourceItemType.Cinema : ContentSourceItemType.Bangumi,
                coverSummary: item.CoverUrl,
                seasonId: item.SeasonId,
                mediaId: item.MediaId > 0 ? item.MediaId : null,
                nodeKind: ContentSourceNodeKind.Container,
                accessState: item.IsPlayable || item.IsStarted ? ContentAccessState.Available : ContentAccessState.Expired,
                childCount: item.TotalCount)).ToArray();
            return new ContentPage(items, page.NextToken, page.HasMore);
        }

        return await GetEpisodePageAsync(descriptor, request, cancellationToken);
    }

    public Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor,
        ContentSourceItem item,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(descriptor);
        if (item.Key.SourceKind != Kind || item.NodeKind != ContentSourceNodeKind.Media ||
            item.AccessState != ContentAccessState.Available ||
            item.Aid is not > 0 || item.Cid is not > 0 || item.EpId is not > 0 || item.SeasonId is not > 0)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "番剧来源项不满足解析条件。");

        var collection = new BiliVideoCollection
        {
            SeriesTitle = descriptor.DisplayName,
            Cover = item.CoverSummary ?? "",
            Items =
            [
                new BiliVideoItem
                {
                    Title = item.Title,
                    Aid = item.Aid.Value,
                    Bvid = item.Bvid ?? "",
                    Cid = item.Cid.Value,
                    Duration = item.DurationSeconds ?? 0,
                    MediaType = BiliMediaType.Bangumi,
                    EpId = item.EpId.Value,
                    SeasonId = item.SeasonId.Value,
                },
            ],
        };
        return Task.FromResult(ContentCollectionAdapter.Normalize(collection));
    }

    private async Task<ContentPage> GetEpisodePageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken)
    {
        var parent = request.ParentKey!.Value;
        var seasonId = ParseParentId(parent, "season:");
        IReadOnlyList<ContentSourceItem> all;
        string snapshotId;
        int offset;
        if (request.ContinuationToken is null)
        {
            var detail = await _api.GetSeasonAsync(seasonId, _account.GetCookieHeader(), cancellationToken);
            all = detail.Episodes.Select(ep => new ContentSourceItem(
                new ContentItemKey(Kind, $"season:{seasonId}/ep:{ep.EpisodeId}"),
                ep.Title,
                IsCinema ? ContentSourceItemType.Cinema : ContentSourceItemType.Bangumi,
                coverSummary: ep.CoverUrl ?? detail.CoverUrl,
                aid: ep.Aid > 0 ? ep.Aid : null,
                bvid: ep.Bvid,
                cid: ep.Cid > 0 ? ep.Cid : null,
                epId: ep.EpisodeId > 0 ? ep.EpisodeId : null,
                seasonId: seasonId,
                parentKey: parent,
                accessState: BangumiAccessPolicy.Classify(ep),
                durationSeconds: ep.DurationSeconds)).ToArray();
            snapshotId = _snapshots.Create(parent, all);
            offset = 0;
        }
        else
        {
            (snapshotId, offset) = HierarchyContinuationTokenCodec.Decode(
                request.ContinuationToken, Kind.ToString(), parent);
            all = _snapshots.Get(snapshotId, parent);
        }

        var items = all.Skip(offset).Take(request.PageSize).ToArray();
        var nextOffset = offset + items.Length;
        var hasMore = nextOffset < all.Count;
        var token = hasMore
            ? HierarchyContinuationTokenCodec.Encode(Kind.ToString(), parent, snapshotId, nextOffset)
            : null;
        return new ContentPage(items, token, hasMore, snapshotId);
    }

    private void Validate(ContentSourceDescriptor descriptor)
    {
        if (descriptor.Kind != Kind || descriptor.CapabilityVersion != CapabilityVersion)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "追番追剧描述符与 Provider 不一致。");
    }

    private void ValidateParentKind(ContentPageRequest request)
    {
        if (request.ParentKey.HasValue && request.ParentKey.Value.SourceKind != Kind)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "父集合与追番追剧来源类型不一致。");
    }

    private void RequireAccount()
    {
        if (!_account.IsLoggedIn || _account.UserId is not > 0)
            throw new ContentSourceException(ContentSourceErrorCode.LoginRequired, $"{RootName}需要登录。");
    }

    internal static long ParseParentId(ContentItemKey key, string prefix)
    {
        if (!key.NativeId.StartsWith(prefix, StringComparison.Ordinal) ||
            !long.TryParse(key.NativeId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "父集合稳定 ID 无效。");
        return id;
    }
}

public sealed class FollowingBangumiSourceProvider : FollowingSourceProviderBase
{
    public FollowingBangumiSourceProvider(
        IBiliFollowingCatalogApi api,
        IBiliAccountContext account,
        HierarchicalContentSnapshotStore snapshots) : base(api, account, snapshots) { }

    protected override bool IsCinema => false;
    protected override string RootName => "我的追番";
    public override ContentSourceKind Kind => ContentSourceKind.FollowingBangumi;
}

public sealed class FollowingCinemaSourceProvider : FollowingSourceProviderBase
{
    public FollowingCinemaSourceProvider(
        IBiliFollowingCatalogApi api,
        IBiliAccountContext account,
        HierarchicalContentSnapshotStore snapshots) : base(api, account, snapshots) { }

    protected override bool IsCinema => true;
    protected override string RootName => "我的追剧";
    public override ContentSourceKind Kind => ContentSourceKind.FollowingCinema;
}

/// <summary>当前账号订阅合集来源；父集合导航与普通视频解析保持分离。</summary>
public sealed class CollectionSourceProvider : IContentSourceProvider, IContentSourceResolutionProvider
{
    private readonly IBiliCollectedFolderApi _api;
    private readonly IBiliAccountContext _account;
    private readonly IContentSourceItemResolver _resolver;

    public CollectionSourceProvider(
        IBiliCollectedFolderApi api,
        IBiliAccountContext account,
        IContentSourceItemResolver resolver)
    {
        _api = api;
        _account = account;
        _resolver = resolver;
    }

    public ContentSourceKind Kind => ContentSourceKind.Collection;
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresLogin |
        ContentSourceCapabilities.SupportsPaging |
        ContentSourceCapabilities.SupportsChildPaging |
        ContentSourceCapabilities.SupportsIncremental;
    public int CapabilityVersion => 1;

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireAccount();
        return ValueTask.FromResult(new ContentSourceDescriptor(
            Kind, $"collection-library:{_account.UserId}", "我的订阅合集", null, CapabilityVersion));
    }

    public async Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken)
    {
        Validate(descriptor);
        ValidateParentKind(request);
        RequireAccount();
        if (!request.ParentKey.HasValue)
        {
            var page = await _api.GetCollectedFoldersAsync(
                _account.UserId!.Value,
                request.PageSize,
                request.ContinuationToken,
                _account.GetCookieHeader(),
                cancellationToken);
            return new ContentPage(page.Items.Select(folder => new ContentSourceItem(
                    new ContentItemKey(Kind, $"collection:{folder.MediaId}"),
                    folder.Title,
                    ContentSourceItemType.Collection,
                    author: folder.OwnerName,
                    coverSummary: folder.CoverUrl,
                    mediaId: folder.MediaId,
                    nodeKind: ContentSourceNodeKind.Container,
                    accessState: folder.IsExpired ? ContentAccessState.Expired : ContentAccessState.Available,
                    childCount: folder.MediaCount)).ToArray(),
                page.NextToken,
                page.HasMore);
        }

        var parent = request.ParentKey.Value;
        var mediaId = FollowingSourceProviderBase.ParseParentId(parent, "collection:");
        var pageItems = await _api.GetFolderItemsAsync(
            mediaId,
            request.PageSize,
            request.ContinuationToken,
            _account.GetCookieHeader(),
            cancellationToken);
        var items = pageItems.Items.Select(item => new ContentSourceItem(
            new ContentItemKey(Kind, item.Aid > 0
                ? $"collection:{mediaId}/aid:{item.Aid}"
                : $"collection:{mediaId}/bvid:{item.Bvid}"),
            item.Title,
            ContentSourceItemType.Video,
            item.Author,
            item.PublishedUnixSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(item.PublishedUnixSeconds) : null,
            item.CoverUrl,
            item.Aid > 0 ? item.Aid : null,
            item.Bvid,
            parentKey: parent,
            accessState: item.Aid > 0 || !string.IsNullOrWhiteSpace(item.Bvid)
                ? ContentAccessState.Available
                : ContentAccessState.Expired)).ToArray();
        return new ContentPage(items, pageItems.NextToken, pageItems.HasMore);
    }

    public Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor,
        ContentSourceItem item,
        CancellationToken cancellationToken)
    {
        Validate(descriptor);
        if (item.Key.SourceKind != Kind || item.NodeKind != ContentSourceNodeKind.Media ||
            item.AccessState != ContentAccessState.Available)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "订阅合集项目不满足解析条件。");
        return _resolver.ResolveAsync(item, cancellationToken);
    }

    private void Validate(ContentSourceDescriptor descriptor)
    {
        if (descriptor.Kind != Kind || descriptor.CapabilityVersion != CapabilityVersion)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "订阅合集描述符与 Provider 不一致。");
    }

    private static void ValidateParentKind(ContentPageRequest request)
    {
        if (request.ParentKey.HasValue && request.ParentKey.Value.SourceKind != ContentSourceKind.Collection)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "父集合与订阅合集来源类型不一致。");
    }

    private void RequireAccount()
    {
        if (!_account.IsLoggedIn || _account.UserId is not > 0)
            throw new ContentSourceException(ContentSourceErrorCode.LoginRequired, "订阅合集需要登录。");
    }
}

/// <summary>
/// 课程目录 Provider。
/// 设计意图：P1-G2 只公开合法可见目录，因此刻意不实现 IContentSourceResolutionProvider。
/// </summary>
public sealed class CourseSourceProvider : IContentSourceProvider
{
    private static readonly Regex CourseIdRegex = new(
        @"(?:(?<ep>ep)|(?<ss>ss))(?<id>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IBiliCourseCatalogApi _api;
    private readonly IBiliAccountContext _account;

    public CourseSourceProvider(IBiliCourseCatalogApi api, IBiliAccountContext account)
    {
        _api = api;
        _account = account;
    }

    public ContentSourceKind Kind => ContentSourceKind.Course;
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.SupportsPaging | ContentSourceCapabilities.SupportsChildPaging;
    public int CapabilityVersion => 1;

    public async ValueTask<ContentSourceDescriptor> NormalizeAsync(
        string input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(input?.Trim(), "self", StringComparison.OrdinalIgnoreCase))
        {
            RequireAccount();
            return new ContentSourceDescriptor(
                Kind, $"course-library:{_account.UserId}", "我的课程", null, CapabilityVersion);
        }

        if (string.IsNullOrWhiteSpace(input))
            throw new ContentSourceException(ContentSourceErrorCode.InvalidInput, "请输入课程 ss/ep 链接或 ID。");
        var match = CourseIdRegex.Match(input.Trim());
        long? seasonId = null;
        long? episodeId = null;
        if (match.Success && long.TryParse(match.Groups["id"].Value, out var parsed) && parsed > 0)
        {
            if (match.Groups["ep"].Success) episodeId = parsed;
            else seasonId = parsed;
        }
        else if (long.TryParse(input.Trim(), out parsed) && parsed > 0)
        {
            seasonId = parsed;
        }
        else
        {
            throw new ContentSourceException(ContentSourceErrorCode.InvalidInput, "无法识别课程 ss/ep 链接或 ID。");
        }

        var detail = await _api.GetCourseAsync(
            seasonId, episodeId, _account.GetCookieHeader(), cancellationToken);
        if (detail.SeasonId <= 0)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "课程详情缺少稳定 season_id。");
        return new ContentSourceDescriptor(
            Kind,
            $"course-direct:{detail.SeasonId}",
            detail.Title,
            new Dictionary<string, string> { ["autoOpen"] = "true" },
            CapabilityVersion);
    }

    public async Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken)
    {
        Validate(descriptor);
        ValidateParentKind(request);
        if (!request.ParentKey.HasValue)
        {
            if (descriptor.StableSourceId.StartsWith("course-direct:", StringComparison.Ordinal))
            {
                var seasonId = ParseStableId(descriptor.StableSourceId, "course-direct:");
                var directDetail = await _api.GetCourseAsync(
                    seasonId, null, _account.GetCookieHeader(), cancellationToken);
                return new ContentPage(
                    [MapCourseContainer(directDetail.SeasonId, directDetail.Title, directDetail.CoverUrl, 0, directDetail.IsExpired)],
                    null,
                    false);
            }

            RequireAccount();
            var page = await _api.GetMyCoursesAsync(
                request.PageSize,
                request.ContinuationToken,
                _account.GetCookieHeader(),
                cancellationToken);
            return new ContentPage(page.Items.Select(item =>
                    MapCourseContainer(item.SeasonId, item.Title, item.CoverUrl, item.EpisodeCount, item.IsExpired)).ToArray(),
                page.NextToken,
                page.HasMore);
        }

        var parent = request.ParentKey.Value;
        var courseId = FollowingSourceProviderBase.ParseParentId(parent, "course:");
        var detail = await _api.GetCourseAsync(courseId, null, _account.GetCookieHeader(), cancellationToken);
        var pageResult = await _api.GetCourseEpisodesAsync(
            courseId,
            request.PageSize,
            request.ContinuationToken,
            _account.GetCookieHeader(),
            cancellationToken);
        var items = pageResult.Items.Select(ep => new ContentSourceItem(
            new ContentItemKey(Kind, $"course:{courseId}/ep:{ep.EpisodeId}"),
            ep.Title,
            ContentSourceItemType.Course,
            detail.Author,
            ep.PublishedUnixSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(ep.PublishedUnixSeconds) : null,
            ep.CoverUrl ?? detail.CoverUrl,
            ep.Aid > 0 ? ep.Aid : null,
            cid: ep.Cid > 0 ? ep.Cid : null,
            epId: ep.EpisodeId > 0 ? ep.EpisodeId : null,
            seasonId: courseId,
            parentKey: parent,
            accessState: CourseAccessPolicy.Classify(detail, ep),
            durationSeconds: ep.DurationSeconds)).ToArray();
        return new ContentPage(items, pageResult.NextToken, pageResult.HasMore);
    }

    private ContentSourceItem MapCourseContainer(
        long seasonId,
        string title,
        string? cover,
        int count,
        bool expired) => new(
        new ContentItemKey(Kind, $"course:{seasonId}"),
        title,
        ContentSourceItemType.Course,
        coverSummary: cover,
        seasonId: seasonId,
        nodeKind: ContentSourceNodeKind.Container,
        accessState: expired ? ContentAccessState.Expired : ContentAccessState.Available,
        childCount: count);

    private void Validate(ContentSourceDescriptor descriptor)
    {
        if (descriptor.Kind != Kind || descriptor.CapabilityVersion != CapabilityVersion ||
            !descriptor.StableSourceId.StartsWith("course-library:", StringComparison.Ordinal) &&
            !descriptor.StableSourceId.StartsWith("course-direct:", StringComparison.Ordinal))
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "课程描述符与 Provider 不一致。");
    }

    private static void ValidateParentKind(ContentPageRequest request)
    {
        if (request.ParentKey.HasValue && request.ParentKey.Value.SourceKind != ContentSourceKind.Course)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "父集合与课程来源类型不一致。");
    }

    private void RequireAccount()
    {
        if (!_account.IsLoggedIn || _account.UserId is not > 0)
            throw new ContentSourceException(ContentSourceErrorCode.LoginRequired, "读取我的课程需要登录。");
    }

    private static long ParseStableId(string value, string prefix)
    {
        if (!long.TryParse(value[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "课程稳定 ID 无效。");
        return id;
    }
}
