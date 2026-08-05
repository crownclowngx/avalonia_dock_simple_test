using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;

namespace BiliDownloader.Services.ContentSources;

/// <summary>
/// 直接链接内容源策略。
/// 设计意图：把链接识别、短链展开和普通视频/番剧路由从 ViewModel 收敛到单一来源边界，
/// 同时继续复用 <see cref="BiliApiService"/> 的 HTTP 与 JSON 映射能力。
/// </summary>
public sealed class DirectLinkProvider : IContentSourceProvider
{
    private const string VideoBvPrefix = "video:bv:";
    private const string VideoAvPrefix = "video:av:";
    private const string BangumiEpPrefix = "bangumi:ep:";
    private const string BangumiSsPrefix = "bangumi:ss:";
    private const string BangumiMdPrefix = "bangumi:md:";

    private readonly IBiliContentSourceApi _api;
    private readonly IBiliCredentialProvider _credentials;

    public DirectLinkProvider(IBiliContentSourceApi api, IBiliCredentialProvider credentials)
    {
        _api = api;
        _credentials = credentials;
    }

    public ContentSourceKind Kind => ContentSourceKind.DirectLink;
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresLogin;
    public int CapabilityVersion => 1;

    public async ValueTask<ContentSourceDescriptor> NormalizeAsync(
        string input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(input))
            throw InvalidInput();

        var resolved = input.Trim();
        if (BiliApiService.IsB23TvLink(resolved))
        {
            try
            {
                resolved = await _api.ResolveShortLinkAsync(resolved, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                throw new ContentSourceException(ContentSourceErrorCode.RemoteFailure, "短链解析失败，请稍后重试。");
            }
        }

        var stableId = NormalizeStableId(resolved);
        return new ContentSourceDescriptor(
            Kind,
            stableId,
            ToDisplayName(stableId),
            publicParameters: null,
            CapabilityVersion);
    }

    public Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDescriptor(descriptor);
        ArgumentNullException.ThrowIfNull(request);

        if (request.ContinuationToken is not null)
            throw new ContentSourceException(
                ContentSourceErrorCode.ProtocolViolation,
                "直接链接来源不支持 continuation token。");

        var item = CreateRootItem(descriptor);
        return Task.FromResult(new ContentPage([item], null, hasMore: false));
    }

    public async Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor,
        ContentSourceItem item,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDescriptor(descriptor);
        ArgumentNullException.ThrowIfNull(item);
        if (item.Key != new ContentItemKey(Kind, descriptor.StableSourceId))
            throw Protocol("来源项与描述符身份不一致。");

        var cookie = _credentials.GetCookieHeader();
        if (string.IsNullOrWhiteSpace(cookie))
            throw new ContentSourceException(ContentSourceErrorCode.LoginRequired, "请先登录后再解析。");

        try
        {
            BiliVideoCollection collection;
            if (descriptor.StableSourceId.StartsWith(VideoBvPrefix, StringComparison.Ordinal))
            {
                var bvid = "BV" + descriptor.StableSourceId[VideoBvPrefix.Length..];
                collection = await _api.GetVideoCollectionAsync(bvid, true, cookie, cancellationToken);
            }
            else if (descriptor.StableSourceId.StartsWith(VideoAvPrefix, StringComparison.Ordinal))
            {
                var avid = "av" + descriptor.StableSourceId[VideoAvPrefix.Length..];
                collection = await _api.GetVideoCollectionAsync(avid, false, cookie, cancellationToken);
            }
            else
            {
                var (id, isSeasonId) = ToBangumiApiId(descriptor.StableSourceId);
                collection = await _api.GetBangumiCollectionAsync(id, isSeasonId, cookie, cancellationToken);
            }

            return AdaptCollection(collection);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ContentSourceException)
        {
            throw;
        }
        catch
        {
            // 不保留远端异常文本或 InnerException，避免签名 URL 经上层日志泄漏。
            throw new ContentSourceException(ContentSourceErrorCode.RemoteFailure, "获取内容信息失败，请稍后重试。");
        }
    }

    private static string NormalizeStableId(string input)
    {
        var video = BiliApiService.ParseVideoId(input);
        if (video is not null)
        {
            if (video.Value.IsBvid)
            {
                var payload = video.Value.Id[2..];
                if (string.IsNullOrWhiteSpace(payload))
                    throw InvalidInput();
                return VideoBvPrefix + payload;
            }

            return VideoAvPrefix + NormalizePositiveNumber(video.Value.Id[2..]);
        }

        var bangumi = BiliApiService.ParseBangumiId(input);
        if (bangumi is null)
            throw InvalidInput();

        var prefix = bangumi.Value.Id[..2].ToLowerInvariant();
        var number = NormalizePositiveNumber(bangumi.Value.Id[2..]);
        return prefix switch
        {
            "ep" => BangumiEpPrefix + number,
            "ss" => BangumiSsPrefix + number,
            "md" => BangumiMdPrefix + number,
            _ => throw InvalidInput(),
        };
    }

    private static string NormalizePositiveNumber(string value)
    {
        if (!long.TryParse(value, out var number) || number <= 0)
            throw InvalidInput();
        return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ToDisplayName(string stableId)
    {
        if (stableId.StartsWith(VideoBvPrefix, StringComparison.Ordinal))
            return "BV" + stableId[VideoBvPrefix.Length..];
        if (stableId.StartsWith(VideoAvPrefix, StringComparison.Ordinal))
            return "av" + stableId[VideoAvPrefix.Length..];
        if (stableId.StartsWith(BangumiEpPrefix, StringComparison.Ordinal))
            return "ep" + stableId[BangumiEpPrefix.Length..];
        if (stableId.StartsWith(BangumiSsPrefix, StringComparison.Ordinal))
            return "ss" + stableId[BangumiSsPrefix.Length..];
        if (stableId.StartsWith(BangumiMdPrefix, StringComparison.Ordinal))
            return "md" + stableId[BangumiMdPrefix.Length..];
        throw InvalidInput();
    }

    private static ContentSourceItem CreateRootItem(ContentSourceDescriptor descriptor)
    {
        var stableId = descriptor.StableSourceId;
        var itemType = stableId.StartsWith("video:", StringComparison.Ordinal)
            ? ContentSourceItemType.Video
            : ContentSourceItemType.Bangumi;

        long? aid = null;
        string? bvid = null;
        long? epId = null;
        long? seasonId = null;
        long? mediaId = null;

        if (stableId.StartsWith(VideoBvPrefix, StringComparison.Ordinal))
            bvid = "BV" + stableId[VideoBvPrefix.Length..];
        else if (stableId.StartsWith(VideoAvPrefix, StringComparison.Ordinal))
            aid = long.Parse(stableId[VideoAvPrefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
        else if (stableId.StartsWith(BangumiEpPrefix, StringComparison.Ordinal))
            epId = long.Parse(stableId[BangumiEpPrefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
        else if (stableId.StartsWith(BangumiSsPrefix, StringComparison.Ordinal))
            seasonId = long.Parse(stableId[BangumiSsPrefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
        else if (stableId.StartsWith(BangumiMdPrefix, StringComparison.Ordinal))
            mediaId = long.Parse(stableId[BangumiMdPrefix.Length..], System.Globalization.CultureInfo.InvariantCulture);
        else
            throw InvalidInput();

        return new ContentSourceItem(
            new ContentItemKey(ContentSourceKind.DirectLink, stableId),
            descriptor.DisplayName,
            itemType,
            aid: aid,
            bvid: bvid,
            epId: epId,
            seasonId: seasonId,
            mediaId: mediaId);
    }

    private static (string Id, bool IsSeasonId) ToBangumiApiId(string stableId)
    {
        if (stableId.StartsWith(BangumiEpPrefix, StringComparison.Ordinal))
            return ("ep" + stableId[BangumiEpPrefix.Length..], false);
        if (stableId.StartsWith(BangumiSsPrefix, StringComparison.Ordinal))
            return ("ss" + stableId[BangumiSsPrefix.Length..], true);
        if (stableId.StartsWith(BangumiMdPrefix, StringComparison.Ordinal))
            return ("md" + stableId[BangumiMdPrefix.Length..], false);
        throw InvalidInput();
    }

    private static BiliVideoCollection AdaptCollection(BiliVideoCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var index = 1;
        foreach (var video in collection.Items)
        {
            if (video.Aid <= 0 || video.Cid <= 0)
                throw Protocol("内容源返回了无效的 Aid/Cid。");

            video.Index = index++;
            video.OriginalTitle = video.Title;
            video.CoverUrl = collection.Cover;
            video.MediaUnitKey = new MediaUnitKey(video.Aid, video.Cid);
        }

        return collection;
    }

    private void ValidateDescriptor(ContentSourceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Kind != Kind || descriptor.CapabilityVersion != CapabilityVersion)
            throw Protocol("来源描述符与 Provider 契约不一致。");
        _ = ToDisplayName(descriptor.StableSourceId);
    }

    private static ContentSourceException InvalidInput() =>
        new(ContentSourceErrorCode.InvalidInput, "无法识别该 B 站链接或 ID。");

    private static ContentSourceException Protocol(string message) =>
        new(ContentSourceErrorCode.ProtocolViolation, message);
}
