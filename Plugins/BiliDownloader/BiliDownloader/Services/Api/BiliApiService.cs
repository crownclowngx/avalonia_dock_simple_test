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

    // 缓存 wbi keys，避免每次都请求（static 以便跨实例共享）
    private static string? _cachedMixinKey;
    private static DateTime _mixinKeyExpireTime = DateTime.MinValue;

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

        // b23.tv 短链：自动跟踪重定向获取真实 URL
        if (input.Contains("b23.tv"))
        {
            // 返回特殊标记，由调用方使用 ResolveB23TvAsync 异步解析
            return null;
        }

        return null;
    }

    /// <summary>
    /// 从用户输入中解析番剧 ID（ep/ss/md 号或 bangumi URL）
    /// </summary>
    /// <param name="input">用户输入的 URL 或 ID</param>
    /// <returns>(原始ID, 是否为season_id) 或 null</returns>
    public static (string Id, bool IsSeasonId)? ParseBangumiId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        // 直接输入 ep/ss/md 号
        if (EpRegex().IsMatch(input))
            return (input, false); // ep_id
        if (SsRegex().IsMatch(input))
            return (input, true);  // season_id
        if (MdRegex().IsMatch(input))
            return (input, false); // media_id，后续需转换

        // 从 URL 中提取：bilibili.com/bangumi/play/ep12345 或 ss12345
        var urlMatch = BangumiUrlRegex().Match(input);
        if (urlMatch.Success)
        {
            var id = urlMatch.Groups[1].Value;
            if (id.StartsWith("ss", StringComparison.OrdinalIgnoreCase))
                return (id, true);
            return (id, false); // ep
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

    #region 番剧信息获取

    /// <summary>
    /// 获取番剧剧集列表
    /// API: /pgc/view/web/season（不需要 wbi 签名）
    /// md 号需先调 /pgc/review/user 转 season_id
    /// </summary>
    public async Task<BiliVideoCollection> GetBangumiCollectionAsync(string id, bool isSeasonId, string cookie)
    {
        var idPrefix = id[..2].ToLowerInvariant();
        var idNum = id[2..]; // 去掉前缀

        var paramsDict = new Dictionary<string, string>();

        if (idPrefix == "md")
        {
            // md 号需先转为 season_id
            var seasonId = await ResolveMediaIdToSeasonIdAsync(idNum, cookie);
            paramsDict["season_id"] = seasonId.ToString();
        }
        else if (isSeasonId)
        {
            paramsDict["season_id"] = idNum;
        }
        else
        {
            paramsDict["ep_id"] = idNum;
        }

        var json = await BuildRequest("https://api.bilibili.com/pgc/view/web/season", paramsDict, cookie)
            .GetStringAsync();
        var resp = JObject.Parse(json);

        if (resp["code"]?.Value<int>() != 0)
            throw new Exception($"获取番剧信息失败: {resp["message"]?.Value<string>()}");

        var data = resp["result"]!;
        var seasonTitle = data["season_title"]?.Value<string>() ?? "未知番剧";
        var seasonId2 = data["season_id"]?.Value<long>() ?? 0;
        var cover = data["cover"]?.Value<string>() ?? "";

        var collection = new BiliVideoCollection
        {
            SeriesTitle = seasonTitle,
            Cover = cover,
            Items = new List<BiliVideoItem>()
        };

        // 解析正片剧集列表
        var episodes = data["episodes"] as JArray;
        if (episodes != null)
        {
            int idx = 1;
            foreach (var ep in episodes)
            {
                collection.Items.Add(new BiliVideoItem
                {
                    Index = idx++,
                    Title = ep["long_title"]?.Value<string>() ?? ep["title"]?.Value<string>() ?? $"第{idx - 1}话",
                    Aid = ep["aid"]?.Value<long>() ?? 0,
                    Bvid = ep["bvid"]?.Value<string>() ?? "",
                    Cid = ep["cid"]?.Value<long>() ?? 0,
                    Duration = (int)((ep["duration"]?.Value<long>() ?? 0) / 1000), // 番剧 duration 为毫秒
                    MediaType = BiliMediaType.Bangumi,
                    EpId = ep["ep_id"]?.Value<long>() ?? 0,
                    SeasonId = seasonId2,
                });
            }
        }

        // 解析 section（附加内容如 PV、SP 等）
        var sections = data["section"] as JArray;
        if (sections != null)
        {
            foreach (var section in sections)
            {
                var sectionEps = section["episodes"] as JArray;
                if (sectionEps == null) continue;
                foreach (var ep in sectionEps)
                {
                    collection.Items.Add(new BiliVideoItem
                    {
                        Title = ep["long_title"]?.Value<string>() ?? ep["title"]?.Value<string>() ?? "未知",
                        Aid = ep["aid"]?.Value<long>() ?? 0,
                        Bvid = ep["bvid"]?.Value<string>() ?? "",
                        Cid = ep["cid"]?.Value<long>() ?? 0,
                        Duration = (int)((ep["duration"]?.Value<long>() ?? 0) / 1000),
                        MediaType = BiliMediaType.Bangumi,
                        EpId = ep["ep_id"]?.Value<long>() ?? 0,
                        SeasonId = seasonId2,
                    });
                }
            }
        }

        return collection;
    }

    /// <summary>
    /// md 号转 season_id
    /// API: /pgc/review/user
    /// </summary>
    private async Task<long> ResolveMediaIdToSeasonIdAsync(string mediaId, string cookie)
    {
        var json = await BuildRequest("https://api.bilibili.com/pgc/review/user",
            new Dictionary<string, string> { ["media_id"] = mediaId }, cookie)
            .GetStringAsync();
        var resp = JObject.Parse(json);

        var seasonId = resp["result"]?["media"]?["season_id"]?.Value<long>();
        if (seasonId == null || seasonId == 0)
            throw new Exception($"无法解析 md 号对应的番剧: media_id={mediaId}");

        return seasonId.Value;
    }

    #endregion

    #region 附加资源 API（弹幕/字幕）

    /// <summary>
    /// 获取弹幕分段（Protobuf 二进制数据）
    /// API: /x/v2/dm/wbi/web/seg.so（需 wbi 签名）
    /// </summary>
    /// <param name="oid">视频 cid</param>
    /// <param name="segmentIndex">分段序号（从 1 开始）</param>
    /// <param name="aid">视频 aid</param>
    /// <param name="cookie">Cookie</param>
    /// <returns>Protobuf 二进制数据</returns>
    public async Task<byte[]> GetDanmakuSegmentAsync(long oid, int segmentIndex, long aid, string cookie)
    {
        var url = "https://api.bilibili.com/x/v2/dm/wbi/web/seg.so";
        var paramsDict = new Dictionary<string, string>
        {
            ["type"] = "1",
            ["oid"] = oid.ToString(),
            ["pid"] = aid.ToString(),
            ["segment_index"] = segmentIndex.ToString(),
        };

        var signedQuery = await WbiSignAsync(paramsDict, cookie);
        var fullUrl = $"{url}?{signedQuery}";

        var request = fullUrl
            .WithHeader("User-Agent", HttpConstants.UserAgent)
            .WithHeader("Referer", HttpConstants.Referer);
        var response = await WithCookie(request, cookie).GetBytesAsync();

        return response;
    }

    /// <summary>
    /// 获取字幕列表（含字幕语言、下载 URL）
    /// API: /x/player/wbi/v2（需 wbi 签名）
    /// </summary>
    public async Task<List<SubtitleListItem>> GetSubtitleListAsync(long aid, long cid, string cookie)
    {
        var url = "https://api.bilibili.com/x/player/wbi/v2";
        var paramsDict = new Dictionary<string, string>
        {
            ["aid"] = aid.ToString(),
            ["cid"] = cid.ToString(),
        };

        var signedQuery = await WbiSignAsync(paramsDict, cookie);
        var fullUrl = $"{url}?{signedQuery}";

        var request = fullUrl
            .WithHeader("User-Agent", HttpConstants.UserAgent)
            .WithHeader("Referer", HttpConstants.Referer);
        var json = await WithCookie(request, cookie).GetStringAsync();

        var resp = JObject.Parse(json);
        if (resp["code"]?.Value<int>() != 0)
            throw new Exception($"获取字幕列表失败: {resp["message"]?.Value<string>()}");

        var subtitles = new List<SubtitleListItem>();
        var subtitleArray = resp["data"]?["subtitle"]?["subtitles"] as JArray;
        if (subtitleArray == null)
            return subtitles;

        foreach (var sub in subtitleArray)
        {
            var subtitleUrl = sub["subtitle_url"]?.Value<string>() ?? "";
            if (string.IsNullOrEmpty(subtitleUrl)) continue;

            // 补充协议头（B站返回的 URL 可能是 // 开头）
            if (subtitleUrl.StartsWith("//"))
                subtitleUrl = "https:" + subtitleUrl;

            subtitles.Add(new SubtitleListItem
            {
                Lan = sub["lan"]?.Value<string>() ?? "",
                LanDoc = sub["lan_doc"]?.Value<string>() ?? "",
                SubtitleUrl = subtitleUrl,
            });
        }

        return subtitles;
    }

    /// <summary>
    /// 下载字幕 JSON 内容并转换为 SRT 格式
    /// </summary>
    /// <param name="subtitleUrl">字幕下载 URL</param>
    /// <param name="cookie">Cookie</param>
    /// <returns>SRT 格式的字幕文本</returns>
    public async Task<string> GetSubtitleSrtAsync(string subtitleUrl, string cookie)
    {
        var request = subtitleUrl
            .WithHeader("User-Agent", HttpConstants.UserAgent)
            .WithHeader("Referer", HttpConstants.Referer);
        var json = await WithCookie(request, cookie).GetStringAsync();

        var resp = JObject.Parse(json);
        var body = resp["body"] as JArray;
        if (body == null || body.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < body.Count; i++)
        {
            var from = body[i]["from"]?.Value<double>() ?? 0;
            var to = body[i]["to"]?.Value<double>() ?? 0;
            var content = body[i]["content"]?.Value<string>() ?? "";

            sb.AppendLine((i + 1).ToString());
            sb.AppendLine($"{FormatSrtTime(from)} --> {FormatSrtTime(to)}");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 将秒数转换为 SRT 时间格式 (HH:mm:ss,fff)
    /// </summary>
    private static string FormatSrtTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
    }

    #endregion

    #region DASH 流获取

    /// <summary>
    /// 获取 DASH 播放流信息（含可用清晰度列表和具体流 URL）
    /// </summary>
    /// <param name="aid">视频 aid</param>
    /// <param name="cid">视频 cid</param>
    /// <param name="qualityId">清晰度 ID</param>
    /// <param name="cookie">Cookie</param>
    /// <param name="mediaType">媒体类型（默认 Video，番剧传 Bangumi）</param>
    /// <param name="epId">番剧 ep_id（仅 Bangumi 时需要）</param>
    /// <param name="seasonId">番剧 season_id（仅 Bangumi 时需要）</param>
    public async Task<BiliDashResult> GetDashResultAsync(
        long aid, long cid, int qualityId, string cookie,
        BiliMediaType mediaType = BiliMediaType.Video,
        long epId = 0, long seasonId = 0)
    {
        string url;
        var paramsDict = new Dictionary<string, string>();

        if (mediaType == BiliMediaType.Bangumi)
        {
            url = "https://api.bilibili.com/pgc/player/web/v2/playurl";
            paramsDict["ep_id"] = epId.ToString();
            paramsDict["season_id"] = seasonId.ToString();
            paramsDict["qn"] = qualityId.ToString();
            paramsDict["fnval"] = "4048";
            paramsDict["fnver"] = "0";
            paramsDict["fourk"] = "1";
        }
        else
        {
            url = "https://api.bilibili.com/x/player/wbi/playurl";
            paramsDict["avid"] = aid.ToString();
            paramsDict["cid"] = cid.ToString();
            paramsDict["qn"] = qualityId.ToString();
            paramsDict["fnval"] = "4048";
            paramsDict["fnver"] = "0";
            paramsDict["fourk"] = "1";
        }

        var signedQuery = await WbiSignAsync(paramsDict, cookie);
        var fullUrl = $"{url}?{signedQuery}";

        var request = fullUrl
            .WithHeader("User-Agent", HttpConstants.UserAgent)
            .WithHeader("Referer", HttpConstants.Referer);
        var json = await WithCookie(request, cookie).GetStringAsync();

        var resp = JObject.Parse(json);
        if (resp["code"]?.Value<int>() != 0)
        {
            var code = resp["code"]?.Value<int>() ?? -1;
            var msg = resp["message"]?.Value<string>() ?? "未知错误";
            // 番剧大会员检测
            if (mediaType == BiliMediaType.Bangumi && (code == -403 || code == -10403))
                throw new Exception($"该番剧需要大会员才能下载，请确认已登录大会员账号 (code: {code})");
            throw new Exception($"获取播放地址失败: {msg} (code: {code})");
        }

        // 兼容普通视频（data）和番剧（result.video_info）两种响应格式
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

        var request = "https://api.bilibili.com/x/web-interface/nav"
            .WithHeader("User-Agent", HttpConstants.UserAgent)
            .WithHeader("Referer", HttpConstants.Referer);
        var json = await WithCookie(request, cookie).GetStringAsync();

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

    #region b23.tv 短链解析

    /// <summary>
    /// 检测输入是否包含 b23.tv 短链
    /// </summary>
    public static bool IsB23TvLink(string input) =>
        !string.IsNullOrWhiteSpace(input) && input.Contains("b23.tv");

    /// <summary>
    /// 异步解析 b23.tv 短链，跟随重定向获取真实 URL
    /// </summary>
    /// <param name="b23TvUrl">b23.tv 短链 URL</param>
    /// <returns>重定向后的真实 URL</returns>
    public static async Task<string> ResolveB23TvAsync(string b23TvUrl)
    {
        var response = await b23TvUrl
            .WithHeader("User-Agent", HttpConstants.UserAgent)
            .WithAutoRedirect(false)
            .HeadAsync();

        var locationHeader = response.Headers
            .FirstOrDefault(h => h.Name.Equals("Location", StringComparison.OrdinalIgnoreCase));
        var location = locationHeader.Value;

        if (!string.IsNullOrEmpty(location))
            return location;

        throw new Exception("无法解析 b23.tv 短链，请手动展开后重试");
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
            .WithHeader("Referer", HttpConstants.Referer);
        req = WithCookie(req, cookie);

        foreach (var kv in paramsDict)
            req = req.SetQueryParam(kv.Key, kv.Value);

        return req;
    }

    private static IFlurlRequest WithCookie(IFlurlRequest request, string cookie)
        => string.IsNullOrWhiteSpace(cookie)
            ? request
            : request.WithHeader("Cookie", cookie);

    // 编译时正则 — 普通视频
    [GeneratedRegex(@"^BV[\w]{10}$", RegexOptions.IgnoreCase)]
    private static partial Regex BvRegex();

    [GeneratedRegex(@"^av\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex AvRegex();

    [GeneratedRegex(@"bilibili\.com/video/(BV[\w]{10}|av\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex VideoUrlRegex();

    // 编译时正则 — 番剧
    [GeneratedRegex(@"^ep\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex EpRegex();

    [GeneratedRegex(@"^ss\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex SsRegex();

    [GeneratedRegex(@"^md\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex MdRegex();

    [GeneratedRegex(@"bilibili\.com/bangumi/play/(ep\d+|ss\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex BangumiUrlRegex();

    #endregion
}
