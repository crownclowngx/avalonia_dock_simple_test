using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.ViewModels.BiliDownloader;
using Flurl.Http.Testing;

namespace BiliDownloader.Tests;

public sealed class PersonalContentProviderG1Tests
{
    [Fact]
    public void 四类Provider只声明当前真正实现的能力()
    {
        var account = new FakeAccount(true, 42);
        var resolver = new FakeItemResolver();

        Assert.Equal(ContentSourceCapabilities.SupportsPaging | ContentSourceCapabilities.SupportsIncremental,
            new UploaderSourceProvider(new FakeUploaderApi(), account, resolver).Capabilities);
        Assert.Equal(ContentSourceCapabilities.SupportsPaging | ContentSourceCapabilities.SupportsIncremental,
            new FavoriteSourceProvider(new FakeFavoriteApi(), account, resolver).Capabilities);
        Assert.Equal(ContentSourceCapabilities.RequiresLogin | ContentSourceCapabilities.SupportsPaging |
            ContentSourceCapabilities.SupportsIncremental,
            new WatchLaterSourceProvider(new FakeWatchLaterApi(), account, resolver, new()).Capabilities);
        Assert.Equal(ContentSourceCapabilities.RequiresLogin | ContentSourceCapabilities.SupportsPaging |
            ContentSourceCapabilities.SupportsIncremental,
            new HistorySourceProvider(new FakeHistoryApi(), account, resolver).Capabilities);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("https://space.bilibili.com/42/video")]
    public async Task Up主来源规范化稳定ID并支持匿名(string input)
    {
        var provider = new UploaderSourceProvider(
            new FakeUploaderApi(), new FakeAccount(false, null), new FakeItemResolver());

        var descriptor = await provider.NormalizeAsync(input, CancellationToken.None);

        Assert.Equal("uploader:42", descriptor.StableSourceId);
        Assert.False(provider.Capabilities.HasFlag(ContentSourceCapabilities.RequiresLogin));
    }

    [Fact]
    public async Task Up主分页原样传递不透明游标并映射公开字段()
    {
        var api = new FakeUploaderApi
        {
            Page = new BiliCatalogPage([Catalog(1, "BV1abcDEF123")], "opaque-next", true),
        };
        var provider = new UploaderSourceProvider(api, new FakeAccount(false, null), new FakeItemResolver());
        var descriptor = await provider.NormalizeAsync("42", CancellationToken.None);

        var page = await provider.GetPageAsync(descriptor, new ContentPageRequest(10, "opaque-current"), CancellationToken.None);

        Assert.Equal("opaque-current", api.LastToken);
        Assert.Equal("opaque-next", page.NextContinuationToken);
        Assert.Equal(1, Assert.Single(page.Items).Aid);
    }

    [Fact]
    public async Task 我的收藏夹需要登录并返回可直接浏览的描述符()
    {
        var api = new FakeFavoriteApi
        {
            Folders = [new BiliFavoriteFolder(7, "学习", 3)],
        };
        var loggedOut = new FavoriteSourceProvider(api, new FakeAccount(false, null), new FakeItemResolver());
        var login = await Assert.ThrowsAsync<ContentSourceException>(() => loggedOut.GetMyFoldersAsync(CancellationToken.None));
        Assert.Equal(ContentSourceErrorCode.LoginRequired, login.Code);

        var loggedIn = new FavoriteSourceProvider(api, new FakeAccount(true, 42), new FakeItemResolver());
        var folder = Assert.Single(await loggedIn.GetMyFoldersAsync(CancellationToken.None));
        Assert.Equal("favorite:7", folder.StableSourceId);
        Assert.Contains("学习", folder.DisplayName);
    }

    [Fact]
    public async Task 公开收藏夹ID无需登录即可浏览()
    {
        var api = new FakeFavoriteApi
        {
            Page = new BiliCatalogPage([Catalog(2, "BV2abcDEF123")], null, false),
        };
        var provider = new FavoriteSourceProvider(api, new FakeAccount(false, null), new FakeItemResolver());
        var descriptor = await provider.NormalizeAsync("https://www.bilibili.com/medialist/detail/ml7", CancellationToken.None);

        var page = await provider.GetPageAsync(descriptor, new ContentPageRequest(), CancellationToken.None);

        Assert.Equal(7, api.LastMediaId);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task 稍后再看使用有界快照分页且只请求远端一次()
    {
        var api = new FakeWatchLaterApi { Items = [Catalog(1), Catalog(2), Catalog(3)] };
        var provider = new WatchLaterSourceProvider(api, new FakeAccount(true, 42), new FakeItemResolver(), new());
        var descriptor = await provider.NormalizeAsync("self", CancellationToken.None);

        var first = await provider.GetPageAsync(descriptor, new ContentPageRequest(2), CancellationToken.None);
        api.Items = [Catalog(9)];
        var second = await provider.GetPageAsync(descriptor, new ContentPageRequest(2, first.NextContinuationToken), CancellationToken.None);

        Assert.Equal(1, api.CallCount);
        Assert.Equal([1L, 2L], first.Items.Select(item => item.Aid));
        Assert.Equal(3, Assert.Single(second.Items).Aid);
        Assert.False(second.HasMore);
        Assert.Equal(first.SnapshotToken, second.SnapshotToken);
    }

    [Theory]
    [InlineData(ContentSourceKind.WatchLater)]
    [InlineData(ContentSourceKind.History)]
    public async Task 私有来源在规范化边界要求有效账号(ContentSourceKind kind)
    {
        IContentSourceProvider provider = kind == ContentSourceKind.WatchLater
            ? new WatchLaterSourceProvider(new FakeWatchLaterApi(), new FakeAccount(false, null), new FakeItemResolver(), new())
            : new HistorySourceProvider(new FakeHistoryApi(), new FakeAccount(false, null), new FakeItemResolver());

        var error = await Assert.ThrowsAsync<ContentSourceException>(async () =>
            await provider.NormalizeAsync("self", CancellationToken.None));

        Assert.Equal(ContentSourceErrorCode.LoginRequired, error.Code);
    }

    [Fact]
    public async Task 历史记录透传服务端游标并保持分页能力()
    {
        var api = new FakeHistoryApi
        {
            Page = new BiliCatalogPage([Catalog(8)], "history-next", true),
        };
        var provider = new HistorySourceProvider(api, new FakeAccount(true, 42), new FakeItemResolver());
        var descriptor = await provider.NormalizeAsync("self", CancellationToken.None);

        var page = await provider.GetPageAsync(descriptor, new ContentPageRequest(20, "history-current"), CancellationToken.None);

        Assert.Equal("history-current", api.LastToken);
        Assert.Equal("history-next", page.NextContinuationToken);
    }

    [Fact]
    public async Task 所有个人来源通过共享解析器汇入下载集合()
    {
        var resolver = new FakeItemResolver();
        var provider = new UploaderSourceProvider(new FakeUploaderApi(), new FakeAccount(false, null), resolver);
        var descriptor = await provider.NormalizeAsync("42", CancellationToken.None);
        var item = new ContentSourceItem(new(ContentSourceKind.Uploader, "aid:1"), "标题", ContentSourceItemType.Video, aid: 1);

        var collection = await provider.ResolveItemAsync(descriptor, item, CancellationToken.None);

        Assert.Equal(1, resolver.CallCount);
        Assert.Single(collection.Items);
    }

    [Fact]
    public void 账号上下文从Cookie精确读取用户ID且不暴露其他字段()
    {
        var context = new BiliAccountContext(new StubCredentials("SESSDATA=x; DedeUserID=123; bili_jct=y"));
        Assert.Equal(123, context.UserId);
        Assert.True(context.IsLoggedIn);
    }

    [Fact]
    public void 不透明游标拒绝跨来源和畸形输入()
    {
        var uploader = ContinuationTokenCodec.EncodePage("uploader", 2);
        Assert.Equal(2, ContinuationTokenCodec.DecodePage(uploader, "uploader"));
        Assert.Equal(ContentSourceErrorCode.ProtocolViolation,
            Assert.Throws<ContentSourceException>(() => ContinuationTokenCodec.DecodePage(uploader, "favorite")).Code);
        Assert.Throws<ContentSourceException>(() => ContinuationTokenCodec.DecodeHistory("not-base64"));
    }

    [Fact]
    public async Task 登录失效时浏览器保留已加载项和用户选择()
    {
        var provider = new AuthFailingSecondPageProvider();
        var browser = new ContentSourceBrowserViewModel(
            new ContentSourceProviderRegistry([provider]),
            new VideoParseResultFactory(new StubMediaProbe(), new StubCredentials("")),
            _ => { });
        var descriptor = await provider.NormalizeAsync("self", CancellationToken.None);
        await browser.OpenAsync(descriptor);
        browser.Items[0].IsSelected = true;

        await browser.LoadMoreCommand.ExecuteAsync(null);

        Assert.Single(browser.Items);
        Assert.True(browser.Items[0].IsSelected);
        Assert.Contains("登录", browser.Status);
        Assert.True(browser.CanRetry);
    }

    [Fact]
    public async Task 风控失败只在用户手动重试后恢复且不会重复加载()
    {
        var provider = new RiskControlledThenSuccessProvider();
        var browser = new ContentSourceBrowserViewModel(
            new ContentSourceProviderRegistry([provider]),
            new VideoParseResultFactory(new StubMediaProbe(), new StubCredentials("")),
            _ => { });
        var descriptor = await provider.NormalizeAsync("42", CancellationToken.None);

        await browser.OpenAsync(descriptor);

        Assert.Empty(browser.Items);
        Assert.True(browser.CanRetry);
        Assert.Equal(1, provider.CallCount);

        await browser.RetryCommand.ExecuteAsync(null);

        Assert.Single(browser.Items);
        Assert.False(browser.CanRetry);
        Assert.False(browser.RetryCommand.CanExecute(null));
        Assert.Equal(2, provider.CallCount);
    }

    private static BiliCatalogItem Catalog(long aid, string? bvid = null) =>
        new(aid, bvid ?? $"BV{aid:0000000000}", $"视频 {aid}", "作者", 1_700_000_000, "https://image.test/a.jpg");
}

public sealed class PersonalContentApiG1Tests
{
    [Fact]
    public async Task 收藏夹Fixture映射分页字段和作者()
    {
        using var http = new HttpTest();
        http.ForCallsTo("https://api.bilibili.com/x/v3/fav/resource/list*").RespondWithJson(new
        {
            code = 0,
            data = new
            {
                has_more = true,
                medias = new[] { new { id = 9, bvid = "BV1abcDEF123", title = "收藏视频", upper = new { name = "UP" }, pubtime = 1_700_000_000, cover = "pic" } },
            },
        });
        var api = new BiliPersonalContentApi(new BiliApiService());

        var page = await api.GetFavoriteItemsAsync(7, 20, null, "", CancellationToken.None);

        Assert.True(page.HasMore);
        Assert.Equal("UP", Assert.Single(page.Items).Author);
        Assert.NotNull(page.NextToken);
    }

    [Theory]
    [InlineData(-101, ContentSourceErrorCode.LoginRequired)]
    [InlineData(-403, ContentSourceErrorCode.Forbidden)]
    [InlineData(11010, ContentSourceErrorCode.NotFound)]
    [InlineData(-401, ContentSourceErrorCode.RiskControlled)]
    [InlineData(-352, ContentSourceErrorCode.RiskControlled)]
    [InlineData(-412, ContentSourceErrorCode.RiskControlled)]
    [InlineData(-500, ContentSourceErrorCode.RemoteFailure)]
    public async Task 远端响应码映射为稳定领域错误(int code, ContentSourceErrorCode expected)
    {
        using var http = new HttpTest();
        http.RespondWithJson(new { code, message = "remote secret" });
        var api = new BiliPersonalContentApi(new BiliApiService());

        var error = await Assert.ThrowsAsync<ContentSourceException>(() =>
            api.GetWatchLaterAsync("", CancellationToken.None));

        Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public async Task 历史Fixture生成下一游标且过滤不可解析项目()
    {
        using var http = new HttpTest();
        http.RespondWithJson(new
        {
            code = 0,
            data = new
            {
                list = new object[]
                {
                    new { title = "历史视频", author_name = "UP", view_at = 1_700_000_000, cover = "pic", history = new { oid = 8, bvid = "BV1abcDEF123" } },
                    new { title = "无效项目", history = new { oid = 0, bvid = "" } },
                },
                cursor = new { max = 8, view_at = 1_700_000_000, business = "archive" },
            },
        });
        var api = new BiliPersonalContentApi(new BiliApiService());

        var page = await api.GetHistoryAsync(2, null, "SESSDATA=x", CancellationToken.None);

        Assert.Single(page.Items);
        Assert.True(page.HasMore);
        Assert.NotNull(page.NextToken);
    }

    [Fact]
    public async Task Http限流映射为RateLimited而不泄露响应体()
    {
        using var http = new HttpTest();
        http.RespondWith("remote secret", 429);
        var api = new BiliPersonalContentApi(new BiliApiService());

        var error = await Assert.ThrowsAsync<ContentSourceException>(() =>
            api.GetWatchLaterAsync("", CancellationToken.None));

        Assert.Equal(ContentSourceErrorCode.RateLimited, error.Code);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Theory]
    [InlineData(401, ContentSourceErrorCode.LoginRequired)]
    [InlineData(403, ContentSourceErrorCode.Forbidden)]
    [InlineData(412, ContentSourceErrorCode.RiskControlled)]
    [InlineData(429, ContentSourceErrorCode.RateLimited)]
    public async Task Http状态按登录权限风控和限流分别映射(int status, ContentSourceErrorCode expected)
    {
        using var http = new HttpTest();
        http.RespondWith("Cookie: SESSDATA=remote-secret", status);
        var api = new BiliPersonalContentApi(new BiliApiService());

        var error = await Assert.ThrowsAsync<ContentSourceException>(() =>
            api.GetWatchLaterAsync("SESSDATA=local-secret", CancellationToken.None));

        Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 投稿请求上下文包含空间Referer和非零紧凑交互参数()
    {
        var context = new BiliUploaderRequestContextFactory().Create(85763781, 2, 20);
        using var interaction = System.Text.Json.JsonDocument.Parse(context.Query["dm_img_inter"]);

        Assert.Equal("https://space.bilibili.com/85763781/upload/video", context.Referer);
        Assert.Equal("web", context.Query["platform"]);
        Assert.Equal("true", context.Query["order_avoided"]);
        Assert.Equal("1550101", context.Query["web_location"]);
        Assert.DoesNotContain(" ", context.Query["dm_img_inter"], StringComparison.Ordinal);
        Assert.Contains(
            interaction.RootElement.GetProperty("wh").EnumerateArray().Select(item => item.GetInt32()),
            value => value != 0);
        Assert.Contains(
            interaction.RootElement.GetProperty("of").EnumerateArray().Select(item => item.GetInt32()),
            value => value != 0);
    }

    [Fact]
    public async Task 投稿接口Http403归类为风控且使用实际空间页Referer()
    {
        using var http = new HttpTest();
        http.ForCallsTo("https://api.bilibili.com/x/web-interface/nav")
            .RespondWithJson(new
            {
                code = 0,
                data = new { wbi_img = new
                {
                    img_url = "https://i.test/abcdefghijklmnopqrstuvwxyz123456.png",
                    sub_url = "https://i.test/654321zyxwvutsrqponmlkjihgfedcba.png",
                } },
            });
        http.ForCallsTo("*x/space/wbi/arc/search*").RespondWith("blocked", 403);
        var api = new BiliPersonalContentApi(new BiliApiService());

        var error = await Assert.ThrowsAsync<ContentSourceException>(() =>
            api.GetUploaderVideosAsync(85763781, 20, null, "SESSDATA=x", CancellationToken.None));

        Assert.Equal(ContentSourceErrorCode.RiskControlled, error.Code);
        Assert.Contains("安全风控", error.Message, StringComparison.Ordinal);
        http.ShouldHaveCalled("*x/space/wbi/arc/search*")
            .WithHeader("Referer", "https://space.bilibili.com/85763781/upload/video")
            .WithQueryParam("dm_img_inter")
            .WithQueryParam("w_rid")
            .Times(1);
    }
}

internal sealed class RiskControlledThenSuccessProvider : IContentSourceProvider
{
    public int CallCount { get; private set; }
    public ContentSourceKind Kind => ContentSourceKind.Uploader;
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPaging;
    public int CapabilityVersion => 1;

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ContentSourceDescriptor(Kind, "uploader:42", "UP 主 42", null, 1));

    public Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        if (CallCount == 1)
            throw new ContentSourceException(ContentSourceErrorCode.RiskControlled, "触发 Bilibili 安全风控。");
        return Task.FromResult(new ContentPage(
            [new ContentSourceItem(new(Kind, "aid:1"), "视频", ContentSourceItemType.Video, aid: 1)],
            null,
            false));
    }

    public Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor,
        ContentSourceItem item,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class AuthFailingSecondPageProvider : IContentSourceProvider, IContentSourceResolutionProvider
{
    public ContentSourceKind Kind => ContentSourceKind.History;
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresLogin | ContentSourceCapabilities.SupportsPaging;
    public int CapabilityVersion => 1;

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ContentSourceDescriptor(Kind, "history:42", "历史记录", null, 1));

    public Task<ContentPage> GetPageAsync(ContentSourceDescriptor descriptor, ContentPageRequest request, CancellationToken cancellationToken)
    {
        if (request.ContinuationToken is not null)
            throw new ContentSourceException(ContentSourceErrorCode.LoginRequired, "登录已失效。");
        return Task.FromResult(new ContentPage(
            [new ContentSourceItem(new(Kind, "aid:1"), "视频", ContentSourceItemType.Video, aid: 1)],
            "next", true));
    }

    public Task<BiliVideoCollection> ResolveItemAsync(ContentSourceDescriptor descriptor, ContentSourceItem item, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeAccount(bool loggedIn, long? userId) : IBiliAccountContext
{
    public bool IsLoggedIn => loggedIn;
    public long? UserId => userId;
    public string GetCookieHeader() => loggedIn ? $"SESSDATA=x; DedeUserID={userId}" : string.Empty;
}

internal sealed class FakeItemResolver : IContentSourceItemResolver
{
    public int CallCount { get; private set; }
    public Task<BiliVideoCollection> ResolveAsync(ContentSourceItem item, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(new BiliVideoCollection
        {
            SeriesTitle = item.Title,
            Items = [new BiliVideoItem { Title = item.Title, OriginalTitle = item.Title, Aid = item.Aid ?? 1, Cid = 2, MediaUnitKey = new(item.Aid ?? 1, 2) }],
        });
    }
}

internal sealed class FakeUploaderApi : IBiliUploaderCatalogApi
{
    public BiliCatalogPage Page { get; set; } = new([], null, false);
    public string? LastToken { get; private set; }
    public Task<BiliCatalogPage> GetUploaderVideosAsync(long uploaderId, int pageSize, string? continuationToken, string cookie, CancellationToken cancellationToken)
    { LastToken = continuationToken; return Task.FromResult(Page); }
}

internal sealed class FakeFavoriteApi : IBiliFavoriteCatalogApi
{
    public IReadOnlyList<BiliFavoriteFolder> Folders { get; set; } = [];
    public BiliCatalogPage Page { get; set; } = new([], null, false);
    public long LastMediaId { get; private set; }
    public Task<IReadOnlyList<BiliFavoriteFolder>> GetFavoriteFoldersAsync(long userId, string cookie, CancellationToken cancellationToken) => Task.FromResult(Folders);
    public Task<BiliCatalogPage> GetFavoriteItemsAsync(long mediaId, int pageSize, string? continuationToken, string cookie, CancellationToken cancellationToken)
    { LastMediaId = mediaId; return Task.FromResult(Page); }
}

internal sealed class FakeWatchLaterApi : IBiliWatchLaterCatalogApi
{
    public IReadOnlyList<BiliCatalogItem> Items { get; set; } = [];
    public int CallCount { get; private set; }
    public Task<IReadOnlyList<BiliCatalogItem>> GetWatchLaterAsync(string cookie, CancellationToken cancellationToken)
    { CallCount++; return Task.FromResult(Items); }
}

internal sealed class FakeHistoryApi : IBiliHistoryCatalogApi
{
    public BiliCatalogPage Page { get; set; } = new([], null, false);
    public string? LastToken { get; private set; }
    public Task<BiliCatalogPage> GetHistoryAsync(int pageSize, string? continuationToken, string cookie, CancellationToken cancellationToken)
    { LastToken = continuationToken; return Task.FromResult(Page); }
}
