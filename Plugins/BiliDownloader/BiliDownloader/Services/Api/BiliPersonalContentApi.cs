using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BiliDownloader.Constants;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.Services.Infrastructure;
using Flurl;
using Flurl.Http;

namespace BiliDownloader.Services.Api;

/// <summary>
/// Bilibili 个人高频来源的 HTTP 适配器。
/// 设计意图：对外拆成四个窄接口，内部集中处理相同的响应码与安全游标，避免 Provider 感知 JSON。
/// </summary>
public sealed class BiliPersonalContentApi :
    IBiliUploaderCatalogApi,
    IBiliFavoriteCatalogApi,
    IBiliWatchLaterCatalogApi,
    IBiliHistoryCatalogApi
{
    private enum Operation { Uploader, FavoriteFolders, FavoriteItems, WatchLater, History }
    private static readonly IPluginLogger Log = PluginLog.For<BiliPersonalContentApi>();
    private readonly BiliApiService _signer;
    private readonly BiliUploaderRequestContextFactory _uploaderContextFactory;

    public BiliPersonalContentApi(BiliApiService signer)
        : this(signer, new BiliUploaderRequestContextFactory()) { }

    internal BiliPersonalContentApi(
        BiliApiService signer,
        BiliUploaderRequestContextFactory uploaderContextFactory)
    {
        _signer = signer;
        _uploaderContextFactory = uploaderContextFactory;
    }

    public async Task<BiliCatalogPage> GetUploaderVideosAsync(
        long uploaderId, int pageSize, string? continuationToken, string cookie,
        CancellationToken cancellationToken)
    {
        var page = ContinuationTokenCodec.DecodePage(continuationToken, "uploader");
        var context = _uploaderContextFactory.Create(uploaderId, page, pageSize);
        var signed = await _signer.WbiSignAsync(context.Query, cookie, cancellationToken);
        var root = await GetRootAsync(
            $"https://api.bilibili.com/x/space/wbi/arc/search?{signed}",
            cookie, Operation.Uploader, context.Referer, cancellationToken);
        var data = root["data"]!;
        var items = MapVideoArray(data["list"]?["vlist"] as JsonArray, "author", "created", "pic");
        var total = data["page"]?["count"]?.Value<int>() ?? items.Count;
        var hasMore = checked(page * pageSize) < total;
        return new(items, hasMore ? ContinuationTokenCodec.EncodePage("uploader", page + 1) : null, hasMore);
    }

    public async Task<IReadOnlyList<BiliFavoriteFolder>> GetFavoriteFoldersAsync(
        long userId, string cookie, CancellationToken cancellationToken)
    {
        var root = await GetRootAsync(
            $"https://api.bilibili.com/x/v3/fav/folder/created/list-all?up_mid={userId}",
            cookie, Operation.FavoriteFolders, null, cancellationToken);
        return (root["data"]?["list"] as JsonArray ?? new JsonArray()).OfType<JsonNode>()
            .Select(item => new BiliFavoriteFolder(
                item["id"]?.Value<long>() ?? 0,
                item["title"]?.Value<string>() ?? "未命名收藏夹",
                item["media_count"]?.Value<int>() ?? 0))
            .Where(folder => folder.MediaId > 0)
            .ToArray();
    }

    public async Task<BiliCatalogPage> GetFavoriteItemsAsync(
        long mediaId, int pageSize, string? continuationToken, string cookie,
        CancellationToken cancellationToken)
    {
        var page = ContinuationTokenCodec.DecodePage(continuationToken, "favorite");
        var url = "https://api.bilibili.com/x/v3/fav/resource/list"
            .SetQueryParam("media_id", mediaId).SetQueryParam("pn", page)
            .SetQueryParam("ps", pageSize).SetQueryParam("platform", "web");
        var root = await GetRootAsync(url.ToString(), cookie, Operation.FavoriteItems, null, cancellationToken);
        var data = root["data"]!;
        var items = MapVideoArray(data["medias"] as JsonArray, "upper.name", "pubtime", "cover");
        var hasMore = data["has_more"]?.Value<bool>() == true;
        return new(items, hasMore ? ContinuationTokenCodec.EncodePage("favorite", page + 1) : null, hasMore);
    }

    public async Task<IReadOnlyList<BiliCatalogItem>> GetWatchLaterAsync(
        string cookie, CancellationToken cancellationToken)
    {
        var root = await GetRootAsync(
            "https://api.bilibili.com/x/v2/history/toview/web",
            cookie, Operation.WatchLater, null, cancellationToken);
        return MapVideoArray(root["data"]?["list"] as JsonArray, "owner.name", "pubdate", "pic");
    }

    public async Task<BiliCatalogPage> GetHistoryAsync(
        int pageSize, string? continuationToken, string cookie,
        CancellationToken cancellationToken)
    {
        var cursor = ContinuationTokenCodec.DecodeHistory(continuationToken);
        var url = "https://api.bilibili.com/x/web-interface/history/cursor"
            .SetQueryParam("ps", pageSize).SetQueryParam("max", cursor.Max)
            .SetQueryParam("view_at", cursor.ViewAt).SetQueryParam("business", cursor.Business);
        var root = await GetRootAsync(url.ToString(), cookie, Operation.History, null, cancellationToken);
        var data = root["data"]!;
        var list = data["list"] as JsonArray ?? new JsonArray();
        var items = list.OfType<JsonNode>().Select(item => new BiliCatalogItem(
                item["history"]?["oid"]?.Value<long>() ?? item["aid"]?.Value<long>() ?? 0,
                item["history"]?["bvid"]?.Value<string>() ?? item["bvid"]?.Value<string>() ?? "",
                item["title"]?.Value<string>() ?? "未命名视频",
                item["author_name"]?.Value<string>(),
                item["view_at"]?.Value<long>() ?? 0,
                item["cover"]?.Value<string>()))
            .Where(IsResolvable).ToArray();
        var next = data["cursor"];
        var hasMore = list.Count >= pageSize && next is not null;
        var token = hasMore
            ? ContinuationTokenCodec.EncodeHistory(
                next!["max"]?.Value<long>() ?? 0,
                next["view_at"]?.Value<long>() ?? 0,
                next["business"]?.Value<string>() ?? "")
            : null;
        return new(items, token, hasMore);
    }

    private static async Task<JsonObject> GetRootAsync(
        string url,
        string cookie,
        Operation operation,
        string? referer,
        CancellationToken cancellationToken)
    {
        JsonObject root;
        try
        {
            var request = url.WithHeader("User-Agent", HttpConstants.UserAgent)
                .WithHeader("Referer", referer ?? HttpConstants.Referer);
            if (!string.IsNullOrWhiteSpace(cookie)) request = request.WithHeader("Cookie", cookie);
            root = JsonNodeReader.ParseObject(await request.GetStringAsync(cancellationToken: cancellationToken));
        }
        catch (OperationCanceledException) { throw; }
        catch (FlurlHttpException ex) when (ex.Call.Response?.StatusCode == 401)
        {
            throw CreateError(operation, 401, ContentSourceErrorCode.LoginRequired, "此来源需要登录。");
        }
        catch (FlurlHttpException ex) when (
            ex.Call.Response?.StatusCode == 412
            || ex.Call.Response?.StatusCode == 403 && operation == Operation.Uploader)
        {
            throw CreateError(operation, ex.Call.Response!.StatusCode,
                ContentSourceErrorCode.RiskControlled, RiskControlMessage);
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
        catch (ContentSourceException) { throw; }
        catch
        {
            throw new ContentSourceException(ContentSourceErrorCode.RemoteFailure, "Bilibili 暂时无法返回此来源。");
        }
        var code = root["code"]?.Value<int>() ?? int.MinValue;
        if (code == 0) return root;
        throw code switch
        {
            -101 => CreateError(operation, code, ContentSourceErrorCode.LoginRequired, "此来源需要登录。"),
            -403 => CreateError(operation, code, ContentSourceErrorCode.Forbidden, "当前账号无权访问此来源。"),
            -404 or 11010 => CreateError(operation, code, ContentSourceErrorCode.NotFound, "来源不存在或已失效。"),
            -401 or -352 or -412 => CreateError(operation, code, ContentSourceErrorCode.RiskControlled, RiskControlMessage),
            _ => CreateError(operation, code, ContentSourceErrorCode.RemoteFailure, "Bilibili 暂时无法返回此来源。"),
        };
    }

    private static IReadOnlyList<BiliCatalogItem> MapVideoArray(
        JsonArray? array, string authorPath, string publishedField, string coverField) =>
        (array ?? new JsonArray()).OfType<JsonNode>().Select(item => new BiliCatalogItem(
                item["aid"]?.Value<long>() ?? item["id"]?.Value<long>() ?? 0,
                item["bvid"]?.Value<string>() ?? "",
                item["title"]?.Value<string>() ?? "未命名视频",
                SelectPath(item, authorPath)?.Value<string>(),
                item[publishedField]?.Value<long>() ?? 0,
                item[coverField]?.Value<string>()))
            .Where(IsResolvable).ToArray();

    private static JsonNode? SelectPath(JsonNode item, string path) =>
        path.Split('.').Aggregate<string, JsonNode?>(item, (current, part) => current?[part]);

    private static bool IsResolvable(BiliCatalogItem item) => item.Aid > 0 || !string.IsNullOrWhiteSpace(item.Bvid);

    private const string RiskControlMessage =
        "触发 Bilibili 安全风控，当前登录状态仍可能正常。请暂停一段时间后手动重试。";

    private static ContentSourceException CreateError(
        Operation operation,
        int remoteCode,
        ContentSourceErrorCode classification,
        string message)
    {
        // 只记录稳定分类，不写请求 URL、Cookie、签名或响应正文。
        Log.Warn($"个人来源请求失败：operation={operation}, remoteCode={remoteCode}, classification={classification}");
        return new ContentSourceException(classification, message);
    }
}

/// <summary>版本化不透明游标，Provider 和 UI 均不依赖页码/历史游标的内部格式。</summary>
internal static class ContinuationTokenCodec
{
    private sealed record Token(string Version, string Kind, int Page, long Max, long ViewAt, string Business);

    public static string EncodePage(string kind, int page) => Encode(new("1", kind, page, 0, 0, ""));

    public static int DecodePage(string? value, string kind)
    {
        if (value is null) return 1;
        var token = Decode(value);
        if (token.Kind != kind || token.Page < 2) throw InvalidToken();
        return token.Page;
    }

    public static string EncodeHistory(long max, long viewAt, string business) =>
        Encode(new("1", "history", 0, max, viewAt, business));

    public static (long Max, long ViewAt, string Business) DecodeHistory(string? value)
    {
        if (value is null) return (0, 0, "");
        var token = Decode(value);
        if (token.Kind != "history") throw InvalidToken();
        return (token.Max, token.ViewAt, token.Business);
    }

    private static string Encode(Token token) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Token Decode(string value)
    {
        try
        {
            if (value.Length > 1024) throw InvalidToken();
            var encoded = value.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            var token = JsonSerializer.Deserialize<Token>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            return token is { Version: "1" } ? token : throw InvalidToken();
        }
        catch (ContentSourceException) { throw; }
        catch { throw InvalidToken(); }
    }

    private static ContentSourceException InvalidToken() =>
        new(ContentSourceErrorCode.ProtocolViolation, "分页游标无效或已过期。");
}
