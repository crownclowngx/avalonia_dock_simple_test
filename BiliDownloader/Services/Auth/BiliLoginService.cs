using System.Text.RegularExpressions;
using Flurl;
using Flurl.Http;
using Newtonsoft.Json.Linq;

namespace BiliDownloader.Services.Auth;

/// <summary>
/// B站登录 API 封装（QR 码扫码登录）
/// </summary>
public partial class BiliLoginService
{
    /// <summary>
    /// 扫码轮询状态码
    /// </summary>
    public enum QrCodeStatus
    {
        /// <summary>成功</summary>
        Success = 0,

        /// <summary>二维码已生成，等待扫码</summary>
        WaitingForScan = 86101,

        /// <summary>已扫码，等待确认</summary>
        ScannedPending = 86090,

        /// <summary>二维码已过期</summary>
        Expired = 86038,
    }

    /// <summary>
    /// 生成登录二维码，返回 (二维码URL, qrcode_key)
    /// </summary>
    public async Task<(string Url, string QrCodeKey)> GetQrCodeAsync()
    {
        var json = await "https://passport.bilibili.com/x/passport-login/web/qrcode/generate"
            .WithHeader("User-Agent", HttpConstants.UserAgent)
            .GetStringAsync();

        var resp = JObject.Parse(json);
        var data = resp["data"];
        var url = data?["url"]?.ToString() ?? throw new Exception("无法获取二维码 URL");
        var key = data?["qrcode_key"]?.ToString() ?? throw new Exception("无法获取 qrcode_key");
        return (url, key);
    }

    /// <summary>
    /// 轮询扫码结果。
    /// 返回 (状态码, Set-Cookie 列表)。
    /// 状态码为 0 时表示登录成功，此时 cookies 非空。
    /// </summary>
    public async Task<(QrCodeStatus Status, List<(string Name, string Value)> Cookies)> PollQrCodeAsync(
        string qrcodeKey)
    {
        var resp = await "https://passport.bilibili.com/x/passport-login/web/qrcode/poll"
            .SetQueryParam("qrcode_key", qrcodeKey)
            .WithHeader("User-Agent", HttpConstants.UserAgent)
            .GetAsync();

        var jsonStr = await resp.GetStringAsync();
        var body = JObject.Parse(jsonStr);
        var code = body["data"]?["code"]?.Value<int>() ?? -1;

        var cookies = new List<(string Name, string Value)>();
        if (code == 0)
        {
            // 从 Set-Cookie 响应头中提取 Cookie
            if (resp.ResponseMessage?.Headers?.TryGetValues("Set-Cookie", out var cookieHeaders) == true)
            {
                foreach (var header in cookieHeaders)
                {
                    var parsed = ParseCookieHeader(header);
                    if (parsed != null) cookies.Add(parsed.Value);
                }
            }
        }

        return ((QrCodeStatus)code, cookies);
    }

    /// <summary>
    /// 验证当前 Cookie 是否有效，同时返回用户名和头像。
    /// 若未登录或 Cookie 失效，返回 (false, null, null)。
    /// </summary>
    public async Task<(bool IsLoggedIn, string? UserName, string? UserAvatar)> CheckLoginAsync(
        string cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
            return (false, null, null);

        try
        {
            var json = await "https://api.bilibili.com/x/web-interface/nav"
                .WithHeader("User-Agent", HttpConstants.UserAgent)
                .WithHeader("Cookie", cookieHeader)
                .GetStringAsync();

            var resp = JObject.Parse(json);

            var isLogin = resp["data"]?["isLogin"]?.Value<bool>() ?? false;
            if (!isLogin) return (false, null, null);

            var uname = resp["data"]?["uname"]?.ToString();
            var face = resp["data"]?["face"]?.ToString();
            return (true, uname, face);
        }
        catch
        {
            return (false, null, null);
        }
    }

    /// <summary>
    /// 调用 B站退出登录接口，使服务端 Cookie 失效
    /// </summary>
    public async Task<bool> ExitLoginAsync(string cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader)) return true;

        try
        {
            // 先从 cookie 中提取 bili_jct 作为 csrf
            var csrf = ExtractCookieValue(cookieHeader, "bili_jct") ?? "";

            var resp = await "https://passport.bilibili.com/login/exit/v2"
                .WithHeader("User-Agent", HttpConstants.UserAgent)
                .WithHeader("Cookie", cookieHeader)
                .PostUrlEncodedAsync(new { biliCSRF = csrf });

            var jsonStr = await resp.GetStringAsync();
            var body = JObject.Parse(jsonStr);
            var code = body["code"]?.Value<int>() ?? -1;
            return code == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从 Set-Cookie 头中解析出 name=value 对
    /// </summary>
    private static (string Name, string Value)? ParseCookieHeader(string header)
    {
        // Set-Cookie 格式：name=value; Path=/; Domain=...
        var match = CookiePairRegexInstance.Match(header);
        if (!match.Success) return null;
        return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
    }

    /// <summary>
    /// 从 Cookie 字符串中提取指定 name 的值
    /// </summary>
    private static string? ExtractCookieValue(string cookieHeader, string name)
    {
        var match = Regex.Match(cookieHeader, $@"(?:^|;\s*){Regex.Escape(name)}=([^;]*)");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static readonly Regex CookiePairRegexInstance =
        new(@"^([^=;\s]+)=?([^;]*)", RegexOptions.Compiled);
}
