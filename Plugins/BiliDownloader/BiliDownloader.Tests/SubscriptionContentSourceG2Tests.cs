using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.ViewModels.BiliDownloader;
using Flurl.Http.Testing;
using Newtonsoft.Json;

namespace BiliDownloader.Tests;

public sealed class SubscriptionContentContractG2Tests
{
    [Fact]
    public void 层级模型校验父子来源并支持Json往返()
    {
        var parent = new ContentItemKey(ContentSourceKind.Course, "course:7");
        var item = new ContentSourceItem(
            new(ContentSourceKind.Course, "course:7/ep:9"),
            "课时",
            ContentSourceItemType.Course,
            aid: 1,
            cid: 2,
            parentKey: parent,
            accessState: ContentAccessState.Unknown,
            durationSeconds: 60);

        var copy = JsonConvert.DeserializeObject<ContentSourceItem>(JsonConvert.SerializeObject(item))!;

        Assert.Equal(parent, copy.ParentKey);
        Assert.Equal(ContentAccessState.Unknown, copy.AccessState);
        Assert.Equal(60, copy.DurationSeconds);
        Assert.Throws<ArgumentException>(() => new ContentSourceItem(
            new(ContentSourceKind.Course, "course:7/ep:9"), "课时", ContentSourceItemType.Course,
            parentKey: new(ContentSourceKind.Collection, "collection:7")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContentSourceItem(
            new(ContentSourceKind.Course, "course:7/ep:9"), "课时", ContentSourceItemType.Course,
            durationSeconds: -1));
    }

    [Fact]
    public void 课程目录无需伪造解析能力且注册表正常启动()
    {
        var provider = new CourseSourceProvider(new FakeCourseCatalogApi(), new FakeAccount(true, 42));
        var registry = new ContentSourceProviderRegistry([provider]);

        Assert.True(registry.TryGet(ContentSourceKind.Course, out _));
        Assert.False(registry.TryGetResolutionProvider(ContentSourceKind.Course, out _));
        Assert.Equal(ContentSourceErrorCode.UnsupportedOperation,
            Assert.Throws<ContentSourceException>(() =>
                registry.GetRequiredResolutionProvider(ContentSourceKind.Course)).Code);
    }

    [Fact]
    public void 子分页要求能力位且父键必须完全匹配()
    {
        var provider = new CatalogOnlyProvider();
        var parent = new ContentItemKey(ContentSourceKind.Course, "course:1");
        var child = new ContentSourceItem(
            new(ContentSourceKind.Course, "course:1/ep:1"), "课时", ContentSourceItemType.Course,
            parentKey: parent);

        Assert.Equal(ContentSourceErrorCode.ProtocolViolation,
            Assert.Throws<ContentSourceException>(() => new ContentPageAccumulator().Append(
                provider, new ContentPageRequest(parentKey: parent), new ContentPage([child], null, false))).Code);

        var hierarchical = new CatalogOnlyProvider(ContentSourceCapabilities.SupportsChildPaging);
        var other = new ContentItemKey(ContentSourceKind.Course, "course:2");
        Assert.Equal(ContentSourceErrorCode.ProtocolViolation,
            Assert.Throws<ContentSourceException>(() => new ContentPageAccumulator().Append(
                hierarchical, new ContentPageRequest(parentKey: other), new ContentPage([child], null, false))).Code);

        var wrongKind = new ContentItemKey(ContentSourceKind.Collection, "collection:2");
        Assert.Equal(ContentSourceErrorCode.ProtocolViolation,
            Assert.Throws<ContentSourceException>(() => new ContentPageAccumulator().Append(
                hierarchical, new ContentPageRequest(parentKey: wrongKind), new ContentPage([], null, false))).Code);
    }
}

public sealed class SubscriptionAccessPolicyG2Tests
{
    [Fact]
    public void 番剧权限按Drm区域失效未发布可用未知排序()
    {
        Assert.Equal(ContentAccessState.DrmProtected, BangumiAccessPolicy.Classify(Episode(drm: true, region: true)));
        Assert.Equal(ContentAccessState.RegionRestricted, BangumiAccessPolicy.Classify(Episode(region: true, expired: true)));
        Assert.Equal(ContentAccessState.Expired, BangumiAccessPolicy.Classify(Episode(expired: true, notReleased: true)));
        Assert.Equal(ContentAccessState.NotReleased, BangumiAccessPolicy.Classify(Episode(notReleased: true, available: true)));
        Assert.Equal(ContentAccessState.Available, BangumiAccessPolicy.Classify(Episode(available: true)));
        Assert.Equal(ContentAccessState.Unknown, BangumiAccessPolicy.Classify(Episode()));
        Assert.Equal(ContentAccessState.Expired, BangumiAccessPolicy.Classify(Episode(stable: false, available: true)));
    }

    [Fact]
    public void 课程未知字段默认阻断且购买判断晚于未发布()
    {
        var unpurchased = new BiliCourseDetail(7, "课程", null, null, false, false);
        var purchased = unpurchased with { IsPurchased = true };

        Assert.Equal(ContentAccessState.DrmProtected,
            CourseAccessPolicy.Classify(unpurchased, CourseEpisode(drm: true, region: true)));
        Assert.Equal(ContentAccessState.RegionRestricted,
            CourseAccessPolicy.Classify(unpurchased, CourseEpisode(region: true, access: 2)));
        Assert.Equal(ContentAccessState.Expired,
            CourseAccessPolicy.Classify(unpurchased with { IsExpired = true }, CourseEpisode(notReleased: true)));
        Assert.Equal(ContentAccessState.NotReleased,
            CourseAccessPolicy.Classify(unpurchased, CourseEpisode(notReleased: true, access: 2)));
        Assert.Equal(ContentAccessState.PurchaseRequired,
            CourseAccessPolicy.Classify(unpurchased, CourseEpisode(access: 2)));
        Assert.Equal(ContentAccessState.Available,
            CourseAccessPolicy.Classify(purchased, CourseEpisode(access: 1)));
        Assert.Equal(ContentAccessState.Unknown,
            CourseAccessPolicy.Classify(purchased, CourseEpisode()));
    }

    private static BiliBangumiEpisode Episode(
        bool drm = false,
        bool region = false,
        bool expired = false,
        bool notReleased = false,
        bool available = false,
        bool stable = true) =>
        new(1, "BV1abcDEF123", 2, 3, 4, "分集", 60, null,
            stable, drm, region, expired, notReleased, available);

    private static BiliCourseEpisode CourseEpisode(
        bool drm = false,
        bool region = false,
        bool notReleased = false,
        int? access = null) =>
        new(1, 2, 3, "课时", 60,
            notReleased ? DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds() : 0,
            null, access, drm, region, notReleased ? false : true);
}

public sealed class SubscriptionProviderG2Tests
{
    [Fact]
    public async Task 追剧Provider使用影视分类且未登录在规范化边界阻断()
    {
        var api = new FakeFollowingCatalogApi();
        var loggedOut = new FollowingCinemaSourceProvider(api, new FakeAccount(false, null), new());
        var login = await Assert.ThrowsAsync<ContentSourceException>(async () =>
            await loggedOut.NormalizeAsync("self", CancellationToken.None));
        Assert.Equal(ContentSourceErrorCode.LoginRequired, login.Code);

        var provider = new FollowingCinemaSourceProvider(api, new FakeAccount(true, 42), new());
        var descriptor = await provider.NormalizeAsync("self", CancellationToken.None);
        await provider.GetPageAsync(descriptor, new ContentPageRequest(), CancellationToken.None);

        Assert.True(api.LastCinema);
        Assert.Equal(ContentSourceKind.FollowingCinema, descriptor.Kind);
    }

    [Fact]
    public async Task 追番根节点和子节点使用稳定层级键并解析为番剧媒体()
    {
        var api = new FakeFollowingCatalogApi
        {
            Following = new([new(7, 8, "番剧", null, 2, true, true)], null, false),
            Detail = new(7, "番剧", null,
            [
                BangumiEpisode(11, 21, 31),
                BangumiEpisode(12, 22, 32),
            ]),
        };
        var provider = new FollowingBangumiSourceProvider(api, new FakeAccount(true, 42), new());
        var descriptor = await provider.NormalizeAsync("self", CancellationToken.None);

        var root = await provider.GetPageAsync(descriptor, new ContentPageRequest(), CancellationToken.None);
        var container = Assert.Single(root.Items);
        var children = await provider.GetPageAsync(descriptor,
            new ContentPageRequest(1, parentKey: container.Key), CancellationToken.None);
        var child = Assert.Single(children.Items);
        var collection = await provider.ResolveItemAsync(descriptor, child, CancellationToken.None);

        Assert.Equal("season:7", container.Key.NativeId);
        Assert.Equal(ContentSourceNodeKind.Container, container.NodeKind);
        Assert.Equal("season:7/ep:31", child.Key.NativeId);
        Assert.Equal(container.Key, child.ParentKey);
        Assert.Equal(new MediaUnitKey(11, 21), Assert.Single(collection.Items).MediaUnitKey);
        Assert.Equal(BiliMediaType.Bangumi, collection.Items[0].MediaType);
        Assert.True(children.HasMore);
    }

    [Fact]
    public async Task 追番子游标拒绝跨父集合且快照不受远端顺序变化影响()
    {
        var api = new FakeFollowingCatalogApi
        {
            Detail = new(7, "番剧", null,
            [BangumiEpisode(1, 11, 101), BangumiEpisode(2, 22, 102)]),
        };
        var provider = new FollowingBangumiSourceProvider(api, new FakeAccount(true, 42), new());
        var descriptor = await provider.NormalizeAsync("self", CancellationToken.None);
        var parent = new ContentItemKey(ContentSourceKind.FollowingBangumi, "season:7");
        var first = await provider.GetPageAsync(descriptor,
            new ContentPageRequest(1, parentKey: parent), CancellationToken.None);
        api.Detail = api.Detail with { Episodes = api.Detail.Episodes.Reverse().ToArray() };
        var second = await provider.GetPageAsync(descriptor,
            new ContentPageRequest(1, first.NextContinuationToken, parentKey: parent), CancellationToken.None);

        Assert.Equal(102, Assert.Single(second.Items).EpId);
        Assert.Equal(1, api.SeasonCallCount);
        var otherParent = new ContentItemKey(ContentSourceKind.FollowingBangumi, "season:8");
        var error = await Assert.ThrowsAsync<ContentSourceException>(() => provider.GetPageAsync(descriptor,
            new ContentPageRequest(1, first.NextContinuationToken, parentKey: otherParent), CancellationToken.None));
        Assert.Equal(ContentSourceErrorCode.ProtocolViolation, error.Code);
    }

    [Fact]
    public async Task 同一视频在不同订阅合集来源键不同但媒体键相同()
    {
        var api = new FakeCollectedFolderApi
        {
            FolderItems = new([new(9, "BV1abcDEF123", "视频", "UP", 0, null)], null, false),
        };
        var resolver = new FakeItemResolver();
        var provider = new CollectionSourceProvider(api, new FakeAccount(true, 42), resolver);
        var descriptor = await provider.NormalizeAsync("self", CancellationToken.None);
        var parent1 = new ContentItemKey(ContentSourceKind.Collection, "collection:1");
        var parent2 = new ContentItemKey(ContentSourceKind.Collection, "collection:2");

        var item1 = Assert.Single((await provider.GetPageAsync(descriptor,
            new ContentPageRequest(parentKey: parent1), CancellationToken.None)).Items);
        var item2 = Assert.Single((await provider.GetPageAsync(descriptor,
            new ContentPageRequest(parentKey: parent2), CancellationToken.None)).Items);
        var media1 = Assert.Single((await provider.ResolveItemAsync(descriptor, item1, CancellationToken.None)).Items).MediaUnitKey;
        var media2 = Assert.Single((await provider.ResolveItemAsync(descriptor, item2, CancellationToken.None)).Items).MediaUnitKey;

        Assert.NotEqual(item1.Key, item2.Key);
        Assert.Equal(media1, media2);
    }

    [Fact]
    public async Task 订阅合集根节点映射失效状态且无身份子项不可解析()
    {
        var api = new FakeCollectedFolderApi
        {
            Folders = new([new(7, "失效合集", "UP", null, 1, true)], null, false),
            FolderItems = new([new(0, "", "无身份", null, 0, null)], null, false),
        };
        var provider = new CollectionSourceProvider(api, new FakeAccount(true, 42), new FakeItemResolver());
        var descriptor = await provider.NormalizeAsync("self", CancellationToken.None);
        var root = Assert.Single((await provider.GetPageAsync(descriptor, new ContentPageRequest(), CancellationToken.None)).Items);
        var child = Assert.Single((await provider.GetPageAsync(descriptor,
            new ContentPageRequest(parentKey: root.Key), CancellationToken.None)).Items);

        Assert.Equal(ContentAccessState.Expired, root.AccessState);
        Assert.Equal(ContentAccessState.Expired, child.AccessState);
        Assert.Equal(ContentSourceErrorCode.ProtocolViolation,
            (await Assert.ThrowsAsync<ContentSourceException>(() =>
                provider.ResolveItemAsync(descriptor, child, CancellationToken.None))).Code);
    }

    [Theory]
    [InlineData("https://www.bilibili.com/cheese/play/ss7", "course-direct:7")]
    [InlineData("https://www.bilibili.com/cheese/play/ep9", "course-direct:7")]
    [InlineData("7", "course-direct:7")]
    public async Task 课程SsEp和数字ID规范化为稳定课程身份(string input, string expected)
    {
        var api = new FakeCourseCatalogApi { Detail = new(7, "课程", null, "讲师", true, false) };
        var provider = new CourseSourceProvider(api, new FakeAccount(false, null));

        var descriptor = await provider.NormalizeAsync(input, CancellationToken.None);

        Assert.Equal(expected, descriptor.StableSourceId);
        Assert.Equal("true", descriptor.PublicParameters["autoOpen"]);
    }

    [Fact]
    public async Task 课程目录展示权限但所有课时保持只读()
    {
        var api = new FakeCourseCatalogApi
        {
            Detail = new(7, "课程", null, "讲师", false, false),
            Episodes = new(
            [
                new(1, 2, 10, "免费", 60, 0, null, 1, false, false, true),
                new(3, 4, 11, "未购买", 60, 0, null, 2, false, false, true),
                new(5, 6, 12, "字段未知", 60, 0, null, null, null, null, null),
            ], null, false),
        };
        var provider = new CourseSourceProvider(api, new FakeAccount(true, 42));
        var descriptor = await provider.NormalizeAsync("self", CancellationToken.None);
        var parent = new ContentItemKey(ContentSourceKind.Course, "course:7");

        var page = await provider.GetPageAsync(descriptor,
            new ContentPageRequest(parentKey: parent), CancellationToken.None);

        Assert.Equal(
            [ContentAccessState.Available, ContentAccessState.PurchaseRequired, ContentAccessState.Unknown],
            page.Items.Select(item => item.AccessState));
        Assert.All(page.Items, item => Assert.Equal(parent, item.ParentKey));
        Assert.False((object)provider is IContentSourceResolutionProvider);
    }

    [Fact]
    public async Task 我的课程根页透传分页且直接课程根页只返回目标课程()
    {
        var api = new FakeCourseCatalogApi
        {
            Courses = new([new(7, "我的课程", null, 3, false)], "next", true),
            Detail = new(9, "直接课程", null, null, true, false),
        };
        var provider = new CourseSourceProvider(api, new FakeAccount(true, 42));
        var library = await provider.NormalizeAsync("self", CancellationToken.None);
        var libraryPage = await provider.GetPageAsync(library,
            new ContentPageRequest(20, "current"), CancellationToken.None);
        var direct = await provider.NormalizeAsync("ss9", CancellationToken.None);
        var directPage = await provider.GetPageAsync(direct, new ContentPageRequest(), CancellationToken.None);

        Assert.True(libraryPage.HasMore);
        Assert.Equal("current", api.LastCourseToken);
        Assert.Equal("course:9", Assert.Single(directPage.Items).Key.NativeId);
        Assert.False(directPage.HasMore);
    }

    private static BiliBangumiEpisode BangumiEpisode(long aid, long cid, long epId) =>
        new(aid, $"BV{aid:0000000000}", cid, epId, 7, $"第 {epId} 话", 60, null,
            true, false, false, false, false, true);
}

public sealed class SubscriptionContentApiG2Tests
{
    [Fact]
    public async Task 追番Fixture传递类型并按项目总数分页()
    {
        using var http = new HttpTest();
        http.RespondWithJson(new
        {
            code = 0,
            data = new
            {
                total = 21,
                list = new[] { new { season_id = 7, media_id = 8, title = "番剧", total_count = 12, is_started = 1, is_play = 1 } },
            },
        });
        var api = new BiliSubscriptionContentApi(new FakeFavoriteApi());

        var page = await api.GetFollowingAsync(42, false, 20, null, "SESSDATA=x", CancellationToken.None);

        Assert.True(page.HasMore);
        Assert.Equal(7, Assert.Single(page.Items).SeasonId);
        http.ShouldHaveCalled("*x/space/bangumi/follow/list*")
            .WithQueryParam("type", "1")
            .WithQueryParam("vmid", "42")
            .Times(1);
    }

    [Fact]
    public async Task 番剧详情Fixture映射公开身份和权限字段()
    {
        using var http = new HttpTest();
        http.RespondWithJson(new
        {
            code = 0,
            result = new
            {
                season_id = 7,
                season_title = "番剧",
                episodes = new[]
                {
                    new { aid = 1, bvid = "BV1abcDEF123", cid = 2, ep_id = 3, long_title = "第一话", duration = 60000, status = 2, rights = new { allow_demand = 1, area_limit = 0, is_drm = 0 } },
                },
            },
        });
        var api = new BiliSubscriptionContentApi(new FakeFavoriteApi());

        var episode = Assert.Single((await api.GetSeasonAsync(7, "", CancellationToken.None)).Episodes);

        Assert.True(episode.HasStableIdentity);
        Assert.True(episode.IsExplicitlyAvailable);
        Assert.Equal(60, episode.DurationSeconds);
    }

    [Fact]
    public async Task 订阅合集Fixture映射根分页并复用收藏夹子列表适配器()
    {
        using var http = new HttpTest();
        http.ForCallsTo("*folder/collected/list*").RespondWithJson(new
        {
            code = 0,
            data = new
            {
                count = 21,
                list = new[] { new { id = 7, title = "合集", media_count = 3, upper = new { name = "UP" }, state = 0 } },
            },
        });
        var favorite = new FakeFavoriteApi
        {
            Page = new BiliCatalogPage([new(9, "BV1abcDEF123", "视频", "UP", 0, null)], null, false),
        };
        var api = new BiliSubscriptionContentApi(favorite);

        var root = await api.GetCollectedFoldersAsync(42, 20, null, "", CancellationToken.None);
        var child = await api.GetFolderItemsAsync(7, 20, null, "", CancellationToken.None);

        Assert.True(root.HasMore);
        Assert.Equal("UP", Assert.Single(root.Items).OwnerName);
        Assert.Equal(9, Assert.Single(child.Items).Aid);
        Assert.Equal(7, favorite.LastMediaId);
    }

    [Fact]
    public async Task 课程详情和课时Fixture只映射权限所需字段()
    {
        using var http = new HttpTest();
        http.ForCallsTo("*pugv/view/web/season*").RespondWithJson(new
        {
            code = 0,
            data = new { season_id = 7, title = "课程", user_status = new { payed = true }, up_info = new { uname = "讲师" }, price = 999 },
        });
        http.ForCallsTo("*pugv/view/web/ep/list*").RespondWithJson(new
        {
            code = 0,
            data = new
            {
                items = new[] { new { aid = 1, cid = 2, id = 3, title = "课时", duration = 60, status = 1, is_drm = 0, area_limit = 0, is_release = 1 } },
                page = new { total = 21 },
            },
        });
        var api = new BiliSubscriptionContentApi(new FakeFavoriteApi());

        var detail = await api.GetCourseAsync(7, null, "", CancellationToken.None);
        var page = await api.GetCourseEpisodesAsync(7, 20, null, "", CancellationToken.None);

        Assert.True(detail.IsPurchased);
        Assert.Equal("讲师", detail.Author);
        Assert.True(page.HasMore);
        Assert.Equal(3, Assert.Single(page.Items).EpisodeId);
        Assert.DoesNotContain("price", JsonConvert.SerializeObject(detail), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 字段缺失和空数据安全收敛为空末页()
    {
        using var http = new HttpTest();
        http.RespondWithJson(new { code = 0, data = new { total = 99, list = new[] { new { title = "缺少身份" } } } });
        var api = new BiliSubscriptionContentApi(new FakeFavoriteApi());

        var page = await api.GetFollowingAsync(42, false, 20, null, "", CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task 已购课程Fixture把Total解释为总页数并忽略价格订单字段()
    {
        using var http = new HttpTest();
        http.RespondWithJson(new
        {
            code = 0,
            data = new
            {
                total = 2,
                data = new[] { new { season_id = 7, title = "课程", ep_count = 3, price = 999, order_id = "secret-order" } },
            },
        });
        var api = new BiliSubscriptionContentApi(new FakeFavoriteApi());

        var page = await api.GetMyCoursesAsync(20, null, "SESSDATA=x", CancellationToken.None);

        Assert.True(page.HasMore);
        Assert.Equal(7, Assert.Single(page.Items).SeasonId);
        Assert.DoesNotContain("order", JsonConvert.SerializeObject(page), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("price", JsonConvert.SerializeObject(page), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-101, ContentSourceErrorCode.LoginRequired)]
    [InlineData(-403, ContentSourceErrorCode.Forbidden)]
    [InlineData(-404, ContentSourceErrorCode.NotFound)]
    [InlineData(53013, ContentSourceErrorCode.Forbidden)]
    [InlineData(-412, ContentSourceErrorCode.RiskControlled)]
    [InlineData(-401, ContentSourceErrorCode.RiskControlled)]
    [InlineData(-352, ContentSourceErrorCode.RiskControlled)]
    [InlineData(-500, ContentSourceErrorCode.RemoteFailure)]
    public async Task 远端码映射为稳定错误且不泄露响应正文(int code, ContentSourceErrorCode expected)
    {
        using var http = new HttpTest();
        http.RespondWithJson(new { code, message = "Cookie SESSDATA=secret; order=secret" });
        var api = new BiliSubscriptionContentApi(new FakeFavoriteApi());

        var error = await Assert.ThrowsAsync<ContentSourceException>(() =>
            api.GetCollectedFoldersAsync(42, 20, null, "SESSDATA=local-secret", CancellationToken.None));

        Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(401, ContentSourceErrorCode.LoginRequired)]
    [InlineData(403, ContentSourceErrorCode.Forbidden)]
    [InlineData(404, ContentSourceErrorCode.NotFound)]
    [InlineData(412, ContentSourceErrorCode.RiskControlled)]
    [InlineData(429, ContentSourceErrorCode.RateLimited)]
    public async Task Http错误映射不包含Cookie和响应正文(int status, ContentSourceErrorCode expected)
    {
        using var http = new HttpTest();
        http.RespondWith("Cookie: SESSDATA=remote-secret", status);
        var api = new BiliSubscriptionContentApi(new FakeFavoriteApi());

        var error = await Assert.ThrowsAsync<ContentSourceException>(() =>
            api.GetFollowingAsync(42, false, 20, null, "SESSDATA=local-secret", CancellationToken.None));

        Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SubscriptionContentUiG2Tests
{
    [Fact]
    public void 来源选择包含八类且课程同时支持链接和账号入口()
    {
        var registry = new ContentSourceProviderRegistry([new CourseSourceProvider(new FakeCourseCatalogApi(), new FakeAccount(true, 42))]);
        var picker = new ContentSourcePickerViewModel(registry, new EmptyFavoriteDiscovery(), _ => Task.CompletedTask);

        Assert.Equal(8, picker.Options.Count);
        var course = Assert.Single(picker.Options, option => option.Kind == ContentSourceKind.Course);
        Assert.True(course.SupportsManualInput);
        Assert.True(course.SupportsAccountShortcut);
        Assert.Contains("ss/ep", course.InputPlaceholder);
    }

    [Fact]
    public async Task 面包屑返回时恢复根层项目且不重复请求()
    {
        var api = new FakeFollowingCatalogApi
        {
            Following = new([new(7, 8, "番剧", null, 1, true, true)], null, false),
            Detail = new(7, "番剧", null, [new(1, "BV1abcDEF123", 2, 3, 7, "第一话", 60, null, true, false, false, false, false, true)]),
        };
        var provider = new FollowingBangumiSourceProvider(api, new FakeAccount(true, 42), new());
        var browser = Browser(new ContentSourceProviderRegistry([provider]));
        var descriptor = await provider.NormalizeAsync("self", CancellationToken.None);
        await browser.OpenAsync(descriptor);

        await browser.Items[0].OpenCommand.ExecuteAsync(null);
        Assert.Equal("番剧", browser.Title);
        Assert.Single(browser.Items).IsSelected = true;
        await browser.Breadcrumbs[0].NavigateCommand.ExecuteAsync(null);

        Assert.Equal("我的追番", browser.Title);
        Assert.Single(browser.Items);
        Assert.Equal(1, api.FollowingCallCount);
        Assert.False(browser.CanGoBack);
    }

    [Fact]
    public async Task 课程页面隐藏解析能力且可用课时仍不可勾选()
    {
        var api = new FakeCourseCatalogApi
        {
            Courses = new([new(7, "课程", null, 1, false)], null, false),
            Detail = new(7, "课程", null, null, true, false),
            Episodes = new([new(1, 2, 3, "课时", 60, 0, null, 1, false, false, true)], null, false),
        };
        var provider = new CourseSourceProvider(api, new FakeAccount(true, 42));
        var browser = Browser(new ContentSourceProviderRegistry([provider]));
        var descriptor = await provider.NormalizeAsync("self", CancellationToken.None);
        await browser.OpenAsync(descriptor);
        await browser.Items[0].OpenCommand.ExecuteAsync(null);

        var item = Assert.Single(browser.Items);
        Assert.True(browser.IsReadOnlySource);
        Assert.False(browser.CanResolveCurrentSource);
        Assert.False(item.CanSelect);
        Assert.False(item.ShowCheckBox);
        Assert.Contains("仅支持浏览", browser.ReadOnlyMessage);
    }

    private static ContentSourceBrowserViewModel Browser(IContentSourceProviderRegistry registry) =>
        new(registry, new VideoParseResultFactory(new StubMediaProbe(), new StubCredentials("")), _ => { });
}

internal sealed class FakeFollowingCatalogApi : IBiliFollowingCatalogApi
{
    public BiliFollowingPage Following { get; set; } = new([], null, false);
    public BiliBangumiSeasonDetail Detail { get; set; } = new(7, "番剧", null, []);
    public int FollowingCallCount { get; private set; }
    public int SeasonCallCount { get; private set; }
    public bool LastCinema { get; private set; }

    public Task<BiliFollowingPage> GetFollowingAsync(long userId, bool cinema, int pageSize, string? continuationToken, string cookie, CancellationToken cancellationToken)
    { FollowingCallCount++; LastCinema = cinema; return Task.FromResult(Following); }

    public Task<BiliBangumiSeasonDetail> GetSeasonAsync(long seasonId, string cookie, CancellationToken cancellationToken)
    { SeasonCallCount++; return Task.FromResult(Detail); }
}

internal sealed class FakeCollectedFolderApi : IBiliCollectedFolderApi
{
    public BiliCollectedFolderPage Folders { get; set; } = new([], null, false);
    public BiliCatalogPage FolderItems { get; set; } = new([], null, false);

    public Task<BiliCollectedFolderPage> GetCollectedFoldersAsync(long userId, int pageSize, string? continuationToken, string cookie, CancellationToken cancellationToken) => Task.FromResult(Folders);
    public Task<BiliCatalogPage> GetFolderItemsAsync(long mediaId, int pageSize, string? continuationToken, string cookie, CancellationToken cancellationToken) => Task.FromResult(FolderItems);
}

internal sealed class FakeCourseCatalogApi : IBiliCourseCatalogApi
{
    public BiliCoursePage Courses { get; set; } = new([], null, false);
    public BiliCourseDetail Detail { get; set; } = new(7, "课程", null, null, true, false);
    public BiliCourseEpisodePage Episodes { get; set; } = new([], null, false);
    public string? LastCourseToken { get; private set; }

    public Task<BiliCoursePage> GetMyCoursesAsync(int pageSize, string? continuationToken, string cookie, CancellationToken cancellationToken)
    { LastCourseToken = continuationToken; return Task.FromResult(Courses); }
    public Task<BiliCourseDetail> GetCourseAsync(long? seasonId, long? episodeId, string cookie, CancellationToken cancellationToken) => Task.FromResult(Detail);
    public Task<BiliCourseEpisodePage> GetCourseEpisodesAsync(long seasonId, int pageSize, string? continuationToken, string cookie, CancellationToken cancellationToken) => Task.FromResult(Episodes);
}

internal sealed class CatalogOnlyProvider(ContentSourceCapabilities capabilities = ContentSourceCapabilities.None) : IContentSourceProvider
{
    public ContentSourceKind Kind => ContentSourceKind.Course;
    public ContentSourceCapabilities Capabilities => capabilities;
    public int CapabilityVersion => 1;
    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ContentSourceDescriptor(Kind, "course:test", "测试", null, 1));
    public Task<ContentPage> GetPageAsync(ContentSourceDescriptor descriptor, ContentPageRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new ContentPage([], null, false));
}

internal sealed class EmptyFavoriteDiscovery : IFavoriteSourceDiscoveryService
{
    public Task<IReadOnlyList<ContentSourceDescriptor>> GetMyFoldersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ContentSourceDescriptor>>([]);
}
