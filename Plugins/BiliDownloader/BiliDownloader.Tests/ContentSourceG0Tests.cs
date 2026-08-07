using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Plugin;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.ViewModels.BiliDownloader;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace BiliDownloader.Tests;

public sealed class ContentSourceModelTests
{
    [Fact]
    public void 来源枚举和能力位覆盖P1公共契约()
    {
        Assert.Equal(9, Enum.GetValues<ContentSourceKind>().Length);
        Assert.Equal(7, Enum.GetValues<ContentSourceCapabilities>().Length - 1);

        var capabilities = ContentSourceCapabilities.RequiresLogin |
                           ContentSourceCapabilities.SupportsPaging |
                           ContentSourceCapabilities.SupportsIncremental;
        Assert.True(capabilities.HasFlag(ContentSourceCapabilities.RequiresLogin));
        Assert.True(capabilities.HasFlag(ContentSourceCapabilities.SupportsPaging));
        Assert.False(capabilities.HasFlag(ContentSourceCapabilities.SupportsKeyword));
    }

    [Fact]
    public void 描述符防御性复制公开参数并验证能力版本()
    {
        var parameters = new Dictionary<string, string> { ["folder"] = "1" };
        var descriptor = new ContentSourceDescriptor(
            ContentSourceKind.Favorite, "favorite:1", "收藏夹", parameters, 1);
        parameters["folder"] = "changed";

        Assert.Equal("1", descriptor.PublicParameters["folder"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContentSourceDescriptor(
            ContentSourceKind.DirectLink, "video:av:1", "av1", null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContentSourceDescriptor(
            (ContentSourceKind)999, "id", "name", null, 1));
        Assert.Throws<ArgumentException>(() => new ContentSourceDescriptor(
            ContentSourceKind.DirectLink, " ", "name", null, 1));
        Assert.Throws<ArgumentException>(() => new ContentSourceDescriptor(
            ContentSourceKind.DirectLink, "id", " ", null, 1));
    }

    [Fact]
    public void 稳定键保持Bvid载荷大小写并支持Json往返与哈希相等()
    {
        var first = new ContentItemKey(ContentSourceKind.DirectLink, "video:bv:1abcDEF123");
        var same = JsonConvert.DeserializeObject<ContentItemKey>(JsonConvert.SerializeObject(first));
        var differentCase = new ContentItemKey(ContentSourceKind.DirectLink, "video:bv:1ABCdef123");

        Assert.Equal(first, same);
        Assert.NotEqual(first, differentCase);
        Assert.Single(new HashSet<ContentItemKey> { first, same });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContentItemKey((ContentSourceKind)999, "id"));
        Assert.Throws<ArgumentException>(() => new ContentItemKey(ContentSourceKind.DirectLink, " "));
    }

    [Fact]
    public void 媒体键只接受正数并支持Json往返()
    {
        var key = new MediaUnitKey(123, 456);
        var restored = JsonConvert.DeserializeObject<MediaUnitKey>(JsonConvert.SerializeObject(key));

        Assert.Equal(key, restored);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaUnitKey(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaUnitKey(1, 0));
    }

    [Fact]
    public void 分页请求验证边界并原样保存游标()
    {
        const string token = " opaque token +/% ";
        var min = new ContentPageRequest(1, token);
        var max = new ContentPageRequest(100);
        var restored = JsonConvert.DeserializeObject<ContentPageRequest>(JsonConvert.SerializeObject(min));

        Assert.Equal(token, min.ContinuationToken);
        Assert.Equal(token, restored!.ContinuationToken);
        Assert.Equal(100, max.PageSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContentPageRequest(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContentPageRequest(101));
    }

    [Fact]
    public void 分页结果拒绝HasMore与游标矛盾()
    {
        Assert.Throws<ArgumentException>(() => new ContentPage([], null, true));
        Assert.Throws<ArgumentException>(() => new ContentPage([], "unexpected", false));

        var page = new ContentPage([], "next secret", true, "snapshot secret");
        var restored = JsonConvert.DeserializeObject<ContentPage>(JsonConvert.SerializeObject(page));
        Assert.Equal("next secret", restored!.NextContinuationToken);
        Assert.Equal("snapshot secret", restored.SnapshotToken);
    }

    [Fact]
    public void 描述符内容项和分页结果支持Json往返()
    {
        var descriptor = new ContentSourceDescriptor(
            ContentSourceKind.DirectLink,
            "video:av:1",
            "av1",
            new Dictionary<string, string>(),
            1);
        var item = new ContentSourceItem(
            new ContentItemKey(ContentSourceKind.DirectLink, "video:av:1"),
            "视频",
            ContentSourceItemType.Video,
            author: "作者",
            publishedAt: DateTimeOffset.Parse("2026-08-05T00:00:00Z"),
            aid: 1,
            cid: 2);
        var page = new ContentPage([item], null, false, "snapshot");

        var restoredDescriptor = JsonConvert.DeserializeObject<ContentSourceDescriptor>(
            JsonConvert.SerializeObject(descriptor));
        var restoredPage = JsonConvert.DeserializeObject<ContentPage>(JsonConvert.SerializeObject(page));

        Assert.Equal(descriptor.StableSourceId, restoredDescriptor!.StableSourceId);
        Assert.Equal(descriptor.CapabilityVersion, restoredDescriptor.CapabilityVersion);
        Assert.Equal(item.Key, Assert.Single(restoredPage!.Items).Key);
        Assert.Equal("snapshot", restoredPage.SnapshotToken);
    }

    [Fact]
    public void 筛选规则验证日期并防御性复制媒体类型()
    {
        var mediaTypes = new List<ContentSourceItemType> { ContentSourceItemType.Video };
        var rules = new SourceFilterRules(
            "  keyword  ",
            DateTimeOffset.Parse("2026-01-01"),
            DateTimeOffset.Parse("2026-02-01"),
            mediaTypes,
            ContentSourceSortOrder.PublishedNewest);
        mediaTypes.Add(ContentSourceItemType.Bangumi);

        Assert.Equal("keyword", rules.Keyword);
        Assert.Single(rules.MediaTypes);
        Assert.Throws<ArgumentException>(() => new SourceFilterRules(
            publishedFrom: DateTimeOffset.Parse("2026-02-01"),
            publishedTo: DateTimeOffset.Parse("2026-01-01")));
        _ = new SourceFilterRules(publishedFrom: DateTimeOffset.UtcNow);
        _ = new SourceFilterRules(publishedTo: DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceFilterRules(
            sortOrder: (ContentSourceSortOrder)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceFilterRules(
            mediaTypes: [(ContentSourceItemType)999]));
    }

    [Fact]
    public void 内容项验证标题类型并覆盖可选封面和空页输入()
    {
        var key = new ContentItemKey(ContentSourceKind.DirectLink, "video:av:1");
        Assert.Throws<ArgumentException>(() =>
            new ContentSourceItem(key, " ", ContentSourceItemType.Video));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ContentSourceItem(key, "标题", (ContentSourceItemType)999));

        var item = new ContentSourceItem(
            key,
            "标题",
            ContentSourceItemType.Video,
            coverSummary: " https://image.test/cover.jpg ");
        var empty = new ContentPage(null, null, false);

        Assert.Equal("https://image.test/cover.jpg", item.CoverSummary);
        Assert.Empty(empty.Items);
    }
}

public sealed class ContentSourceRegistryAndPagingTests
{
    [Fact]
    public void 注册表支持查找并拒绝重复和未知来源()
    {
        var provider = new StubProvider(ContentSourceKind.DirectLink);
        var registry = new ContentSourceProviderRegistry([provider]);

        Assert.True(registry.TryGet(ContentSourceKind.DirectLink, out var found));
        Assert.Same(provider, found);
        Assert.Same(provider, registry.GetRequired(ContentSourceKind.DirectLink));
        Assert.Equal(ContentSourceErrorCode.UnknownProvider,
            Assert.Throws<ContentSourceException>(() => registry.GetRequired(ContentSourceKind.Uploader)).Code);
        Assert.Equal(ContentSourceErrorCode.ProtocolViolation,
            Assert.Throws<ContentSourceException>(() =>
                new ContentSourceProviderRegistry([provider, new StubProvider(ContentSourceKind.DirectLink)])).Code);
        Assert.False(registry.TryGet((ContentSourceKind)999, out var invalid));
        Assert.Null(invalid);
    }

    [Fact]
    public void 注册表拒绝非法能力版本和未知能力位()
    {
        Assert.Throws<ContentSourceException>(() => new ContentSourceProviderRegistry(
            [new StubProvider(ContentSourceKind.DirectLink, capabilityVersion: 0)]));
        Assert.Throws<ContentSourceException>(() => new ContentSourceProviderRegistry(
            [new StubProvider(ContentSourceKind.DirectLink, (ContentSourceCapabilities)(1 << 20))]));
        Assert.Throws<ContentSourceException>(() => new ContentSourceProviderRegistry(
            [new StubProvider((ContentSourceKind)999)]));
    }

    [Fact]
    public void 累加器跨页去重并保持首次顺序()
    {
        var provider = new StubProvider(
            ContentSourceKind.Uploader,
            ContentSourceCapabilities.SupportsPaging);
        var accumulator = new ContentPageAccumulator();
        var first = Item(ContentSourceKind.Uploader, "1");
        var second = Item(ContentSourceKind.Uploader, "2");
        var third = Item(ContentSourceKind.Uploader, "3");

        var firstAdded = accumulator.Append(
            provider,
            new ContentPageRequest(),
            new ContentPage([first, second, first], "page-2", true));
        var secondAdded = accumulator.Append(
            provider,
            new ContentPageRequest(continuationToken: "page-2"),
            new ContentPage([second, third], null, false));

        Assert.Equal(["1", "2"], firstAdded.Select(x => x.Key.NativeId));
        Assert.Equal(["3"], secondAdded.Select(x => x.Key.NativeId));
        Assert.Equal(["1", "2", "3"], accumulator.Items.Select(x => x.Key.NativeId));
    }

    [Fact]
    public void 重复游标有新增项时允许继续_无新增项时终止()
    {
        var provider = new StubProvider(
            ContentSourceKind.History,
            ContentSourceCapabilities.SupportsPaging);
        var accumulator = new ContentPageAccumulator();
        var request = new ContentPageRequest(continuationToken: "same-token");

        var added = accumulator.Append(
            provider,
            request,
            new ContentPage([Item(ContentSourceKind.History, "1")], "same-token", true));
        Assert.Single(added);

        var error = Assert.Throws<ContentSourceException>(() => accumulator.Append(
            provider,
            request,
            new ContentPage([Item(ContentSourceKind.History, "1")], "same-token", true)));
        Assert.Equal(ContentSourceErrorCode.ProtocolViolation, error.Code);
        Assert.DoesNotContain("same-token", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 累加器拒绝来源类型错误和非分页Provider的下一页()
    {
        var direct = new StubProvider(ContentSourceKind.DirectLink);
        var accumulator = new ContentPageAccumulator();

        Assert.Throws<ContentSourceException>(() => accumulator.Append(
            direct,
            new ContentPageRequest(),
            new ContentPage([Item(ContentSourceKind.Uploader, "1")], null, false)));
        Assert.Throws<ContentSourceException>(() => accumulator.Append(
            direct,
            new ContentPageRequest(),
            new ContentPage([], "next", true)));
    }

    private static ContentSourceItem Item(ContentSourceKind kind, string id) =>
        new(new ContentItemKey(kind, id), "项目" + id, ContentSourceItemType.Video);
}

/// <summary>
/// Provider 通用契约测试基类。后续来源只需提供一个离线实例和合法描述符即可复用。
/// </summary>
public abstract class ContentSourceProviderContractTests
{
    protected abstract IContentSourceProvider CreateProvider();
    protected abstract ValueTask<ContentSourceDescriptor> CreateDescriptorAsync(IContentSourceProvider provider);

    [Fact]
    public async Task Provider声明与描述符一致()
    {
        var provider = CreateProvider();
        var descriptor = await CreateDescriptorAsync(provider);

        Assert.True(Enum.IsDefined(provider.Kind));
        Assert.True(provider.CapabilityVersion > 0);
        Assert.Equal(provider.Kind, descriptor.Kind);
        Assert.Equal(provider.CapabilityVersion, descriptor.CapabilityVersion);
    }

    [Fact]
    public async Task Provider末页契约一致且项目来源正确()
    {
        var provider = CreateProvider();
        var descriptor = await CreateDescriptorAsync(provider);
        var page = await provider.GetPageAsync(descriptor, new ContentPageRequest(), CancellationToken.None);

        Assert.False(page.HasMore);
        Assert.Null(page.NextContinuationToken);
        Assert.All(page.Items, item => Assert.Equal(provider.Kind, item.Key.SourceKind));
    }

    [Fact]
    public async Task Provider在调用入口传播取消()
    {
        var provider = CreateProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await provider.NormalizeAsync("BV1abcDEF123", cts.Token));
    }
}

public sealed class DirectLinkProviderContractTests : ContentSourceProviderContractTests
{
    protected override IContentSourceProvider CreateProvider() =>
        new DirectLinkProvider(new StubContentSourceApi(), new StubCredentials("SESSDATA=test"));

    protected override ValueTask<ContentSourceDescriptor> CreateDescriptorAsync(IContentSourceProvider provider) =>
        provider.NormalizeAsync("BV1abcDEF123", CancellationToken.None);
}

public sealed class DirectLinkProviderTests
{
    [Theory]
    [InlineData("BV1abcDEF123", "video:bv:1abcDEF123", "BV1abcDEF123")]
    [InlineData("bv1abcDEF123", "video:bv:1abcDEF123", "BV1abcDEF123")]
    [InlineData("av000123", "video:av:123", "av123")]
    [InlineData("EP00012", "bangumi:ep:12", "ep12")]
    [InlineData("ss00034", "bangumi:ss:34", "ss34")]
    [InlineData("md00056", "bangumi:md:56", "md56")]
    public async Task 规范化所有P0直接链接并保留Bvid载荷大小写(
        string input,
        string stableId,
        string displayName)
    {
        var provider = CreateProvider(out _);

        var descriptor = await provider.NormalizeAsync(input, CancellationToken.None);

        Assert.Equal(stableId, descriptor.StableSourceId);
        Assert.Equal(displayName, descriptor.DisplayName);
        Assert.Empty(descriptor.PublicParameters);
    }

    [Fact]
    public async Task 短链通过窄Api解析且不在描述符保存原始地址()
    {
        var api = new StubContentSourceApi
        {
            ResolvedShortLink = "https://www.bilibili.com/video/BV1abcDEF123?p=1&token=secret",
        };
        var provider = new DirectLinkProvider(api, new StubCredentials("SESSDATA=secret"));

        var descriptor = await provider.NormalizeAsync("https://b23.tv/secret", CancellationToken.None);
        var json = JsonConvert.SerializeObject(descriptor);

        Assert.Equal("https://b23.tv/secret", api.LastShortLink);
        Assert.Equal("video:bv:1abcDEF123", descriptor.StableSourceId);
        Assert.DoesNotContain("b23.tv", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SESSDATA", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 直接来源第一页只有根项目且拒绝游标()
    {
        var provider = CreateProvider(out _);
        var descriptor = await provider.NormalizeAsync("ep0012", CancellationToken.None);
        var page = await provider.GetPageAsync(descriptor, new ContentPageRequest(), CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("bangumi:ep:12", item.Key.NativeId);
        Assert.Equal(12, item.EpId);
        Assert.False(page.HasMore);
        await Assert.ThrowsAsync<ContentSourceException>(() => provider.GetPageAsync(
            descriptor,
            new ContentPageRequest(continuationToken: "private-token"),
            CancellationToken.None));
    }

    [Fact]
    public async Task 视频集合经适配器生成序号原始标题封面和媒体键()
    {
        var provider = CreateProvider(out var api);
        api.VideoCollection = new BiliVideoCollection
        {
            SeriesTitle = "合集",
            Cover = "https://image.test/cover.jpg",
            Items =
            [
                new BiliVideoItem { Title = "第一P", Aid = 1, Cid = 11 },
                new BiliVideoItem { Title = "第二P", Aid = 1, Cid = 12 },
            ],
        };
        var descriptor = await provider.NormalizeAsync("av1", CancellationToken.None);
        var page = await provider.GetPageAsync(descriptor, new ContentPageRequest(), CancellationToken.None);

        var collection = await provider.ResolveItemAsync(descriptor, page.Items[0], CancellationToken.None);

        Assert.Equal([1, 2], collection.Items.Select(x => x.Index));
        Assert.Equal(["第一P", "第二P"], collection.Items.Select(x => x.OriginalTitle));
        Assert.All(collection.Items, x => Assert.Equal(collection.Cover, x.CoverUrl));
        Assert.Equal(new MediaUnitKey(1, 11), collection.Items[0].MediaUnitKey);
        Assert.Equal("av1", api.LastVideoId);
        Assert.False(api.LastVideoWasBvid);
    }

    [Theory]
    [InlineData("ep12", "ep12", false)]
    [InlineData("ss34", "ss34", true)]
    [InlineData("md56", "md56", false)]
    public async Task 番剧来源统一路由到目录Api(string input, string apiId, bool isSeasonId)
    {
        var provider = CreateProvider(out var api);
        var descriptor = await provider.NormalizeAsync(input, CancellationToken.None);
        var page = await provider.GetPageAsync(descriptor, new ContentPageRequest(), CancellationToken.None);

        await provider.ResolveItemAsync(descriptor, page.Items[0], CancellationToken.None);

        Assert.Equal(apiId, api.LastBangumiId);
        Assert.Equal(isSeasonId, api.LastBangumiWasSeason);
    }

    [Fact]
    public async Task 匿名公开链接可解析且非法输入使用结构化错误()
    {
        var provider = new DirectLinkProvider(new StubContentSourceApi(), new StubCredentials(""));
        var invalid = await Assert.ThrowsAsync<ContentSourceException>(async () =>
            await provider.NormalizeAsync("not-a-link", CancellationToken.None));
        Assert.Equal(ContentSourceErrorCode.InvalidInput, invalid.Code);

        var descriptor = await provider.NormalizeAsync("av1", CancellationToken.None);
        var page = await provider.GetPageAsync(descriptor, new ContentPageRequest(), CancellationToken.None);
        var collection = await provider.ResolveItemAsync(descriptor, page.Items[0], CancellationToken.None);
        Assert.Single(collection.Items);
        Assert.Equal(ContentSourceCapabilities.None, provider.Capabilities);
    }

    [Fact]
    public async Task Api异常被脱敏映射且无效媒体键按协议失败()
    {
        var provider = CreateProvider(out var api);
        api.Exception = new Exception("https://api.test?a=1&w_rid=secret SESSDATA=secret");
        var descriptor = await provider.NormalizeAsync("av1", CancellationToken.None);
        var page = await provider.GetPageAsync(descriptor, new ContentPageRequest(), CancellationToken.None);

        var remote = await Assert.ThrowsAsync<ContentSourceException>(() =>
            provider.ResolveItemAsync(descriptor, page.Items[0], CancellationToken.None));
        Assert.Equal(ContentSourceErrorCode.RemoteFailure, remote.Code);
        Assert.DoesNotContain("secret", remote.ToString(), StringComparison.OrdinalIgnoreCase);

        api.Exception = null;
        api.VideoCollection = new BiliVideoCollection
        {
            SeriesTitle = "坏数据",
            Items = [new BiliVideoItem { Title = "坏项目", Aid = 0, Cid = 1 }],
        };
        var protocol = await Assert.ThrowsAsync<ContentSourceException>(() =>
            provider.ResolveItemAsync(descriptor, page.Items[0], CancellationToken.None));
        Assert.Equal(ContentSourceErrorCode.ProtocolViolation, protocol.Code);
    }

    [Fact]
    public async Task DirectLinkProvider覆盖非法边界和全部根项目引用()
    {
        var provider = CreateProvider(out _);
        await Assert.ThrowsAsync<ContentSourceException>(async () =>
            await provider.NormalizeAsync(" ", CancellationToken.None));
        await Assert.ThrowsAsync<ContentSourceException>(async () =>
            await provider.NormalizeAsync("av0", CancellationToken.None));
        await Assert.ThrowsAsync<ContentSourceException>(async () =>
            await provider.NormalizeAsync("av999999999999999999999999", CancellationToken.None));

        foreach (var input in new[] { "BV1abcDEF123", "av1", "ss2", "md3" })
        {
            var descriptor = await provider.NormalizeAsync(input, CancellationToken.None);
            var page = await provider.GetPageAsync(descriptor, new ContentPageRequest(), CancellationToken.None);
            Assert.Single(page.Items);
        }

        var valid = await provider.NormalizeAsync("av1", CancellationToken.None);
        var wrongItem = new ContentSourceItem(
            new ContentItemKey(ContentSourceKind.DirectLink, "video:av:2"),
            "av2",
            ContentSourceItemType.Video);
        await Assert.ThrowsAsync<ContentSourceException>(() =>
            provider.ResolveItemAsync(valid, wrongItem, CancellationToken.None));

        var wrongKind = new ContentSourceDescriptor(ContentSourceKind.Uploader, "video:av:1", "av1", null, 1);
        await Assert.ThrowsAsync<ContentSourceException>(() =>
            provider.GetPageAsync(wrongKind, new ContentPageRequest(), CancellationToken.None));
        var wrongVersion = new ContentSourceDescriptor(ContentSourceKind.DirectLink, "video:av:1", "av1", null, 2);
        await Assert.ThrowsAsync<ContentSourceException>(() =>
            provider.GetPageAsync(wrongVersion, new ContentPageRequest(), CancellationToken.None));
    }

    private static DirectLinkProvider CreateProvider(out StubContentSourceApi api)
    {
        api = new StubContentSourceApi();
        return new DirectLinkProvider(api, new StubCredentials("SESSDATA=test"));
    }
}

public sealed class ContentSourceViewModelAndDiTests
{
    [Fact]
    public void 模块注册RegistryProvider和同一Api的窄接口投影()
    {
        var services = new ServiceCollection();
        new BiliDownloaderPluginModule().ConfigureServices(services);
        services.AddSingleton<IBiliCredentialProvider>(new StubCredentials("SESSDATA=test"));

        using var provider = services.BuildServiceProvider();
        var api = provider.GetRequiredService<BiliApiService>();

        Assert.Same(api, provider.GetRequiredService<IBiliContentSourceApi>());
        Assert.Same(api, provider.GetRequiredService<IBiliMediaProbe>());
        var registry = provider.GetRequiredService<IContentSourceProviderRegistry>();
        Assert.IsType<DirectLinkProvider>(registry.GetRequired(ContentSourceKind.DirectLink));
        Assert.Same(registry, provider.GetRequiredService<IContentSourceProviderRegistry>());
    }

    [Fact]
    public async Task 解析取消不覆盖上一次成功结果或触发第二次回调()
    {
        var source = new CancelOnSecondResolveProvider();
        var registry = new ContentSourceProviderRegistry([source]);
        var probe = new StubMediaProbe();
        var callbackCount = 0;
        var vm = new VideoParseViewModel(
            registry,
            probe,
            new StubCredentials("SESSDATA=test"),
            _ => callbackCount++,
            () => true)
        {
            Url = "BV1abcDEF123",
        };

        await vm.ParseCommand.ExecuteAsync(null);
        var successfulCollection = vm.VideoCollection;
        vm.Url = "av2";

        var second = vm.ParseCommand.ExecuteAsync(null);
        await source.SecondResolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.ParseCommand.Cancel();
        await second.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Same(successfulCollection, vm.VideoCollection);
        Assert.True(vm.IsParsed);
        Assert.Equal(1, callbackCount);
        Assert.Contains("已取消解析", vm.DownloadInfo, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ContentSourceErrorCode.InvalidInput, "无法解析链接")]
    [InlineData(ContentSourceErrorCode.LoginRequired, "请先登录")]
    [InlineData(ContentSourceErrorCode.RemoteFailure, "远端暂时不可用")]
    [InlineData(ContentSourceErrorCode.ProtocolViolation, "不符合协议")]
    [InlineData(ContentSourceErrorCode.UnknownProvider, "不符合协议")]
    public async Task 解析ViewModel将来源错误映射为稳定中文提示(
        ContentSourceErrorCode code,
        string expected)
    {
        var source = new ThrowingNormalizeProvider(code);
        var vm = new VideoParseViewModel(
            new ContentSourceProviderRegistry([source]),
            new StubMediaProbe(),
            new StubCredentials("SESSDATA=test"),
            null,
            () => true)
        {
            Url = "av1",
        };

        await vm.ParseCommand.ExecuteAsync(null);

        Assert.Contains(expected, vm.DownloadInfo, StringComparison.Ordinal);
        Assert.False(vm.IsLoading);
        Assert.False(vm.IsParsed);
    }

    [Fact]
    public async Task 解析ViewModel隐藏未知异常文本()
    {
        var source = new ThrowingNormalizeProvider(
            ContentSourceErrorCode.RemoteFailure,
            throwUnknownException: true);
        var vm = new VideoParseViewModel(
            new ContentSourceProviderRegistry([source]),
            new StubMediaProbe(),
            new StubCredentials("SESSDATA=test"),
            null,
            () => true)
        {
            Url = "av1",
        };

        await vm.ParseCommand.ExecuteAsync(null);

        Assert.Equal("解析异常，请稍后重试", vm.DownloadInfo);
        Assert.DoesNotContain("secret", vm.DownloadInfo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 解析ViewModel不以本地登录状态阻断公开来源()
    {
        var source = new CountingNormalizeProvider();
        var vm = new VideoParseViewModel(
            new ContentSourceProviderRegistry([source]),
            new StubMediaProbe(),
            new StubCredentials(""),
            null,
            () => false)
        {
            Url = "av1",
        };

        await vm.ParseCommand.ExecuteAsync(null);

        Assert.Equal("解析异常，请稍后重试", vm.DownloadInfo);
        Assert.Equal(1, source.NormalizeCount);
    }

    [Fact]
    public async Task 解析ViewModel覆盖全部音频质量命名分支()
    {
        var source = new CancelOnSecondResolveProvider();
        var probe = new StubMediaProbe
        {
            AudioStreams =
            [
                new BiliDashStream { Id = 30216, Bandwidth = 64000 },
                new BiliDashStream { Id = 30232, Bandwidth = 128000 },
                new BiliDashStream { Id = 30280, Bandwidth = 256000 },
                new BiliDashStream { Id = 30251, Bandwidth = 320000 },
                new BiliDashStream { Id = 42, Bandwidth = 48000 },
                new BiliDashStream { Id = 30232, Bandwidth = 96000 },
            ],
        };
        VideoParseResult? result = null;
        var vm = new VideoParseViewModel(
            new ContentSourceProviderRegistry([source]),
            probe,
            new StubCredentials("SESSDATA=test"),
            parsed => result = parsed,
            () => true)
        {
            Url = "av1",
        };

        await vm.ParseCommand.ExecuteAsync(null);

        Assert.NotNull(result);
        Assert.Contains(result.AudioQualityOptions, x => x.DisplayName.Contains("标准", StringComparison.Ordinal));
        Assert.Contains(result.AudioQualityOptions, x => x.DisplayName.Contains("高品质", StringComparison.Ordinal));
        Assert.Contains(result.AudioQualityOptions, x => x.DisplayName.Contains("无损", StringComparison.Ordinal));
        Assert.Contains(result.AudioQualityOptions, x => x.DisplayName.Contains("Hi-Res", StringComparison.Ordinal));
        Assert.Contains(result.AudioQualityOptions, x => x.DisplayName.Contains("ID:42", StringComparison.Ordinal));
        Assert.Equal(30251, result.SelectedAudioQuality?.QualityId);
    }
}

internal sealed class StubProvider : IContentSourceProvider
{
    public StubProvider(
        ContentSourceKind kind,
        ContentSourceCapabilities capabilities = ContentSourceCapabilities.None,
        int capabilityVersion = 1)
    {
        Kind = kind;
        Capabilities = capabilities;
        CapabilityVersion = capabilityVersion;
    }

    public ContentSourceKind Kind { get; }
    public ContentSourceCapabilities Capabilities { get; }
    public int CapabilityVersion { get; }

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ContentSourceDescriptor(Kind, input, input, null, CapabilityVersion));
    }

    public Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ContentPage([], null, false));
    }

    public Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor,
        ContentSourceItem item,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class StubContentSourceApi : IBiliContentSourceApi
{
    public string ResolvedShortLink { get; set; } = "https://www.bilibili.com/video/BV1abcDEF123";
    public Exception? Exception { get; set; }
    public string? LastShortLink { get; private set; }
    public string? LastVideoId { get; private set; }
    public bool LastVideoWasBvid { get; private set; }
    public string? LastBangumiId { get; private set; }
    public bool LastBangumiWasSeason { get; private set; }

    public BiliVideoCollection VideoCollection { get; set; } = ValidCollection();
    public BiliVideoCollection BangumiCollection { get; set; } = ValidCollection(BiliMediaType.Bangumi);

    public Task<string> ResolveShortLinkAsync(string shortLink, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastShortLink = shortLink;
        if (Exception is not null) throw Exception;
        return Task.FromResult(ResolvedShortLink);
    }

    public Task<BiliVideoCollection> GetVideoCollectionAsync(
        string videoId,
        bool isBvid,
        string cookie,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastVideoId = videoId;
        LastVideoWasBvid = isBvid;
        if (Exception is not null) throw Exception;
        return Task.FromResult(VideoCollection);
    }

    public Task<BiliVideoCollection> GetBangumiCollectionAsync(
        string id,
        bool isSeasonId,
        string cookie,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastBangumiId = id;
        LastBangumiWasSeason = isSeasonId;
        if (Exception is not null) throw Exception;
        return Task.FromResult(BangumiCollection);
    }

    private static BiliVideoCollection ValidCollection(BiliMediaType mediaType = BiliMediaType.Video) => new()
    {
        SeriesTitle = "测试集合",
        Cover = "https://image.test/cover.jpg",
        Items =
        [
            new BiliVideoItem
            {
                Title = "测试项目",
                Aid = 1,
                Cid = 2,
                MediaType = mediaType,
                EpId = mediaType == BiliMediaType.Bangumi ? 3 : 0,
                SeasonId = mediaType == BiliMediaType.Bangumi ? 4 : 0,
            },
        ],
    };
}

internal sealed class StubCredentials(string cookie) : IBiliCredentialProvider
{
    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(cookie);
    public string GetCookieHeader() => cookie;
}

internal sealed class StubMediaProbe : IBiliMediaProbe
{
    public List<BiliDashStream> AudioStreams { get; init; } = [];

    public Task<BiliDashResult> GetDashResultAsync(
        long aid,
        long cid,
        int qualityId,
        string cookie,
        BiliMediaType mediaType = BiliMediaType.Video,
        long epId = 0,
        long seasonId = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BiliDashResult
        {
            AcceptQualities = [new BiliQualityOption { QualityId = 80, DisplayName = "1080P" }],
            AudioStreams = AudioStreams,
        });
    }
}

internal sealed class CancelOnSecondResolveProvider : IContentSourceProvider, IContentSourceResolutionProvider
{
    private int _resolveCount;
    public TaskCompletionSource SecondResolveStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ContentSourceKind Kind => ContentSourceKind.DirectLink;
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresLogin;
    public int CapabilityVersion => 1;

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ContentSourceDescriptor(Kind, input, input, null, 1));
    }

    public Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ContentPage(
            [new ContentSourceItem(new ContentItemKey(Kind, descriptor.StableSourceId), "项目", ContentSourceItemType.Video)],
            null,
            false));
    }

    public async Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor,
        ContentSourceItem item,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _resolveCount) == 2)
        {
            SecondResolveStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return new BiliVideoCollection
        {
            SeriesTitle = descriptor.DisplayName,
            Items =
            [
                new BiliVideoItem
                {
                    Title = "项目",
                    OriginalTitle = "项目",
                    Aid = 1,
                    Cid = 2,
                    MediaUnitKey = new MediaUnitKey(1, 2),
                },
            ],
        };
    }
}

internal sealed class ThrowingNormalizeProvider(
    ContentSourceErrorCode code,
    bool throwUnknownException = false) : IContentSourceProvider
{
    public ContentSourceKind Kind => ContentSourceKind.DirectLink;
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresLogin;
    public int CapabilityVersion => 1;

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (throwUnknownException)
            throw new InvalidOperationException("secret remote detail");
        throw new ContentSourceException(
            code,
            code == ContentSourceErrorCode.RemoteFailure ? "远端暂时不可用" : "结构化来源错误");
    }

    public Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor,
        ContentSourceItem item,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class CountingNormalizeProvider : IContentSourceProvider
{
    public int NormalizeCount { get; private set; }
    public ContentSourceKind Kind => ContentSourceKind.DirectLink;
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresLogin;
    public int CapabilityVersion => 1;

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken)
    {
        NormalizeCount++;
        return ValueTask.FromResult(new ContentSourceDescriptor(Kind, input, input, null, 1));
    }

    public Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor,
        ContentSourceItem item,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}
