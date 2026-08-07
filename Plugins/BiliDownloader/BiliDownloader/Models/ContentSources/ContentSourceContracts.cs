using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace BiliDownloader.Models.ContentSources;

/// <summary>内容源类型。每一种来源只能由一个 Provider 负责。</summary>
public enum ContentSourceKind
{
    DirectLink,
    Uploader,
    Favorite,
    WatchLater,
    History,
    FollowingBangumi,
    FollowingCinema,
    Collection,
    Course,
}

/// <summary>
/// 内容源能力声明。UI 和编排层只依据能力决定可用操作，不通过具体 Provider 类型做分支判断。
/// </summary>
[Flags]
public enum ContentSourceCapabilities
{
    None = 0,
    RequiresLogin = 1 << 0,
    SupportsPaging = 1 << 1,
    SupportsKeyword = 1 << 2,
    SupportsDateRange = 1 << 3,
    SupportsTypeFilter = 1 << 4,
    SupportsIncremental = 1 << 5,
    /// <summary>支持在父集合内继续分页读取子项目。</summary>
    SupportsChildPaging = 1 << 6,
}

/// <summary>内容源节点形态。容器只负责导航，媒体节点才可能进入解析边界。</summary>
public enum ContentSourceNodeKind
{
    Media,
    Container,
}

/// <summary>
/// 平台侧内容可访问状态。
/// 设计意图：权限事实与 UI 文案、客户端是否支持下载分离，未知状态默认拒绝解析。
/// </summary>
public enum ContentAccessState
{
    Available,
    LoginRequired,
    PurchaseRequired,
    RegionRestricted,
    Expired,
    NotReleased,
    DrmProtected,
    Unknown,
}

/// <summary>来源列表项的稳定业务类型，不与下载执行器的媒体类型耦合。</summary>
public enum ContentSourceItemType
{
    Unknown,
    Video,
    Bangumi,
    Cinema,
    Collection,
    Course,
}

/// <summary>来源排序意图。G0 只冻结数据契约，具体筛选语义由后续功能组实现。</summary>
public enum ContentSourceSortOrder
{
    ProviderDefault,
    PublishedNewest,
    PublishedOldest,
}

/// <summary>
/// 内容源描述符，只保存可公开、可长期复用的来源身份。
/// 设计意图：通过防御性复制阻止调用方在注册或分页过程中偷偷改变来源参数。
/// </summary>
public sealed class ContentSourceDescriptor
{
    [JsonConstructor]
    public ContentSourceDescriptor(
        ContentSourceKind kind,
        string stableSourceId,
        string displayName,
        IReadOnlyDictionary<string, string>? publicParameters,
        int capabilityVersion)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (string.IsNullOrWhiteSpace(stableSourceId))
            throw new ArgumentException("稳定来源 ID 不能为空。", nameof(stableSourceId));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("来源显示名称不能为空。", nameof(displayName));
        if (capabilityVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(capabilityVersion), "能力版本必须为正数。");

        Kind = kind;
        StableSourceId = stableSourceId.Trim();
        DisplayName = displayName.Trim();
        CapabilityVersion = capabilityVersion;
        PublicParameters = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(publicParameters ?? new Dictionary<string, string>(), StringComparer.Ordinal));
    }

    public ContentSourceKind Kind { get; }
    public string StableSourceId { get; }
    public string DisplayName { get; }
    public IReadOnlyDictionary<string, string> PublicParameters { get; }
    public int CapabilityVersion { get; }
}

/// <summary>
/// 来源内项目的稳定键。NativeId 必须由 Provider 先规范化；键本身只去除外围空白，
/// 避免对 BVID 等大小写敏感的原生标识做破坏性转换。
/// </summary>
public readonly record struct ContentItemKey
{
    [JsonConstructor]
    public ContentItemKey(ContentSourceKind sourceKind, string nativeId)
    {
        if (!Enum.IsDefined(sourceKind))
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        if (string.IsNullOrWhiteSpace(nativeId))
            throw new ArgumentException("平台原生项目 ID 不能为空。", nameof(nativeId));

        SourceKind = sourceKind;
        NativeId = nativeId.Trim();
    }

    public ContentSourceKind SourceKind { get; }
    public string NativeId { get; }
}

/// <summary>解析后的媒体单元身份。跨来源聚合只比较 Aid 与 Cid。</summary>
public readonly record struct MediaUnitKey
{
    [JsonConstructor]
    public MediaUnitKey(long aid, long cid)
    {
        if (aid <= 0)
            throw new ArgumentOutOfRangeException(nameof(aid), "Aid 必须为正数。");
        if (cid <= 0)
            throw new ArgumentOutOfRangeException(nameof(cid), "Cid 必须为正数。");

        Aid = aid;
        Cid = cid;
    }

    public long Aid { get; }
    public long Cid { get; }
}

/// <summary>内容源筛选规则值对象。G0 不执行筛选，只保证分页接口后续无需破坏性变更。</summary>
public sealed class SourceFilterRules
{
    public static SourceFilterRules Empty { get; } = new();

    [JsonConstructor]
    public SourceFilterRules(
        string? keyword = null,
        DateTimeOffset? publishedFrom = null,
        DateTimeOffset? publishedTo = null,
        IReadOnlyList<ContentSourceItemType>? mediaTypes = null,
        ContentSourceSortOrder sortOrder = ContentSourceSortOrder.ProviderDefault)
    {
        if (publishedFrom.HasValue && publishedTo.HasValue && publishedFrom > publishedTo)
            throw new ArgumentException("发布时间起点不能晚于终点。", nameof(publishedFrom));
        if (!Enum.IsDefined(sortOrder))
            throw new ArgumentOutOfRangeException(nameof(sortOrder));

        var mediaTypeArray = (mediaTypes ?? Array.Empty<ContentSourceItemType>()).ToArray();
        if (mediaTypeArray.Any(type => !Enum.IsDefined(type)))
            throw new ArgumentOutOfRangeException(nameof(mediaTypes), "媒体类型包含未知枚举值。");

        Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        PublishedFrom = publishedFrom;
        PublishedTo = publishedTo;
        MediaTypes = Array.AsReadOnly(mediaTypeArray);
        SortOrder = sortOrder;
    }

    public string? Keyword { get; }
    public DateTimeOffset? PublishedFrom { get; }
    public DateTimeOffset? PublishedTo { get; }
    public IReadOnlyList<ContentSourceItemType> MediaTypes { get; }
    public ContentSourceSortOrder SortOrder { get; }
}

/// <summary>单页请求。ContinuationToken 是 Provider 私有游标，调用方不得解释或改写。</summary>
public sealed class ContentPageRequest
{
    public const int DefaultPageSize = 20;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 100;

    [JsonConstructor]
    public ContentPageRequest(
        int pageSize = DefaultPageSize,
        string? continuationToken = null,
        SourceFilterRules? filters = null,
        ContentItemKey? parentKey = null)
    {
        if (pageSize is < MinPageSize or > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"页大小必须在 {MinPageSize}～{MaxPageSize} 之间。");

        PageSize = pageSize;
        ContinuationToken = continuationToken;
        Filters = filters ?? SourceFilterRules.Empty;
        ParentKey = parentKey;
    }

    public int PageSize { get; }
    public string? ContinuationToken { get; }
    public SourceFilterRules Filters { get; }
    /// <summary>为空时读取根列表；非空时读取指定父集合的子页。</summary>
    public ContentItemKey? ParentKey { get; }
}

/// <summary>
/// 内容源中的一个稳定项目。只允许公开元数据和平台稳定引用，禁止携带 Cookie、请求头或签名地址。
/// </summary>
public sealed class ContentSourceItem
{
    [JsonConstructor]
    public ContentSourceItem(
        ContentItemKey key,
        string title,
        ContentSourceItemType itemType,
        string? author = null,
        DateTimeOffset? publishedAt = null,
        string? coverSummary = null,
        long? aid = null,
        string? bvid = null,
        long? cid = null,
        long? epId = null,
        long? seasonId = null,
        long? mediaId = null,
        ContentItemKey? parentKey = null,
        ContentSourceNodeKind nodeKind = ContentSourceNodeKind.Media,
        ContentAccessState accessState = ContentAccessState.Available,
        int? childCount = null,
        int? durationSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("内容标题不能为空。", nameof(title));
        if (!Enum.IsDefined(itemType))
            throw new ArgumentOutOfRangeException(nameof(itemType));
        if (!Enum.IsDefined(nodeKind))
            throw new ArgumentOutOfRangeException(nameof(nodeKind));
        if (!Enum.IsDefined(accessState))
            throw new ArgumentOutOfRangeException(nameof(accessState));
        if (parentKey.HasValue && parentKey.Value.SourceKind != key.SourceKind)
            throw new ArgumentException("父项目与子项目必须属于同一种内容源。", nameof(parentKey));
        if (childCount is < 0)
            throw new ArgumentOutOfRangeException(nameof(childCount));
        if (durationSeconds is < 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));

        Key = key;
        Title = title.Trim();
        ItemType = itemType;
        Author = author?.Trim();
        PublishedAt = publishedAt;
        CoverSummary = coverSummary?.Trim();
        Aid = aid;
        Bvid = bvid?.Trim();
        Cid = cid;
        EpId = epId;
        SeasonId = seasonId;
        MediaId = mediaId;
        ParentKey = parentKey;
        NodeKind = nodeKind;
        AccessState = accessState;
        ChildCount = childCount;
        DurationSeconds = durationSeconds;
    }

    public ContentItemKey Key { get; }
    public string Title { get; }
    public ContentSourceItemType ItemType { get; }
    public string? Author { get; }
    public DateTimeOffset? PublishedAt { get; }
    public string? CoverSummary { get; }
    public long? Aid { get; }
    public string? Bvid { get; }
    public long? Cid { get; }
    public long? EpId { get; }
    public long? SeasonId { get; }
    public long? MediaId { get; }
    public ContentItemKey? ParentKey { get; }
    public ContentSourceNodeKind NodeKind { get; }
    public ContentAccessState AccessState { get; }
    public int? ChildCount { get; }
    public int? DurationSeconds { get; }
}

/// <summary>不可变分页结果，同时保证 HasMore 与下一游标的状态不会互相矛盾。</summary>
public sealed class ContentPage
{
    [JsonConstructor]
    public ContentPage(
        IReadOnlyList<ContentSourceItem>? items,
        string? nextContinuationToken,
        bool hasMore,
        string? snapshotToken = null)
    {
        if (hasMore && string.IsNullOrEmpty(nextContinuationToken))
            throw new ArgumentException("仍有下一页时必须提供 continuation token。", nameof(nextContinuationToken));
        if (!hasMore && nextContinuationToken is not null)
            throw new ArgumentException("末页不得提供 continuation token。", nameof(nextContinuationToken));

        Items = Array.AsReadOnly((items ?? Array.Empty<ContentSourceItem>()).ToArray());
        NextContinuationToken = nextContinuationToken;
        HasMore = hasMore;
        SnapshotToken = snapshotToken;
    }

    public IReadOnlyList<ContentSourceItem> Items { get; }
    public string? NextContinuationToken { get; }
    public bool HasMore { get; }
    public string? SnapshotToken { get; }
}
