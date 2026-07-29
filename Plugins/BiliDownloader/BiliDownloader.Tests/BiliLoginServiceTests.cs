using BiliDownloader.Services.Auth;
using Flurl.Http.Testing;

namespace BiliDownloader.Tests;

public sealed class BiliLoginServiceTests
{
    [Fact]
    public async Task 二维码生成映射Url和Key()
    {
        using var http = new HttpTest();
        http.RespondWith("""
            {"code":0,"data":{"url":"https://login.test/qrcode","qrcode_key":"key-1"}}
            """);

        var result = await new BiliLoginService().GetQrCodeAsync();

        Assert.Equal("https://login.test/qrcode", result.Url);
        Assert.Equal("key-1", result.QrCodeKey);
        http.ShouldHaveCalled("*qrcode/generate")
            .WithVerb(HttpMethod.Get)
            .WithHeader("User-Agent")
            .Times(1);
    }

    [Theory]
    [InlineData("""{"code":0,"data":{"qrcode_key":"key"}}""", "URL")]
    [InlineData("""{"code":0,"data":{"url":"https://login.test"}}""", "qrcode_key")]
    public async Task 二维码响应缺字段会明确失败(string response, string expected)
    {
        using var http = new HttpTest();
        http.RespondWith(response);

        var ex = await Assert.ThrowsAsync<Exception>(
            () => new BiliLoginService().GetQrCodeAsync());

        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(86101, BiliLoginService.QrCodeStatus.WaitingForScan)]
    [InlineData(86090, BiliLoginService.QrCodeStatus.ScannedPending)]
    [InlineData(86038, BiliLoginService.QrCodeStatus.Expired)]
    public async Task 轮询状态码映射保持稳定(
        int code,
        BiliLoginService.QrCodeStatus expected)
    {
        using var http = new HttpTest();
        http.RespondWith("{\"data\":{\"code\":" + code + "}}");

        var result = await new BiliLoginService().PollQrCodeAsync("qr-key");

        Assert.Equal(expected, result.Status);
        Assert.Empty(result.Cookies);
        http.ShouldHaveCalled("*qrcode/poll*")
            .WithQueryParam("qrcode_key", "qr-key")
            .Times(1);
    }

    [Fact]
    public async Task 登录成功会从SetCookie提取名称和值()
    {
        using var http = new HttpTest();
        http.RespondWith(
            """{"data":{"code":0}}""",
            200,
            new Dictionary<string, string>
            {
                ["Set-Cookie"] = "SESSDATA=session-value; Path=/; HttpOnly",
            });

        var result = await new BiliLoginService().PollQrCodeAsync("qr-key");

        Assert.Equal(BiliLoginService.QrCodeStatus.Success, result.Status);
        var cookie = Assert.Single(result.Cookies);
        Assert.Equal("SESSDATA", cookie.Name);
        Assert.Equal("session-value", cookie.Value);
    }

    [Fact]
    public async Task 空Cookie直接判定失效且不发网络请求()
    {
        using var http = new HttpTest();

        var result = await new BiliLoginService().CheckLoginAsync("");

        Assert.Equal(LoginValidationStatus.Invalid, result.Status);
        http.ShouldNotHaveMadeACall();
    }

    [Fact]
    public async Task 有效登录映射用户名头像并携带Cookie()
    {
        using var http = new HttpTest();
        http.RespondWith("""
            {"code":0,"data":{"isLogin":true,"uname":"测试用户","face":"https://img.test/avatar"}}
            """);

        var result = await new BiliLoginService().CheckLoginAsync(
            "SESSDATA=session; bili_jct=csrf");

        Assert.Equal(LoginValidationStatus.Valid, result.Status);
        Assert.Equal("测试用户", result.UserName);
        Assert.Equal("https://img.test/avatar", result.UserAvatar);
        http.ShouldHaveCalled("*x/web-interface/nav")
            .WithHeader("Cookie", "SESSDATA=session; bili_jct=csrf")
            .Times(1);
    }

    [Fact]
    public async Task 服务端未登录返回Invalid而网络异常返回Unavailable()
    {
        using (var http = new HttpTest())
        {
            http.RespondWith("""{"data":{"isLogin":false}}""");
            var result = await new BiliLoginService().CheckLoginAsync("cookie");
            Assert.Equal(LoginValidationStatus.Invalid, result.Status);
        }

        using (var http = new HttpTest())
        {
            http.SimulateException(new HttpRequestException("offline"));
            var result = await new BiliLoginService().CheckLoginAsync("cookie");
            Assert.Equal(LoginValidationStatus.Unavailable, result.Status);
        }
    }

    [Fact]
    public async Task 空Cookie退出直接成功()
    {
        using var http = new HttpTest();

        Assert.True(await new BiliLoginService().ExitLoginAsync(""));
        http.ShouldNotHaveMadeACall();
    }

    [Fact]
    public async Task 退出登录提取Csrf并解析成功码()
    {
        using var http = new HttpTest();
        http.RespondWith("""{"code":0}""");

        var success = await new BiliLoginService().ExitLoginAsync(
            "SESSDATA=session; bili_jct=csrf-token; sid=s");

        Assert.True(success);
        http.ShouldHaveCalled("*login/exit/v2")
            .WithVerb(HttpMethod.Post)
            .WithHeader("Cookie", "SESSDATA=session; bili_jct=csrf-token; sid=s")
            .WithRequestBody("*biliCSRF=csrf-token*")
            .Times(1);
    }

    [Fact]
    public async Task 退出接口错误与网络异常返回False()
    {
        using (var http = new HttpTest())
        {
            http.RespondWith("""{"code":-1}""");
            Assert.False(await new BiliLoginService().ExitLoginAsync("bili_jct=x"));
        }

        using (var http = new HttpTest())
        {
            http.SimulateException(new HttpRequestException("offline"));
            Assert.False(await new BiliLoginService().ExitLoginAsync("bili_jct=x"));
        }
    }
}
