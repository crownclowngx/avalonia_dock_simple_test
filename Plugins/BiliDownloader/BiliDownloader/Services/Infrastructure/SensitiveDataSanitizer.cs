using System.Text.RegularExpressions;

namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// BiliDownloader 所有日志、持久化错误和 UI 错误共用的脱敏规则。
/// </summary>
public static partial class SensitiveDataSanitizer
{
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        var sanitized = HeaderRegex().Replace(input, match => $"{match.Groups[1].Value}: <redacted>");
        sanitized = KnownCookieRegex().Replace(
            sanitized,
            match => $"{match.Groups[1].Value}=<redacted>");
        sanitized = SensitiveParameterRegex().Replace(
            sanitized,
            match => $"{match.Groups[1].Value}=<redacted>");
        sanitized = UrlRegex().Replace(sanitized, StripUrlQuery);
        return sanitized;
    }

    /// <summary>
    /// 任务事实可以保存资源地址，但不能保存可复用的查询签名或片段。
    /// </summary>
    public static string SanitizeUrlForStorage(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)
            || !Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Sanitize(input);
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri.AbsoluteUri;
    }

    private static string StripUrlQuery(Match match)
    {
        var value = match.Value;
        var queryIndex = value.IndexOfAny(['?', '#']);
        return queryIndex < 0 ? value : value[..queryIndex] + "?<redacted>";
    }

    [GeneratedRegex(
        @"(?im)\b(cookie|set-cookie|authorization)\s*[:=]\s*[^\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(
        @"\b(SESSDATA|bili_jct|DedeUserID__ckMd5|DedeUserID|sid|buvid3|buvid4)\s*=\s*[^;\s,&]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KnownCookieRegex();

    [GeneratedRegex(
        @"\b(w_rid|wts|sign|signature|token|access_key|csrf)\s*=\s*[^&\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveParameterRegex();

    [GeneratedRegex(
        "https?://[^\\s\\\"'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();
}
