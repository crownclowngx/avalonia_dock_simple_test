namespace BiliDownloader.Services.Download;

/// <summary>
/// CDN URL 筛选与排序辅助类
/// 参考 BiliTools urlFilter 逻辑：优先 mirror(bv) > upos > bcache > others
/// </summary>
public static class CdnUrlHelper
{
    // 已知的快速 mirror 域名列表
    private static readonly string[] MirrorHosts =
    {
        "upos-sz-mirrorali.bilivideo.com",
        "upos-sz-mirrorcos.bilivideo.com",
        "upos-sz-mirrorhw.bilivideo.com",
    };

    /// <summary>
    /// 从 BaseUrl + BackupUrls 中筛选并排序 CDN URL
    /// 优先级：mirror(bv) > upos > bcache > others
    /// 如果镜像不足，尝试将 upos 域名替换为 mirror 域名
    /// </summary>
    /// <param name="baseUrl">主下载 URL</param>
    /// <param name="backupUrls">备用 URL 列表</param>
    /// <returns>去重、排序后的 URL 列表（至少包含 baseUrl）</returns>
    public static List<string> FilterAndSortUrls(string baseUrl, List<string> backupUrls)
    {
        var allUrls = new List<string>();
        if (!string.IsNullOrEmpty(baseUrl))
            allUrls.Add(baseUrl);
        if (backupUrls != null)
            allUrls.AddRange(backupUrls.Where(u => !string.IsNullOrEmpty(u)));

        if (allUrls.Count == 0)
            return new List<string>();

        var mirror = new List<string>();
        var upos = new List<string>();
        var bcache = new List<string>();
        var others = new List<string>();

        foreach (var urlStr in allUrls)
        {
            try
            {
                var uri = new Uri(urlStr);
                var host = uri.Host;
                var os = GetQueryParam(uri, "os") ?? "";

                if (host.Contains("mirror") && os.EndsWith("bv"))
                {
                    mirror.Add(urlStr);
                }
                else if (os == "upos")
                {
                    upos.Add(urlStr);
                }
                else if (host.StartsWith("cn") && os == "bcache")
                {
                    bcache.Add(urlStr);
                }
                else
                {
                    others.Add(urlStr);
                }
            }
            catch
            {
                others.Add(urlStr);
            }
        }

        List<string> result;

        if (mirror.Count > 0)
        {
            // 有 mirror：如果 mirror 不够多，补充 upos
            result = mirror.Count < 2 && upos.Count > 0
                ? mirror.Concat(upos).ToList()
                : mirror.ToList();
        }
        else if (upos.Count > 0 || bcache.Count > 0)
        {
            var source = upos.Count > 0 ? upos : bcache;

            // 尝试将 upos/bcache URL 的域名替换为 mirror 域名
            var rewritten = new List<string>();
            var original = new List<string>();
            for (int i = 0; i < source.Count; i++)
            {
                original.Add(source[i]);
                if (i < MirrorHosts.Length)
                {
                    rewritten.Add(RewriteHost(source[i], MirrorHosts[i]));
                }
            }

            // 同时保留原始 URL 和改写后的 mirror URL
            result = rewritten.Concat(original).Distinct().ToList();
        }
        else
        {
            result = others.ToList();
        }

        // 确保至少有 baseUrl
        if (result.Count == 0 && !string.IsNullOrEmpty(baseUrl))
            result.Add(baseUrl);

        // 去重
        return result.Distinct().ToList();
    }

    /// <summary>
    /// 将 URL 的域名替换为指定域名
    /// </summary>
    private static string RewriteHost(string url, string newHost)
    {
        try
        {
            var uri = new Uri(url);
            var builder = new UriBuilder(uri) { Host = newHost };
            return builder.ToString();
        }
        catch
        {
            return url;
        }
    }

    /// <summary>
    /// 从 Uri 中获取指定查询参数的值
    /// </summary>
    private static string? GetQueryParam(Uri uri, string key)
    {
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return query[key];
    }
}
