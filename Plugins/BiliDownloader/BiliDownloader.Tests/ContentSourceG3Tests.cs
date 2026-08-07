using System.Diagnostics;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.ViewModels.BiliDownloader;

namespace BiliDownloader.Tests;

public sealed class ContentSourceG3FilteringTests
{
    [Fact]
    public void 筛选规则规范化关键词媒体类型并限制明文长度()
    {
        var rules = new SourceFilterRules(
            "  作者  ",
            mediaTypes: [ContentSourceItemType.Video, ContentSourceItemType.Bangumi, ContentSourceItemType.Video]);

        Assert.Equal("作者", rules.Keyword);
        Assert.Equal([ContentSourceItemType.Video, ContentSourceItemType.Bangumi], rules.MediaTypes);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SourceFilterRules(new string('x', SourceFilterRules.MaxKeywordLength + 1)));
    }

    [Fact]
    public void 指纹对等价规则稳定且不包含关键词明文()
    {
        var first = new SourceFilterRules(" ＡＢＣ ", mediaTypes: [ContentSourceItemType.Video, ContentSourceItemType.Video]);
        var second = new SourceFilterRules("ABC", mediaTypes: [ContentSourceItemType.Video]);

        var firstFingerprint = ContentFilterPlanBuilder.CreateFingerprint(first);
        var secondFingerprint = ContentFilterPlanBuilder.CreateFingerprint(second);

        Assert.Equal(firstFingerprint, secondFingerprint);
        Assert.DoesNotContain("ABC", firstFingerprint.Value, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(firstFingerprint, ContentFilterPlanBuilder.CreateFingerprint(new SourceFilterRules("ABCD")));
    }

    [Fact]
    public void 执行计划只下推Provider声明支持的字段()
    {
        var rules = new SourceFilterRules(
            "目标",
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2025-12-31T23:59:59Z"),
            [ContentSourceItemType.Video],
            ContentSourceSortOrder.PublishedNewest);

        var plan = ContentFilterPlanBuilder.Build(
            rules,
            ContentSourceCapabilities.SupportsKeyword | ContentSourceCapabilities.SupportsTypeFilter);

        Assert.Equal("目标", plan.ServerRules.Keyword);
        Assert.Equal([ContentSourceItemType.Video], plan.ServerRules.MediaTypes);
        Assert.Null(plan.ServerRules.PublishedFrom);
        Assert.Null(plan.ResidualRules.Keyword);
        Assert.Equal(rules.PublishedFrom, plan.ResidualRules.PublishedFrom);
        Assert.Equal(ContentSourceSortOrder.PublishedNewest, plan.ResidualRules.SortOrder);
    }

    [Fact]
    public void 客户端筛选覆盖关键词日期类型并稳定排列未知日期到末尾()
    {
        var start = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
        var end = DateTimeOffset.Parse("2025-12-31T23:59:59Z");
        var items = new[]
        {
            Item(1, "普通", "目标作者", start.AddDays(2), ContentSourceItemType.Video),
            Item(2, "目标标题", "其他", start.AddDays(1), ContentSourceItemType.Video),
            Item(3, "目标但过期", "其他", start.AddDays(-1), ContentSourceItemType.Video),
            Item(4, "目标但未知日期", "其他", null, ContentSourceItemType.Video),
            Item(5, "目标番剧", "其他", start.AddDays(3), ContentSourceItemType.Bangumi),
        };
        var rules = new SourceFilterRules(
            "目标", start, end, [ContentSourceItemType.Video], ContentSourceSortOrder.PublishedNewest);

        var result = ContentSourceFilterEngine.Apply(items, rules);

        Assert.Equal([1L, 2L], result.Select(static item => item.Aid!.Value));
        var unknownLast = ContentSourceFilterEngine.Apply(
            [items[3], items[1]],
            new SourceFilterRules(sortOrder: ContentSourceSortOrder.PublishedOldest));
        Assert.Equal([2L, 4L], unknownLast.Select(static item => item.Aid!.Value));
    }

    [Theory]
    [InlineData(100, 250)]
    [InlineData(500, 500)]
    [InlineData(1000, 1000)]
    public void 大列表筛选与排序保持线性性能(int count, int limitMilliseconds)
    {
        var items = Enumerable.Range(1, count)
            .Select(index => Item(index, $"视频 {index}", index % 2 == 0 ? "目标作者" : "其他",
                DateTimeOffset.UnixEpoch.AddDays(index), ContentSourceItemType.Video))
            .ToArray();
        var rules = new SourceFilterRules("目标", sortOrder: ContentSourceSortOrder.PublishedNewest);
        _ = ContentSourceFilterEngine.Apply(items, rules);

        var stopwatch = Stopwatch.StartNew();
        var result = ContentSourceFilterEngine.Apply(items, rules);
        stopwatch.Stop();

        Assert.Equal(count / 2, result.Count);
        Assert.True(stopwatch.ElapsedMilliseconds < limitMilliseconds,
            $"筛选 {count} 项耗时 {stopwatch.ElapsedMilliseconds}ms，超过 {limitMilliseconds}ms。 ");
    }

    internal static ContentSourceItem Item(
        long id,
        string title,
        string? author = "作者",
        DateTimeOffset? publishedAt = null,
        ContentSourceItemType type = ContentSourceItemType.Video) =>
        new(new ContentItemKey(ContentSourceKind.Uploader, $"aid:{id}"), title, type,
            author, publishedAt, aid: id, bvid: $"BV{id:0000000000}");
}

public sealed class ContentSourceG3SelectionTests
{
    [Fact]
    public void 显式选择支持全选已加载取消已加载和清空全部()
    {
        var state = new ContentSelectionState();
        var fingerprint = ContentFilterPlanBuilder.CreateFingerprint(SourceFilterRules.Empty);
        var keys = Enumerable.Range(1, 3)
            .Select(index => new ContentItemKey(ContentSourceKind.Uploader, $"aid:{index}"))
            .ToArray();

        state.SelectLoaded(keys, fingerprint);
        state.DeselectLoaded([keys[1]], fingerprint);

        Assert.True(state.IsSelected(keys[0], fingerprint));
        Assert.False(state.IsSelected(keys[1], fingerprint));
        Assert.Equal(2, state.ExplicitCount);
        state.Clear();
        Assert.False(state.HasSelection);
    }

    [Fact]
    public void 全部匹配只保存排除键且筛选变化后失效()
    {
        var state = new ContentSelectionState();
        var first = ContentFilterPlanBuilder.CreateFingerprint(new SourceFilterRules("甲"));
        var second = ContentFilterPlanBuilder.CreateFingerprint(new SourceFilterRules("乙"));
        var key = new ContentItemKey(ContentSourceKind.Uploader, "aid:1");

        state.SelectAllMatching(first);
        state.SetSelected(key, false, first);

        Assert.Equal(SelectionScope.AllMatchingResults, state.Scope);
        Assert.Empty(state.SelectedKeys);
        Assert.Single(state.ExcludedKeys);
        Assert.False(state.IsSelected(key, first));
        Assert.True(state.InvalidateAllMatching(second));
        Assert.False(state.HasSelection);
    }

    [Fact]
    public void 显式选择不因筛选指纹变化而失效()
    {
        var state = new ContentSelectionState();
        var first = ContentFilterPlanBuilder.CreateFingerprint(new SourceFilterRules("甲"));
        var second = ContentFilterPlanBuilder.CreateFingerprint(new SourceFilterRules("乙"));
        var key = new ContentItemKey(ContentSourceKind.Uploader, "aid:1");
        state.SetSelected(key, true, first);

        Assert.False(state.InvalidateAllMatching(second));
        Assert.True(state.IsSelected(key, second));
    }
}

public sealed class ContentSourceG3CacheAndMaterializationTests
{
    [Fact]
    public async Task 查询协调器推进代际时取消旧令牌并串行化游标推进()
    {
        using var coordinator = new ContentQueryCoordinator();
        var oldToken = coordinator.Token;
        var firstGeneration = coordinator.Generation;

        var nextGeneration = coordinator.Advance();

        Assert.True(oldToken.IsCancellationRequested);
        Assert.True(nextGeneration > firstGeneration);
        Assert.True(coordinator.IsCurrent(nextGeneration));
        using var lease = await coordinator.EnterAsync(CancellationToken.None);
    }

    [Fact]
    public void 页面缓存按完整键隔离并淘汰最久未访问页()
    {
        var cache = new MemoryContentPageCache(2);
        var fingerprint = ContentFilterPlanBuilder.CreateFingerprint(SourceFilterRules.Empty);
        var first = Key("1", fingerprint);
        var second = Key("2", fingerprint);
        var third = Key("3", fingerprint);
        cache.Set(first, Page(1));
        cache.Set(second, Page(2));
        Assert.True(cache.TryGet(first, out _));

        cache.Set(third, Page(3));

        Assert.True(cache.TryGet(first, out _));
        Assert.False(cache.TryGet(second, out _));
        Assert.True(cache.TryGet(third, out _));
    }

    [Fact]
    public void 刷新只失效当前来源父级和筛选组合()
    {
        var cache = new MemoryContentPageCache();
        var descriptor = new ContentSourceDescriptor(
            ContentSourceKind.Uploader, "uploader:1", "来源", null, 1);
        var firstFingerprint = ContentFilterPlanBuilder.CreateFingerprint(new SourceFilterRules("甲"));
        var secondFingerprint = ContentFilterPlanBuilder.CreateFingerprint(new SourceFilterRules("乙"));
        var first = Key("1", firstFingerprint);
        var second = Key("2", secondFingerprint);
        cache.Set(first, Page(1));
        cache.Set(second, Page(2));

        cache.Invalidate(descriptor, null, firstFingerprint);

        Assert.False(cache.TryGet(first, out _));
        Assert.True(cache.TryGet(second, out _));
    }

    [Fact]
    public async Task 全部匹配逐页枚举应用客户端筛选与排除且不创建ItemViewModel()
    {
        var provider = new PagedG3Provider();
        var descriptor = await provider.NormalizeAsync("1", CancellationToken.None);
        var rules = new SourceFilterRules("目标");
        var selection = new ContentSelectionState();
        selection.SelectAllMatching(ContentFilterPlanBuilder.CreateFingerprint(rules));
        selection.SetSelected(
            new ContentItemKey(ContentSourceKind.Uploader, "aid:3"),
            false,
            ContentFilterPlanBuilder.CreateFingerprint(rules));

        var result = await new ContentSelectionMaterializer().MaterializeAllMatchingAsync(
            provider, descriptor, null, rules, selection, null, CancellationToken.None);

        Assert.Equal([1L], result.Select(static item => item.Aid!.Value));
        Assert.Equal(3, provider.CallCount); // 两页枚举 + 无 SnapshotToken 时首屏复核
        Assert.All(provider.Requests, request => Assert.Null(request.Filters.Keyword));
    }

    [Fact]
    public async Task 快照在分页期间变化会中止全部匹配物化()
    {
        var provider = new PagedG3Provider(snapshotChanges: true);
        var descriptor = await provider.NormalizeAsync("1", CancellationToken.None);
        var selection = new ContentSelectionState();
        var fingerprint = ContentFilterPlanBuilder.CreateFingerprint(SourceFilterRules.Empty);
        selection.SelectAllMatching(fingerprint);

        var error = await Assert.ThrowsAsync<ContentSourceException>(() =>
            new ContentSelectionMaterializer().MaterializeAllMatchingAsync(
                provider, descriptor, null, SourceFilterRules.Empty, selection, null, CancellationToken.None));

        Assert.Equal(ContentSourceErrorCode.ProtocolViolation, error.Code);
        Assert.Contains("发生变化", error.Message);
    }

    private static ContentPageCacheKey Key(string token, FilterFingerprint fingerprint) =>
        new(ContentSourceKind.Uploader, "uploader:1", 1, null, fingerprint, 20, token);

    private static ContentPage Page(long id) => new(
        [ContentSourceG3FilteringTests.Item(id, $"视频 {id}")], null, false);
}

public sealed class ContentSourceG3BrowserTests
{
    [Fact]
    public async Task 全选已加载可升级为全部匹配且逐项取消只产生排除键()
    {
        var provider = new PagedG3Provider();
        var browser = Browser(provider);
        var descriptor = await provider.NormalizeAsync("1", CancellationToken.None);
        await browser.OpenAsync(descriptor);

        browser.SelectLoadedCommand.Execute(null);

        Assert.True(browser.HasSelection);
        Assert.True(browser.ShowSelectAllMatchingPrompt);
        Assert.Equal(true, browser.LoadedSelectionState);
        browser.SelectAllMatchingCommand.Execute(null);
        Assert.True(browser.IsAllMatchingSelected);

        browser.Items[0].IsSelected = false;
        Assert.Contains("排除 1", browser.SelectionSummaryText);
    }

    [Fact]
    public async Task 筛选变化使全部匹配失效但保留显式选择()
    {
        var provider = new PagedG3Provider();
        var browser = Browser(provider);
        var descriptor = await provider.NormalizeAsync("1", CancellationToken.None);
        await browser.OpenAsync(descriptor);
        browser.SelectLoadedCommand.Execute(null);
        browser.SelectAllMatchingCommand.Execute(null);

        browser.SearchText = "目标";
        await Task.Delay(400);

        Assert.False(browser.IsAllMatchingSelected);
        Assert.False(browser.HasSelection);
        Assert.True(browser.HasSelectionInvalidatedMessage);

        browser.SelectLoadedCommand.Execute(null);
        browser.SearchText = "普通";
        await Task.Delay(400);
        Assert.True(browser.HasSelection);
        Assert.Contains("隐藏", browser.SelectionSummaryText);
    }

    [Fact]
    public async Task 后返回的旧代际响应不能覆盖新筛选结果()
    {
        var provider = new DeferredGenerationProvider();
        var browser = Browser(provider);
        var descriptor = await provider.NormalizeAsync("1", CancellationToken.None);
        await browser.OpenAsync(descriptor);

        browser.SearchText = "慢";
        await provider.SecondCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        browser.SearchText = "快";
        await Task.Delay(320);
        provider.CompleteSecondCall();
        await provider.ThirdCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        Assert.Single(browser.Items);
        Assert.Equal("快结果", browser.Items[0].Title);
    }

    private static ContentSourceBrowserViewModel Browser(IContentSourceProvider provider) =>
        new(
            new ContentSourceProviderRegistry([provider]),
            new VideoParseResultFactory(new StubMediaProbe(), new StubCredentials("")),
            _ => { });
}

internal sealed class PagedG3Provider(bool snapshotChanges = false) :
    IContentSourceProvider,
    IContentSourceResolutionProvider
{
    public ContentSourceKind Kind => ContentSourceKind.Uploader;
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPaging;
    public int CapabilityVersion => 1;
    public int CallCount { get; private set; }
    public List<ContentPageRequest> Requests { get; } = [];

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ContentSourceDescriptor(Kind, "uploader:1", "测试来源", null, 1));

    public Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        Requests.Add(request);
        if (request.ContinuationToken is null)
            return Task.FromResult(new ContentPage(
                [
                    ContentSourceG3FilteringTests.Item(1, "目标一"),
                    ContentSourceG3FilteringTests.Item(2, "普通二"),
                ],
                "next", true, snapshotChanges ? "snapshot-a" : null));
        return Task.FromResult(new ContentPage(
            [
                ContentSourceG3FilteringTests.Item(3, "目标三"),
                ContentSourceG3FilteringTests.Item(4, "普通四"),
            ],
            null, false, snapshotChanges ? "snapshot-b" : null));
    }

    public Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor,
        ContentSourceItem item,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BiliVideoCollection
        {
            SeriesTitle = item.Title,
            Items =
            [
                new BiliVideoItem
                {
                    Aid = item.Aid ?? 1,
                    Cid = item.Aid ?? 1,
                    Bvid = item.Bvid ?? "BV1",
                    Title = item.Title,
                    OriginalTitle = item.Title,
                    MediaUnitKey = new MediaUnitKey(item.Aid ?? 1, item.Aid ?? 1),
                },
            ],
        });
}

internal sealed class DeferredGenerationProvider :
    IContentSourceProvider,
    IContentSourceResolutionProvider
{
    private readonly TaskCompletionSource<ContentPage> _secondPage =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public ContentSourceKind Kind => ContentSourceKind.Uploader;
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPaging;
    public int CapabilityVersion => 1;
    public TaskCompletionSource SecondCallStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ThirdCallStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ContentSourceDescriptor(Kind, "uploader:deferred", "并发测试", null, 1));

    public Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);
        if (call == 1)
            return Task.FromResult(Page("初始", 1));
        if (call == 2)
        {
            SecondCallStarted.TrySetResult();
            return _secondPage.Task; // 故意忽略取消，验证 generation guard。
        }

        ThirdCallStarted.TrySetResult();
        return Task.FromResult(new ContentPage(
            [
                ContentSourceG3FilteringTests.Item(3, "快结果"),
                ContentSourceG3FilteringTests.Item(4, "慢结果"),
            ], null, false));
    }

    public void CompleteSecondCall() => _secondPage.TrySetResult(Page("慢结果", 2));

    public Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor,
        ContentSourceItem item,
        CancellationToken cancellationToken) => Task.FromResult(new BiliVideoCollection());

    private static ContentPage Page(string title, long id) => new(
        [ContentSourceG3FilteringTests.Item(id, title)], null, false);
}
