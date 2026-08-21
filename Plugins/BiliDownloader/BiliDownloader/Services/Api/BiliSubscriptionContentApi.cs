using System.Text.Json;
using System.Text.Json.Nodes;
using BiliDownloader.Constants;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.Services.Infrastructure;
using Flurl;
using Flurl.Http;

namespace BiliDownloader.Services.Api;

/// <summary>
/// P1-G2 订阅、合集与课程的 HTTP 适配器。
/// 设计意图：对外暴露三个窄端口，内部统一错误分类和安全分页，避免 Provider 感知 JSON 与 URL。
/// </summary>
public sealed class BiliSubscriptionContentApi :
    IBiliFollowingCatalogApi,
    IBiliCollectedFolderApi,
    IBiliCourseCatalogApi
{
    private enum Operation
    {
        Following,
        BangumiSeason,
        CollectedFolders,
        CollectedItems,
        MyCourses,
        CourseDetail,
        CourseEpisodes,
    }

    private static readonly IPluginLogger Log = PluginLog.For<BiliSubscriptionContentApi>();
    private readonly IBiliFavoriteCatalogApi _favorites;

    public BiliSubscriptionContentApi(IBiliFavoriteCatalogApi favorites) => _favorites = favorites;

    public async Task<BiliFollowingPage> GetFollowingAsync(
        long userId,
        bool cinema,
        int pageSize,
        string? continuationToken,
        string cookie,
        CancellationToken cancellationToken)
    {
        var kind = cinema ? "following-cinema" : "following-bangumi";
        var page = ContinuationTokenCodec.DecodePage(continuationToken, kind);
        var url = "https://api.bilibili.com/x/space/bangumi/follow/list"
            .SetQueryParam("vmid", userId)
            .SetQueryParam("type", cinema ? 2 : 1)
            .SetQueryParam("pn", page)
            .SetQueryParam("ps", pageSize);
        var root = await GetRootAsync(url.ToString(), cookie, Operation.Following, cancellationToken);
        var data = root["data"] ?? new JsonObject();
        var items = (data["list"] as JsonArray ?? new JsonArray()).OfType<JsonNode>()
            .Select(item => new BiliFollowingSeason(
                item["season_id"]?.Value<long>() ?? 0,
                item["media_id"]?.Value<long>() ?? 0,
                item["title"]?.Value<string>() ?? "未命名剧集",
                item["cover"]?.Value<string>(),
                Math.Max(0, item["total_count"]?.Value<int>() ?? 0),
                item["is_started"]?.Value<int>() != 0,
                item["is_play"]?.Value<int>() != 0))
            .Where(item => item.SeasonId > 0)
            .ToArray();
        var total = data["total"]?.Value<int>() ?? items.Length;
        var hasMore = items.Length > 0 && checked(page * pageSize) < total;
        return new(items, hasMore ? ContinuationTokenCodec.EncodePage(kind, page + 1) : null, hasMore);
    }

    public async Task<BiliBangumiSeasonDetail> GetSeasonAsync(
        long seasonId,
        string cookie,
        CancellationToken cancellationToken)
    {
        var url = "https://api.bilibili.com/pgc/view/web/season"
            .SetQueryParam("season_id", seasonId);
        var root = await GetRootAsync(url.ToString(), cookie, Operation.BangumiSeason, cancellationToken);
        var data = root["result"] ?? root["data"] ?? new JsonObject();
        var actualSeasonId = data["season_id"]?.Value<long>() ?? seasonId;
        var episodes = EnumerateBangumiEpisodes(data)
            .Select(ep => MapBangumiEpisode(ep, actualSeasonId))
            .ToArray();
        return new(
            actualSeasonId,
            data["season_title"]?.Value<string>() ?? data["title"]?.Value<string>() ?? $"剧集 {actualSeasonId}",
            data["cover"]?.Value<string>(),
            episodes);
    }

    public async Task<BiliCollectedFolderPage> GetCollectedFoldersAsync(
        long userId,
        int pageSize,
        string? continuationToken,
        string cookie,
        CancellationToken cancellationToken)
    {
        var page = ContinuationTokenCodec.DecodePage(continuationToken, "collected-folders");
        var url = "https://api.bilibili.com/x/v3/fav/folder/collected/list"
            .SetQueryParam("up_mid", userId)
            .SetQueryParam("platform", "web")
            .SetQueryParam("pn", page)
            .SetQueryParam("ps", pageSize);
        var root = await GetRootAsync(url.ToString(), cookie, Operation.CollectedFolders, cancellationToken);
        var data = root["data"] ?? new JsonObject();
        var items = (data["list"] as JsonArray ?? new JsonArray()).OfType<JsonNode>()
            .Select(item => new BiliCollectedFolder(
                item["id"]?.Value<long>() ?? 0,
                item["title"]?.Value<string>() ?? "未命名合集",
                item["upper"]?["name"]?.Value<string>(),
                item["cover"]?.Value<string>(),
                Math.Max(0, item["media_count"]?.Value<int>() ?? 0),
                item["state"]?.Value<int>() is < 0))
            .Where(item => item.MediaId > 0)
            .ToArray();
        var total = data["count"]?.Value<int>() ?? items.Length;
        var hasMore = items.Length > 0 && checked(page * pageSize) < total;
        return new(items, hasMore ? ContinuationTokenCodec.EncodePage("collected-folders", page + 1) : null, hasMore);
    }

    public async Task<BiliCatalogPage> GetFolderItemsAsync(
        long mediaId,
        int pageSize,
        string? continuationToken,
        string cookie,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _favorites.GetFavoriteItemsAsync(
                mediaId, pageSize, continuationToken, cookie, cancellationToken);
        }
        catch (ContentSourceException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw CreateError(Operation.CollectedItems, -1, ContentSourceErrorCode.RemoteFailure, "Bilibili 暂时无法返回合集内容。");
        }
    }

    public async Task<BiliCoursePage> GetMyCoursesAsync(
        int pageSize,
        string? continuationToken,
        string cookie,
        CancellationToken cancellationToken)
    {
        var page = ContinuationTokenCodec.DecodePage(continuationToken, "my-courses");
        var url = "https://api.bilibili.com/pugv/pay/web/my/paid"
            .SetQueryParam("pn", page)
            .SetQueryParam("ps", pageSize);
        var root = await GetRootAsync(url.ToString(), cookie, Operation.MyCourses, cancellationToken);
        var data = root["data"] ?? new JsonObject();
        var list = data["data"] as JsonArray ?? data["list"] as JsonArray
            ?? data["items"] as JsonArray ?? new JsonArray();
        var items = list.OfType<JsonNode>().Select(item => new BiliCourseSummary(
                item["season_id"]?.Value<long>() ?? item["id"]?.Value<long>() ?? 0,
                item["title"]?.Value<string>() ?? "未命名课程",
                item["cover"]?.Value<string>(),
                Math.Max(0, item["ep_count"]?.Value<int>() ?? item["episode_count"]?.Value<int>() ?? 0),
                item["is_expired"]?.Value<bool>() == true || item["status"]?.Value<int>() is < 0))
            .Where(item => item.SeasonId > 0)
            .ToArray();
        // 已购课程接口的 total 表示总页数；空页必须终止，避免远端异常造成无限翻页。
        var totalPages = data["total"]?.Value<int>() ?? page;
        var hasMore = items.Length > 0 && page < totalPages;
        return new(items, hasMore ? ContinuationTokenCodec.EncodePage("my-courses", page + 1) : null, hasMore);
    }

    public async Task<BiliCourseDetail> GetCourseAsync(
        long? seasonId,
        long? episodeId,
        string cookie,
        CancellationToken cancellationToken)
    {
        if (seasonId is not > 0 && episodeId is not > 0)
            throw new ContentSourceException(ContentSourceErrorCode.InvalidInput, "课程 ss/ep 标识无效。");
        var url = new Url("https://api.bilibili.com/pugv/view/web/season");
        url = seasonId is > 0
            ? url.SetQueryParam("season_id", seasonId.Value)
            : url.SetQueryParam("ep_id", episodeId!.Value);
        var root = await GetRootAsync(url.ToString(), cookie, Operation.CourseDetail, cancellationToken);
        var data = root["data"] ?? new JsonObject();
        var purchasedToken = data["user_status"]?["payed"];
        bool? purchased = purchasedToken?.GetValueKind() switch
        {
            JsonValueKind.True or JsonValueKind.False => purchasedToken.Value<bool>(),
            JsonValueKind.Number => purchasedToken.Value<int>() != 0,
            _ => null,
        };
        return new(
            data["season_id"]?.Value<long>() ?? seasonId ?? 0,
            data["title"]?.Value<string>() ?? "未命名课程",
            data["cover"]?.Value<string>(),
            data["up_info"]?["uname"]?.Value<string>(),
            purchased,
            data["is_expired"]?.Value<bool>() == true || data["expire_status"]?.Value<int>() is > 0);
    }

    public async Task<BiliCourseEpisodePage> GetCourseEpisodesAsync(
        long seasonId,
        int pageSize,
        string? continuationToken,
        string cookie,
        CancellationToken cancellationToken)
    {
        var tokenKind = $"course-episodes:{seasonId}";
        var page = ContinuationTokenCodec.DecodePage(continuationToken, tokenKind);
        var url = "https://api.bilibili.com/pugv/view/web/ep/list"
            .SetQueryParam("season_id", seasonId)
            .SetQueryParam("pn", page)
            .SetQueryParam("ps", pageSize);
        var root = await GetRootAsync(url.ToString(), cookie, Operation.CourseEpisodes, cancellationToken);
        var data = root["data"] ?? new JsonObject();
        var list = data["items"] as JsonArray ?? new JsonArray();
        var items = list.OfType<JsonNode>().Select(item => new BiliCourseEpisode(
                item["aid"]?.Value<long>() ?? 0,
                item["cid"]?.Value<long>() ?? 0,
                item["id"]?.Value<long>() ?? item["ep_id"]?.Value<long>() ?? 0,
                item["title"]?.Value<string>() ?? "未命名课时",
                Math.Max(0, item["duration"]?.Value<int>() ?? 0),
                item["release_date"]?.Value<long>() ?? 0,
                item["cover"]?.Value<string>(),
                item["status"]?.Value<int?>(),
                ReadNullableBoolean(item["is_drm"]),
                ReadNullableBoolean(item["area_limit"]),
                ReadNullableBoolean(item["is_release"])))
            .Where(item => item.EpisodeId > 0)
            .ToArray();
        var pageInfo = data["page"];
        var total = pageInfo?["total"]?.Value<int>() ?? items.Length;
        var hasMore = items.Length > 0 && checked(page * pageSize) < total;
        return new(items, hasMore ? ContinuationTokenCodec.EncodePage(tokenKind, page + 1) : null, hasMore);
    }

    private static IEnumerable<JsonNode> EnumerateBangumiEpisodes(JsonNode data)
    {
        foreach (var episode in data["episodes"] as JsonArray ?? new JsonArray())
            if (episode is not null) yield return episode;
        foreach (var section in data["section"] as JsonArray ?? new JsonArray())
        foreach (var episode in section?["episodes"] as JsonArray ?? new JsonArray())
            if (episode is not null) yield return episode;
    }

    private static BiliBangumiEpisode MapBangumiEpisode(JsonNode ep, long seasonId)
    {
        var aid = ep["aid"]?.Value<long>() ?? 0;
        var cid = ep["cid"]?.Value<long>() ?? 0;
        var episodeId = ep["ep_id"]?.Value<long>() ?? ep["id"]?.Value<long>() ?? 0;
        var rights = ep["rights"];
        var publishAt = ep["pub_time"]?.Value<long>() ?? 0;
        var status = ep["status"]?.Value<int?>();
        var notReleased = publishAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() || ep["ep_status"]?.Value<int>() == -1;
        var regionRestricted = rights?["area_limit"]?.Value<int>() == 1 || ep["area_limit"]?.Value<int>() == 1;
        var drm = rights?["is_drm"]?.Value<int>() == 1 || ep["is_drm"]?.Value<int>() == 1;
        var expired = ep["is_deleted"]?.Value<int>() == 1 || ep["invalid"]?.Value<int>() == 1;
        var available = rights?["allow_demand"]?.Value<int>() == 1 || status == 2 || ep["is_play"]?.Value<int>() == 1;
        return new(
            aid,
            ep["bvid"]?.Value<string>() ?? "",
            cid,
            episodeId,
            seasonId,
            ep["long_title"]?.Value<string>() ?? ep["title"]?.Value<string>() ?? "未命名分集",
            Math.Max(0, (int)((ep["duration"]?.Value<long>() ?? 0) / 1000)),
            ep["cover"]?.Value<string>(),
            aid > 0 && cid > 0 && episodeId > 0,
            drm,
            regionRestricted,
            expired,
            notReleased,
            available);
    }

    private static bool? ReadNullableBoolean(JsonNode? token) => token?.GetValueKind() switch
    {
        JsonValueKind.True or JsonValueKind.False => token.Value<bool>(),
        JsonValueKind.Number => token.Value<int>() != 0,
        _ => null,
    };

    private static async Task<JsonObject> GetRootAsync(
        string url,
        string cookie,
        Operation operation,
        CancellationToken cancellationToken)
    {
        JsonObject root;
        try
        {
            var request = url.WithHeader("User-Agent", HttpConstants.UserAgent)
                .WithHeader("Referer", HttpConstants.Referer);
            if (!string.IsNullOrWhiteSpace(cookie))
                request = request.WithHeader("Cookie", cookie);
            root = JsonNodeReader.ParseObject(await request.GetStringAsync(cancellationToken: cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FlurlHttpException ex) when (ex.Call.Response?.StatusCode == 401)
        {
            throw CreateError(operation, 401, ContentSourceErrorCode.LoginRequired, "此来源需要登录。");
        }
        catch (FlurlHttpException ex) when (ex.Call.Response?.StatusCode == 412)
        {
            throw CreateError(operation, 412, ContentSourceErrorCode.RiskControlled, "触发 Bilibili 安全风控，请稍后手动重试。");
        }
        catch (FlurlHttpException ex) when (ex.Call.Response?.StatusCode == 403)
        {
            throw CreateError(operation, 403, ContentSourceErrorCode.Forbidden, "当前账号无权访问此来源。");
        }
        catch (FlurlHttpException ex) when (ex.Call.Response?.StatusCode == 404)
        {
            throw CreateError(operation, 404, ContentSourceErrorCode.NotFound, "来源不存在或已失效。");
        }
        catch (FlurlHttpException ex) when (ex.Call.Response?.StatusCode == 429)
        {
            throw CreateError(operation, 429, ContentSourceErrorCode.RateLimited, "请求过于频繁，请稍后手动重试。");
        }
        catch (ContentSourceException)
        {
            throw;
        }
        catch
        {
            throw CreateError(operation, -1, ContentSourceErrorCode.RemoteFailure, "Bilibili 暂时无法返回此来源。");
        }

        var code = root["code"]?.Value<int>() ?? int.MinValue;
        if (code == 0)
            return root;
        throw code switch
        {
            -101 => CreateError(operation, code, ContentSourceErrorCode.LoginRequired, "此来源需要登录。"),
            -403 or 53013 => CreateError(operation, code, ContentSourceErrorCode.Forbidden, "当前账号无权访问此来源。"),
            -404 or 11010 => CreateError(operation, code, ContentSourceErrorCode.NotFound, "来源不存在或已失效。"),
            -401 or -352 or -412 => CreateError(operation, code, ContentSourceErrorCode.RiskControlled, "触发 Bilibili 安全风控，请稍后手动重试。"),
            _ => CreateError(operation, code, ContentSourceErrorCode.RemoteFailure, "Bilibili 暂时无法返回此来源。"),
        };
    }

    private static ContentSourceException CreateError(
        Operation operation,
        int remoteCode,
        ContentSourceErrorCode classification,
        string message)
    {
        // 只记录稳定操作、远端码和分类；URL、Cookie、响应正文及课程订单信息均不进入日志。
        Log.Warn($"P1-G2 来源请求失败：operation={operation}, remoteCode={remoteCode}, classification={classification}");
        return new ContentSourceException(classification, message);
    }
}
