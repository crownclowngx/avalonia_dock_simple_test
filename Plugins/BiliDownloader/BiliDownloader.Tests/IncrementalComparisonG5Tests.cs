using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.Services.Persistence;
using BiliDownloader.Services.Download;
using BiliDownloader.ViewModels.BiliDownloader;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.Tests;

public sealed class IncrementalIdentityG5Tests
{
    [Fact]
    public void 媒体键使用版本化格式并可安全往返()
    {
        var key = new MediaUnitKey(123, 456);

        Assert.Equal("mu1:123:456", key.ToStorageKey());
        Assert.True(MediaUnitKey.TryParseStorageKey(key.ToStorageKey(), out var restored));
        Assert.Equal(key, restored);
        Assert.False(MediaUnitKey.TryParseStorageKey("123:456", out _));
        Assert.False(MediaUnitKey.TryParseStorageKey("mu1:0:1", out _));
    }

    [Fact]
    public void 输出指纹只随规定的输出维度变化()
    {
        var media = new MediaUnitKey(1, 2);
        var baseSpec = new RenditionSpecification(80, 30280,
            VideoCodecPreference.AutoCompatibility, OutputContainer.Mp4, OutputMediaMode.AudioVideo);
        var first = RenditionFingerprint.Create(media, baseSpec);
        var second = RenditionFingerprint.Create(media, baseSpec);
        var changed = RenditionFingerprint.Create(media, baseSpec with { OutputContainer = OutputContainer.Mkv });

        Assert.Equal(first, second);
        Assert.StartsWith(RenditionFingerprint.Prefix, first.Value);
        Assert.NotEqual(first, changed);
        Assert.True(RenditionFingerprint.TryParse(first.Value, out var restored));
        Assert.Equal(first, restored);
    }
}

public sealed class ContentComparisonPolicyG5Tests
{
    private static readonly MediaUnitKey Media = new(1, 2);
    private static readonly RenditionSpecification Spec = new(
        80, 0, VideoCodecPreference.AutoCompatibility, OutputContainer.Mp4, OutputMediaMode.AudioVideo);
    private static readonly RenditionFingerprint Fingerprint = RenditionFingerprint.Create(Media, Spec);

    [Theory]
    [InlineData("pending", false)]
    [InlineData("waiting_for_login", false)]
    [InlineData("downloading_video", false)]
    [InlineData("paused", false)]
    [InlineData("interrupted", false)]
    [InlineData("failed", true)]
    public void 活动和可恢复任务分类为下载中(string status, bool retryable)
    {
        var policy = new ContentComparisonPolicy(new FixedFileFacts(false));
        var result = policy.Classify(Media, Fingerprint, Spec, "标题", [], Item(),
        [
            Task(status, Fingerprint.Value, retryable),
        ]);

        Assert.Equal(ContentComparisonStatus.InProgress, result.Status);
    }

    [Fact]
    public void 完成且文件存在优先于活动任务()
    {
        var policy = new ContentComparisonPolicy(new FixedFileFacts(true));
        var result = policy.Classify(Media, Fingerprint, Spec, "标题", [], Item(),
        [
            Task("pending", Fingerprint.Value),
            Task("done", Fingerprint.Value, path: "exists.mp4"),
        ]);

        Assert.Equal(ContentComparisonStatus.Downloaded, result.Status);
    }

    [Fact]
    public void 成品缺失和旧身份候选仍分类为新增并警告()
    {
        var policy = new ContentComparisonPolicy(new FixedFileFacts(false));
        var result = policy.Classify(Media, Fingerprint, Spec, "标题", [], Item(),
        [
            Task("done", Fingerprint.Value, path: "missing.mp4"),
            Task("done", ""),
        ]);

        Assert.Equal(ContentComparisonStatus.New, result.Status);
        Assert.Contains(result.Warnings, warning => warning.Code == "completed_file_missing");
        Assert.Contains(result.Warnings, warning =>
            warning.Code == "legacy_identity_incomplete" && warning.RequiresConfirmation);
    }

    [Theory]
    [InlineData("canceled", false)]
    [InlineData("failed", false)]
    public void 取消和不可恢复失败不占用输出身份(string status, bool retryable)
    {
        var policy = new ContentComparisonPolicy(new FixedFileFacts(false));
        var result = policy.Classify(Media, Fingerprint, Spec, "标题", [], Item(),
        [
            Task(status, Fingerprint.Value, retryable),
        ]);

        Assert.Equal(ContentComparisonStatus.New, result.Status);
    }

    private static BiliVideoItem Item() => new() { Aid = 1, Cid = 2, Title = "标题", MediaUnitKey = Media };

    private static DownloadTaskRecord Task(string status, string fingerprint, bool retryable = false, string path = "") => new()
    {
        TaskId = Guid.NewGuid().ToString("N"),
        Aid = 1,
        Cid = 2,
        MediaUnitKey = Media.ToStorageKey(),
        RenditionFingerprint = fingerprint,
        QualityId = 80,
        AudioQualityId = 0,
        Status = status,
        IsRetryable = retryable,
        OutputFilePath = path,
    };

    private sealed class FixedFileFacts(bool exists) : IOutputFileFactProvider
    {
        public bool Exists(string path) => exists;
    }
}

public sealed class IncrementalScannerG5Tests
{
    [Fact]
    public async Task 层级来源递归扫描并聚合完整叶子边界()
    {
        var provider = TestIncrementalProvider.Hierarchical();
        var scanner = new ContentSourceScanService();
        var result = await scanner.ScanAsync(provider, provider, provider.Descriptor,
            SourceFilterRules.Empty, null, CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Leaves.Count);
        Assert.Equal(2, result.BoundaryKeys.Count);
        Assert.All(result.Leaves, leaf => Assert.Single(leaf.ResolvedItems));
        Assert.Contains(provider.Calls, call => call.ParentKey?.NativeId == "folder:1");
        Assert.StartsWith("scan1:", result.ScanFingerprint);
    }

    [Fact]
    public async Task 重复游标产生部分预览且不宣称完整()
    {
        var provider = TestIncrementalProvider.Looping();
        var result = await new ContentSourceScanService().ScanAsync(
            provider, provider, provider.Descriptor, SourceFilterRules.Empty, null, CancellationToken.None);

        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Code == "scan_partial");
    }

    [Fact]
    public async Task 取消保留已加载结果但不推进完整性()
    {
        var provider = TestIncrementalProvider.CancelAfterFirst();
        using var cts = new CancellationTokenSource();
        provider.OnFirstPage = cts.Cancel;

        var result = await new ContentSourceScanService().ScanAsync(
            provider, provider, provider.Descriptor, SourceFilterRules.Empty, null, cts.Token);

        Assert.False(result.IsComplete);
        Assert.Contains(result.Warnings, warning => warning.Code == "scan_canceled");
    }
}

public sealed class IncrementalComparisonServiceG5Tests
{
    [Fact]
    public async Task 完整扫描产生五类中的来源优先结果并更新轻量基线()
    {
        var provider = TestIncrementalProvider.Flat(
            new ContentSourceItem(new(ContentSourceKind.Uploader, "aid:1"), "匹配", ContentSourceItemType.Video, aid: 1, cid: 11),
            new ContentSourceItem(new(ContentSourceKind.Uploader, "aid:2"), "排除", ContentSourceItemType.Video, aid: 2, cid: 22));
        var registry = new ContentSourceProviderRegistry([provider]);
        var repository = new InMemoryDownloadTaskRepository();
        var service = new IncrementalComparisonService(
            registry, new ContentSourceScanService(), repository,
            new ContentComparisonPolicy(new NoFiles()));
        var baseline = new IncrementalBaselineSaveData
        {
            BoundaryItemKeys =
            [
                new ContentItemKeySaveData { SourceKind = nameof(ContentSourceKind.Uploader), NativeId = "aid:old" },
            ],
        };

        var result = await service.CheckAsync(
            provider.Descriptor,
            new SourceFilterRules(keyword: "匹配"),
            baseline,
            new RenditionSpecification(80, 0, VideoCodecPreference.AutoCompatibility,
                OutputContainer.Mp4, OutputMediaMode.AudioVideo),
            null,
            CancellationToken.None);

        Assert.True(result.IsComplete);
        Assert.NotNull(result.ProposedBaseline);
        Assert.Equal(2, result.ProposedBaseline!.BoundaryItemKeys.Count);
        Assert.Contains(result.Results, item => item.Status == ContentComparisonStatus.New && item.Title == "匹配");
        Assert.Contains(result.Results, item => item.Status == ContentComparisonStatus.RuleExcluded && item.Title == "排除");
        Assert.Contains(result.Results, item => item.Status == ContentComparisonStatus.Invalid);
        Assert.Empty(repository.Tasks); // 检查更新不能写任务。
    }

    [Fact]
    public async Task 同一媒体来自多个来源项时只产生一个输出比较项()
    {
        var provider = TestIncrementalProvider.Flat(
            new ContentSourceItem(new(ContentSourceKind.Uploader, "source:a"), "A", ContentSourceItemType.Video, aid: 1, cid: 2),
            new ContentSourceItem(new(ContentSourceKind.Uploader, "source:b"), "B", ContentSourceItemType.Video, aid: 1, cid: 2));
        var service = new IncrementalComparisonService(
            new ContentSourceProviderRegistry([provider]), new ContentSourceScanService(),
            new InMemoryDownloadTaskRepository(), new ContentComparisonPolicy(new NoFiles()));

        var result = await service.CheckAsync(provider.Descriptor, SourceFilterRules.Empty,
            new IncrementalBaselineSaveData(),
            new RenditionSpecification(80, 0, VideoCodecPreference.AutoCompatibility,
                OutputContainer.Mp4, OutputMediaMode.AudioVideo), null, CancellationToken.None);

        var item = Assert.Single(result.Results, item => item.MediaUnitKey.HasValue);
        Assert.Equal(2, item.SourceKeys.Count);
    }

    private sealed class NoFiles : IOutputFileFactProvider
    {
        public bool Exists(string path) => false;
    }
}

public sealed class DownloadIdentityPersistenceG5Tests
{
    [Fact]
    public async Task 新库写入身份并建立非唯一索引()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        await store.InsertBatchAsync(
        [
            new DownloadTaskRecord
            {
                TaskId = "task",
                DocumentId = "doc",
                Aid = 1,
                Cid = 2,
                MediaUnitKey = "mu1:1:2",
                RenditionFingerprint = RenditionFingerprint.Create(
                    new MediaUnitKey(1, 2),
                    new RenditionSpecification(80, 0, VideoCodecPreference.AutoCompatibility,
                        OutputContainer.Mp4, OutputMediaMode.AudioVideo)).Value,
            },
        ]);

        var restored = Assert.Single(await store.GetByIdentityAsync([new MediaUnitKey(1, 2)], []));
        Assert.Equal("mu1:1:2", restored.MediaUnitKey);
        Assert.StartsWith("rf1:", restored.RenditionFingerprint);

        await using var connection = new SqliteConnection($"Data Source={paths.DownloadTaskDatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'ix_download_tasks_%';";
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        Assert.Contains("ix_download_tasks_media_unit_key", names);
        Assert.Contains("ix_download_tasks_rendition_fingerprint", names);
    }
}

public sealed class IncrementalSubmissionG5Tests
{
    [Fact]
    public async Task 预检合并同批相同输出版本()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var repository = new InMemoryDownloadTaskRepository();
        var service = CreatePreflight(repository);
        var submission = Submission(paths, 80,
        [
            Item("one", 1, 2),
            Item("two", 1, 2),
        ]);

        var report = await service.InspectAsync(submission);

        Assert.Equal(1, report.ReadyCount);
        Assert.Equal(1, report.SkipCount);
        Assert.Contains(report.Items.SelectMany(item => item.Issues), issue => issue.Code == "duplicate_in_batch");
    }

    [Fact]
    public async Task 比较后出现同指纹任务时报告过期而不是部分提交()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var repository = new InMemoryDownloadTaskRepository();
        var spec = new RenditionSpecification(80, 0, VideoCodecPreference.AutoCompatibility,
            OutputContainer.Mp4, OutputMediaMode.AudioVideo);
        var fingerprint = RenditionFingerprint.Create(new MediaUnitKey(1, 2), spec).Value;
        await repository.InsertBatchAsync(
        [
            new DownloadTaskRecord
            {
                TaskId = "other",
                DocumentId = "other-doc",
                Aid = 1,
                Cid = 2,
                MediaUnitKey = "mu1:1:2",
                RenditionFingerprint = fingerprint,
                Status = "pending",
            },
        ]);
        var submission = Submission(paths, 80, [Item("new", 1, 2)]) with
        {
            IncrementalExpectation = new IncrementalSubmissionExpectation("cmp1:test", [fingerprint]),
        };

        var report = await CreatePreflight(repository).InspectAsync(submission);

        Assert.True(report.IsBlocked);
        Assert.Contains(report.Items.SelectMany(item => item.Issues), issue => issue.Code == "stale_comparison");
    }

    [Fact]
    public async Task 同一媒体不同画质允许作为不同输出版本()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var repository = new InMemoryDownloadTaskRepository();
        var oldFingerprint = RenditionFingerprint.Create(
            new MediaUnitKey(1, 2),
            new RenditionSpecification(80, 0, VideoCodecPreference.AutoCompatibility,
                OutputContainer.Mp4, OutputMediaMode.AudioVideo)).Value;
        await repository.InsertBatchAsync(
        [
            new DownloadTaskRecord
            {
                TaskId = "old",
                DocumentId = "doc-a",
                Aid = 1,
                Cid = 2,
                MediaUnitKey = "mu1:1:2",
                RenditionFingerprint = oldFingerprint,
                Status = "pending",
            },
        ]);

        var report = await CreatePreflight(repository).InspectAsync(
            Submission(paths, 64, [Item("new", 1, 2)]));

        Assert.False(report.IsBlocked);
        Assert.Equal(1, report.ReadyCount);
    }

    private static SubmissionPreflightService CreatePreflight(InMemoryDownloadTaskRepository repository) => new(
        new FakeCredentialProvider(),
        new FakeFfmpegService { ReadyOverride = true },
        repository,
        new FixedEstimator(),
        new FixedCapacity());

    private static DownloadSubmission Submission(TestDataPaths paths, int quality, DownloadSubmissionItem[] items) =>
        new("doc", "文档", "系列",
            new DownloadProfileSnapshot(quality, 0, paths.RootDirectory, false, false,
                false, false, false, "{title}"), items);

    private static DownloadSubmissionItem Item(string id, long aid, long cid) =>
        new(id, id, aid, $"BV{aid}", cid, 60, BiliMediaType.Video, 0, 0, "");

    private sealed class FixedEstimator : IMediaSizeEstimator
    {
        public Task<long?> EstimatePeakBytesAsync(
            DownloadSubmissionItem item, DownloadProfileSnapshot profile, CancellationToken cancellationToken) =>
            Task.FromResult<long?>(100);
    }

    private sealed class FixedCapacity : IStorageCapacityProvider
    {
        public long? GetAvailableBytes(string path) => 1_000_000;
    }
}

public sealed class IncrementalComparisonViewModelG5Tests
{
    [Fact]
    public async Task 完整结果仅默认选择新增并写回基线()
    {
        var descriptor = new ContentSourceDescriptor(ContentSourceKind.Uploader, "uploader:1", "UP", null, 1);
        IncrementalBaselineSaveData? written = null;
        var service = new StubComparisonService();
        var vm = new IncrementalComparisonViewModel(
            service,
            () => descriptor,
            () => SourceFilterRules.Empty,
            () => new IncrementalBaselineSaveData(),
            baseline => written = baseline,
            () => new RenditionSpecification(80, 0, VideoCodecPreference.AutoCompatibility,
                OutputContainer.Mp4, OutputMediaMode.AudioVideo));
        vm.RefreshCapability();

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.True(vm.IsSupported);
        Assert.NotNull(written);
        Assert.Single(vm.AllItems, item => item.IsSelected);
        Assert.Equal(ContentComparisonStatus.New, vm.AllItems.Single(item => item.IsSelected).Result.Status);
        Assert.True(vm.CanUseSelected);
    }

    [Fact]
    public void 课程来源不开放增量检查()
    {
        var descriptor = new ContentSourceDescriptor(ContentSourceKind.Course, "course:1", "课程", null, 1);
        var vm = new IncrementalComparisonViewModel(
            new StubComparisonService(), () => descriptor, () => SourceFilterRules.Empty,
            () => new IncrementalBaselineSaveData(), _ => { }, () => null);

        vm.RefreshCapability();

        Assert.False(vm.IsSupported);
        Assert.Contains("仅支持浏览", vm.Status);
    }

    private sealed class StubComparisonService : IIncrementalComparisonService
    {
        public Task<IncrementalComparisonSnapshot> CheckAsync(
            ContentSourceDescriptor descriptor,
            SourceFilterRules rules,
            IncrementalBaselineSaveData baseline,
            RenditionSpecification rendition,
            IProgress<IncrementalScanProgress>? progress,
            CancellationToken cancellationToken)
        {
            var media = new MediaUnitKey(1, 2);
            var item = new BiliVideoItem { Aid = 1, Cid = 2, Title = "新增", MediaUnitKey = media };
            var results = new ContentComparisonResult[]
            {
                new(media, RenditionFingerprint.Create(media, rendition), ContentComparisonStatus.New,
                    "新增", [new(ContentSourceKind.Uploader, "aid:1")], item, []),
                new(null, null, ContentComparisonStatus.RuleExcluded,
                    "排除", [new(ContentSourceKind.Uploader, "aid:2")], null, []),
            };
            return Task.FromResult(new IncrementalComparisonSnapshot(
                results, true, "cmp1:test",
                new IncrementalBaselineSaveData
                {
                    LastCompletedCheckAtUtc = DateTimeOffset.UtcNow,
                    SnapshotToken = "scan1:test",
                }, []));
        }

        public Task<IncrementalComparisonSnapshot> ReclassifyAsync(
            IncrementalSourceScanSnapshot sourceSnapshot,
            IncrementalBaselineSaveData baseline,
            RenditionSpecification rendition,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

internal sealed class TestIncrementalProvider : IContentSourceProvider, IContentSourceResolutionProvider
{
    private readonly Func<ContentPageRequest, ContentPage> _pages;

    private TestIncrementalProvider(Func<ContentPageRequest, ContentPage> pages)
    {
        _pages = pages;
        Descriptor = new ContentSourceDescriptor(ContentSourceKind.Uploader, "uploader:1", "测试来源", null, 1);
    }

    public ContentSourceDescriptor Descriptor { get; }
    public List<ContentPageRequest> Calls { get; } = [];
    public Action? OnFirstPage { get; set; }
    public ContentSourceKind Kind => ContentSourceKind.Uploader;
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPaging |
        ContentSourceCapabilities.SupportsChildPaging | ContentSourceCapabilities.SupportsIncremental;
    public int CapabilityVersion => 1;

    public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Descriptor);

    public Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor, ContentPageRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(request);
        if (Calls.Count == 1) OnFirstPage?.Invoke();
        return Task.FromResult(_pages(request));
    }

    public Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor, ContentSourceItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var aid = item.Aid ?? 1;
        var cid = item.Cid ?? aid * 10;
        return Task.FromResult(new BiliVideoCollection
        {
            SeriesTitle = item.Title,
            Items =
            [
                new BiliVideoItem
                {
                    Aid = aid,
                    Cid = cid,
                    Bvid = item.Bvid ?? $"BV{aid}",
                    Title = item.Title,
                    OriginalTitle = item.Title,
                    MediaUnitKey = new MediaUnitKey(aid, cid),
                },
            ],
        });
    }

    public static TestIncrementalProvider Flat(params ContentSourceItem[] items) =>
        new(request => new ContentPage(items, null, false, "stable"));

    public static TestIncrementalProvider Hierarchical()
    {
        var folder = new ContentItemKey(ContentSourceKind.Uploader, "folder:1");
        return new(request => request.ParentKey is null
            ? new ContentPage(
            [
                new ContentSourceItem(folder, "合集", ContentSourceItemType.Collection,
                    nodeKind: ContentSourceNodeKind.Container, childCount: 2),
            ], null, false, "root")
            : new ContentPage(
            [
                new ContentSourceItem(new(ContentSourceKind.Uploader, "aid:1"), "一", ContentSourceItemType.Video, aid: 1, cid: 11),
                new ContentSourceItem(new(ContentSourceKind.Uploader, "aid:2"), "二", ContentSourceItemType.Video, aid: 2, cid: 22),
            ], null, false, "child"));
    }

    public static TestIncrementalProvider Looping() => new(request => request.ContinuationToken switch
    {
        null => new ContentPage(
            [new ContentSourceItem(new(ContentSourceKind.Uploader, "aid:1"), "一", ContentSourceItemType.Video, aid: 1, cid: 11)],
            "loop", true, "stable"),
        _ => new ContentPage([], "loop", true, "stable"),
    });

    public static TestIncrementalProvider CancelAfterFirst() => Flat(
        new ContentSourceItem(new(ContentSourceKind.Uploader, "aid:1"), "一", ContentSourceItemType.Video, aid: 1, cid: 11));
}
