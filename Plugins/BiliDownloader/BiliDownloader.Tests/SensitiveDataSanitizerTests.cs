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

    [Theory]
    [InlineData("cookie=SESSDATA=lower-secret", "lower-secret")]
    [InlineData("SET-COOKIE: bili_jct=csrf-secret; Path=/", "csrf-secret")]
    [InlineData("authorization=Bearer bearer-secret", "bearer-secret")]
    [InlineData("DedeUserID=user-secret", "user-secret")]
    [InlineData("access_key=access-secret", "access-secret")]
    [InlineData("csrf=csrf-secret", "csrf-secret")]
    public void 不同大小写和常见敏感键都会被清洗(string input, string secret)
    {
        var sanitized = SensitiveDataSanitizer.Sanitize(input);

        Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        Assert.Contains("<redacted>", sanitized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("普通错误信息", "普通错误信息")]
    [InlineData("相对路径/video.mp4", "相对路径/video.mp4")]
    public void 空值和普通文本保持稳定(string? input, string expected)
    {
        Assert.Equal(expected, SensitiveDataSanitizer.Sanitize(input));
    }

    [Fact]
    public void 存储URL会移除查询和片段并保留安全路径()
    {
        var sanitized = SensitiveDataSanitizer.SanitizeUrlForStorage(
            "https://cdn.example.test/video/file.m4s?token=secret#part");

        Assert.Equal("https://cdn.example.test/video/file.m4s", sanitized.TrimEnd('/'));
    }

    [Fact]
    public void 非HTTP绝对地址回退到普通清洗规则()
    {
        var sanitized = SensitiveDataSanitizer.SanitizeUrlForStorage(
            "file:///tmp/video?token=secret");

        Assert.DoesNotContain("secret", sanitized, StringComparison.Ordinal);
        Assert.Contains("<redacted>", sanitized, StringComparison.Ordinal);
    }
}
