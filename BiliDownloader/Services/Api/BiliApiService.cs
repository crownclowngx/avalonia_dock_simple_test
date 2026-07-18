using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using BiliDownloader.Models;
using Flurl.Http;
using Newtonsoft.Json.Linq;

namespace BiliDownloader.Services.Api;

/// <summary>
/// B站 API 封装：URL解析、视频信息获取、wbi签名、DASH流获取
/// </summary>
public partial class BiliApiService
{
    // wbi 签名用的固定 encTab（与 BiliTools auth.ts 一致）
    private static readonly int[] MixinKeyEncTab =
    {
        46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49,
        33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55, 40,
        61, 26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11,
        36, 20, 34, 44, 52
    };

    // 缓存 wbi keys，避免每次都请求
    private string? _cachedMixinKey;
    private DateTime _mixinKeyExpireTime = DateTime.MinValue;

    #region URL 解析

    /// <summary>
    /// 从用户输入中解析出视频 ID（BV号或av号）
    /// </summary>
    /// <param name="input">用户输入的 URL 或 ID</param>
    /// <returns>(id, isBvid) 元组</returns>
    public static (string Id, bool IsBvid)? ParseVideoId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        // 直接输入 BV 号或 av 号
        if (BvRegex().IsMatch(input))
            return (input, true);
        if (AvRegex().IsMatch(input))
            return (input, false);

        // 从 URL 中提取
        // 支持 bilibili.com/video/BVxxx 和 bilibili.com/video/avxxx
        var urlMatch = VideoUrlRegex().Match(input);
        if (urlMatch.Success)
        {
            var id = urlMatch.Groups[1].Value;
            if (id.StartsWith("BV", StringComparison.OrdinalIgnoreCase))
                return (id, true);
            if (id.StartsWith("av", StringComparison.OrdinalIgnoreCase))
                return (id, false);
        }

        // b23.tv 短链：不自动跟踪重定向，提示用户手动展开
        if (input.Contains("b23.tv"))
        {
            // 简单尝试跟随重定向
            return null; // 后续可扩展
        }

        return null;
    }

    #endregion

    #region 视频信息获取

    /// <summary>
    /// 获取视频集合信息（支持单视频多P、番剧等）
    /// </summary>
    public async Task<BiliVideoCollection> GetVideoCollectionAsync(string videoId, bool isBvid, string cookie)
    {
        var url = "https://api.bilibili.com/x/web-interface/view";
        var paramsDict = new Dictionary<string, string>();

        if (isBvid)
            paramsDict["bvid"] = videoId;
        else
            paramsDict["aid"] = videoId[2..]; // 去掉 "av" 前缀

        var json = await BuildRequest(url, paramsDict, cookie).GetStringAsync();
        var resp = JObject.Parse(json);

        if (resp["code"]?.Value<int>() != 0)
            throw new Exception($"获取视频信息失败: {resp["message"]?.Value<string>()}");

        var data = resp["data"]!;
        var title = data["title"]?.Value<string>() ?? "未知标题";
        var aid = data["aid"]?.Value<long>() ?? 0;
        var bvid = data["bvid"]?.Value<string>() ?? videoId;
        var cover = data["pic"]?.Value<string>() ?? "";

        var collection = new BiliVideoCollection
        {
            SeriesTitle = title,
            Cover = cover,
            Items = new List<BiliVideoItem>()
        };

        // 解析分P列表
        var pages = data["pages"] as JArray;
        if (pages != null && pages.Count > 0)
        {
            foreach (var page in pages)
            {
                collection.Items.Add(new BiliVideoItem
                {
                    Title = pages.Count > 1
                        ? (page["part"]?.Value<string>() ?? title)
                        : title,
                    Aid = aid,
                    Bvid = bvid,
                    Cid = page["cid"]?.Value<long>() ?? 0,
                    Duration = page["duration"]?.Value<int>() ?? 0,
                });
            }
        }
        else
        {
            // 无分P，单视频
            collection.Items.Add(new BiliVideoItem
            {
                Title = title,
                Aid = aid,
                Bvid = bvid,
                Cid = data["cid"]?.Value<long>() ?? 0,
                Duration = data["duration"]?.Value<int>() ?? 0,
            });
        }

        // 如果有 ugc_season（合集），解析合集下的所有视频
        var ugcSeason = data["ugc_season"];
        if (ugcSeason != null && ugcSeason.Type == JTokenType.Object)
        {
            collection.SeriesTitle = ugcSeason["title"]?.Value<string>() ?? title;
            var sections = ugcSeason["sections"] as JArray;
            if (sections != null)
            {
                collection.Items.Clear();
                foreach (var section in sections)
                {
                    var episodes = section["episodes"] as JArray;
                    if (episodes == null) continue;
                    foreach (var ep in episodes)
                    {
                        var pageToken = ep["page"];
                        var duration = (pageToken != null && pageToken.Type == JTokenType.Object)
                            ? pageToken["duration"]?.Value<int>() ?? 0
                            : 0;

                        collection.Items.Add(new BiliVideoItem
                        {
                            Title = ep["title"]?.Value<string>() ?? "未知",
                            Aid = ep["aid"]?.Value<long>() ?? 0,
                            Bvid = ep["bvid"]?.Value<string>() ?? "",
                            Cid = ep["cid"]?.Value<long>() ?? 0,
                            Duration = duration,
                        });
                    }
                }
            }
        }

        return collection;
    }

    #endregion

    #region DASH 流获取

    /// <summary>
    /// 获取 DASH 播放流信息（含可用清晰度列表和具体流 URL）
    /// </summary>
    public async Task<BiliDashResult> GetDashResultAsync(long aid, long cid, int qualityId, string cookie)
    {
        var url = "https://api.bilibili.com/x/player/wbi/playurl";
        var paramsDict = new Dictionary<string, string>
        {
            ["avid"] = aid.ToString(),
            ["cid"] = cid.ToString(),
            ["qn"] = qualityId.ToString(),
            ["fnval"] = "4048",   // 请求 DASH 格式
            ["fnver"] = "0",
            ["fourk"] = "1",
        };

        var signedQuery = await WbiSignAsync(paramsDict, cookie);
        var fullUrl = $"{url}?{signedQuery}";

        var json = await fullUrl
            .WithHeader("User-Agent", HttpConstants.UserAgent)
            .WithHeader("Referer", HttpConstants.Referer)
            .WithHeader("Cookie", cookie)
            .GetStringAsync();

        var resp = JObject.Parse(json);
        if (resp["code"]?.Value<int>() != 0)
            throw new Exception($"获取播放地址失败: {resp["message"]?.Value<string>()}");

        var data = resp["data"] ?? resp["result"]?["video_info"];
        if (data == null)
            throw new Exception("无法解析播放数据");

        var result = new BiliDashResult();

        // 解析可用清晰度
        var acceptQuality = data["accept_quality"] as JArray;
        var acceptDesc = data["accept_description"] as JArray;
        if (acceptQuality != null)
        {
            for (int i = 0; i < acceptQuality.Count; i++)
            {
                var qid = acceptQuality[i].Value<int>();
                var desc = (acceptDesc != null && i < acceptDesc.Count)
                    ? acceptDesc[i].Value<string>() ?? $"画质 {qid}"
                    : $"画质 {qid}";
                result.AcceptQualities.Add(new BiliQualityOption
                {
                    QualityId = qid,
                    DisplayName = desc
                });
            }
        }

        // 解析 DASH 流
        var dash = data["dash"];
        if (dash == null || dash.Type != JTokenType.Object)
            throw new Exception("该视频不支持 DASH 格式，请检查登录状态或视频权限");

        // 视频流
        var videos = dash["video"] as JArray;
        if (videos != null)
        {
            foreach (var v in videos)
            {
                result.VideoStreams.Add(ParseDashStream(v));
            }
        }

        // 音频流
        var audios = dash["audio"] as JArray;
        if (audios != null)
        {
            foreach (var a in audios)
            {
                result.AudioStreams.Add(ParseDashStream(a));
            }
        }

        // 杜比音频
        var dolbyToken = dash["dolby"];
        if (dolbyToken != null && dolbyToken.Type == JTokenType.Object)
        {
            var dolby = dolbyToken["audio"] as JArray;
            if (dolby != null)
            {
                foreach (var d in dolby)
                {
                    result.AudioStreams.Add(ParseDashStream(d));
                }
            }
        }

        // Hi-Res 无损音频
        var flacToken = dash["flac"];
        if (flacToken != null && flacToken.Type == JTokenType.Object)
        {
            var flac = flacToken["audio"];
            if (flac != null && flac.Type == JTokenType.Object)
            {
                result.AudioStreams.Add(ParseDashStream(flac));
            }
        }

        return result;
    }

    /// <summary>
    /// 仅获取可用清晰度列表（不解析具体流 URL）
    /// </summary>
    public async Task<List<BiliQualityOption>> GetAvailableQualitiesAsync(long aid, long cid, string cookie)
    {
        var dashResult = await GetDashResultAsync(aid, cid, 80, cookie); // 用 1080P 试探
        return dashResult.AcceptQualities;
    }

    #endregion

    #region wbi 签名

    /// <summary>
    /// 对请求参数进行 wbi 签名，返回签名后的查询字符串
    /// </summary>
    public async Task<string> WbiSignAsync(Dictionary<string, string> queryParams, string cookie)
    {
        var mixinKey = await GetMixinKeyAsync(cookie);

        // 添加时间戳
        queryParams["wts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        // 按 key 排序，过滤特殊字符，拼接
        var chrFilter = new Regex(@"[!'()*]");
        var sortedKeys = queryParams.Keys.OrderBy(k => k).ToList();
        var queryParts = new List<string>();
        foreach (var key in sortedKeys)
        {
            var value = chrFilter.Replace(queryParams[key] ?? "", "");
            queryParts.Add($"{HttpUtility.UrlEncode(key)}={HttpUtility.UrlEncode(value)}");
        }

        var query = string.Join("&", queryParts);
        var wRid = Md5Hash(query + mixinKey);
        return query + "&w_rid=" + wRid;
    }

    /// <summary>
    /// 获取或刷新 mixinKey
    /// </summary>
    private async Task<string> GetMixinKeyAsync(string cookie)
    {
        if (_cachedMixinKey != null && DateTime.UtcNow < _mixinKeyExpireTime)
            return _cachedMixinKey;

        var json = await "https://api.bilibili.com/x/web-interface/nav"
            .WithHeader("User-Agent", HttpConstants.UserAgent)
            .WithHeader("Referer", HttpConstants.Referer)
            .WithHeader("Cookie", cookie)
            .GetStringAsync();

        var resp = JObject.Parse(json);
        var wbiImg = resp["data"]?["wbi_img"];
        var imgUrl = wbiImg?["img_url"]?.Value<string>() ?? "";
        var subUrl = wbiImg?["sub_url"]?.Value<string>() ?? "";

        // 从 URL 中提取文件名（不含扩展名）
        var imgKey = ExtractFileNameWithoutExt(imgUrl);
        var subKey = ExtractFileNameWithoutExt(subUrl);

        // 拼接后按 encTab 重排，取前32字符
        var combined = imgKey + subKey;
        var sb = new StringBuilder(32);
        foreach (var idx in MixinKeyEncTab)
        {
            if (idx < combined.Length)
                sb.Append(combined[idx]);
            if (sb.Length >= 32) break;
        }

        _cachedMixinKey = sb.ToString();
        _mixinKeyExpireTime = DateTime.UtcNow.AddMinutes(30); // 缓存30分钟
        return _cachedMixinKey;
    }

    #endregion

    #region 辅助方法

    private static BiliDashStream ParseDashStream(JToken token)
    {
        var backupUrls = new List<string>();
        var backupUrl = token["backup_url"] as JArray;
        if (backupUrl != null)
        {
            foreach (var u in backupUrl)
                backupUrls.Add(u.Value<string>() ?? "");
        }

        return new BiliDashStream
        {
            Id = token["id"]?.Value<int>() ?? 0,
            BaseUrl = token["base_url"]?.Value<string>() ?? token["baseUrl"]?.Value<string>() ?? "",
            BackupUrls = backupUrls,
            Codecid = token["codecid"]?.Value<int>() ?? 0,
            Bandwidth = token["bandwidth"]?.Value<long>() ?? 0,
        };
    }

    private static string ExtractFileNameWithoutExt(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        var lastSlash = url.LastIndexOf('/');
        var lastDot = url.LastIndexOf('.');
        if (lastSlash < 0) return "";
        var start = lastSlash + 1;
        var end = lastDot > start ? lastDot : url.Length;
        return url[start..end];
    }

    private static string Md5Hash(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private IFlurlRequest BuildRequest(string url, Dictionary<string, string> paramsDict, string cookie)
    {
        var req = url
            .WithHeader("User-Agent", HttpConstants.UserAgent)
            .WithHeader("Referer", HttpConstants.Referer)
            .WithHeader("Cookie", cookie);

        foreach (var kv in paramsDict)
            req = req.SetQueryParam(kv.Key, kv.Value);

        return req;
    }

    // 编译时正则
    [GeneratedRegex(@"^BV[\w]{10}$", RegexOptions.IgnoreCase)]
    private static partial Regex BvRegex();

    [GeneratedRegex(@"^av\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex AvRegex();

    [GeneratedRegex(@"bilibili\.com/video/(BV[\w]{10}|av\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex VideoUrlRegex();

    #endregion
}
