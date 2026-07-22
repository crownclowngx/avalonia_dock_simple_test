using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Tests;

public sealed class SensitiveDataSanitizerTests
{
    [Fact]
    public void 日志脱敏会移除请求头Cookie和URL参数()
    {
        const string secret = "secret-value";
        var input = $"""
            Cookie: SESSDATA={secret}; bili_jct=csrf-secret
            Authorization: Bearer bearer-secret
            请求 https://api.bilibili.com/x/player/wbi/playurl?avid=1&w_rid=signed-secret&wts=123
            单独参数 token=token-secret
            """;

        var sanitized = SensitiveDataSanitizer.Sanitize(input);

        Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("csrf-secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("signed-secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("token-secret", sanitized, StringComparison.Ordinal);
        Assert.Contains("Cookie: <redacted>", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://api.bilibili.com/x/player/wbi/playurl?<redacted>", sanitized);
    }
}
