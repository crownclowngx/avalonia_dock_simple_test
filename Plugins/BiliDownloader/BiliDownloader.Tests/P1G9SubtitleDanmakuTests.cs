using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Download.Extras;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels.BiliDownloader;
using ProtoBuf;

namespace BiliDownloader.Tests;

public sealed class P1G9SubtitleDanmakuTests
{
    [Fact]
    public void 字幕和弹幕配置规范化为稳定顺序()
    {
        var subtitle = new SubtitleOptions
        {
            SelectionMode = SubtitleSelectionMode.SelectedLanguages,
            LanguageKeys = [" en ", "ZH-cn", "EN", ""],
            OutputFormat = SubtitleOutputFormat.Ass,
            DeliveryMode = SubtitleDeliveryMode.ExternalAndSoftMuxed,
        }.Canonicalize();
        var danmaku = new DanmakuOptions
        {
            Formats = [DanmakuOutputFormat.Json, DanmakuOutputFormat.Xml, DanmakuOutputFormat.Json],
            AssStyleId = " default ",
        }.Canonicalize();

        Assert.Equal(["en", "ZH-cn"], subtitle.LanguageKeys);
        Assert.Equal([DanmakuOutputFormat.Xml, DanmakuOutputFormat.Json], danmaku.Formats);
        Assert.Equal("default", danmaku.AssStyleId);
    }

    [Fact]
    public void 结构化配置拒绝空语言和未知样式()
    {
        Assert.Throws<ArgumentException>(() => new SubtitleOptions
        {
            SelectionMode = SubtitleSelectionMode.SelectedLanguages,
        }.Validate());
        Assert.Throws<ArgumentException>(() => new DanmakuOptions
        {
            Formats = [DanmakuOutputFormat.Ass],
            AssStyleId = "custom",
        }.Validate());
    }

    [Fact]
    public void 字幕时间轴过滤无效项并稳定排序()
    {
        var cues = SubtitleCueNormalizer.Normalize([
            (2d, 3d, "二"),
            (-1d, 1d, "一\r\n换行"),
            (double.NaN, 2d, "坏"),
            (3d, 3d, "坏"),
            (1d, 2d, " "),
        ]);

        Assert.Equal(2, cues.Count);
        Assert.Equal(TimeSpan.Zero, cues[0].Start);
        Assert.Equal("一\n换行", cues[0].Text);
        Assert.Equal("二", cues[1].Text);
    }

    [Fact]
    public void 三种字幕格式正确处理Unicode换行和长小时数()
    {
        var cues = new[] { new SubtitleCue(TimeSpan.FromHours(25), TimeSpan.FromHours(25) + TimeSpan.FromSeconds(1.23), "中{文}\\行\n二", 0) };

        var srt = new SrtSubtitleFormatter().Format(cues);
        var vtt = new VttSubtitleFormatter().Format(cues);
        var ass = new AssSubtitleFormatter().Format(cues);

        Assert.Contains("25:00:00,000 --> 25:00:01,230", srt, StringComparison.Ordinal);
        Assert.StartsWith("WEBVTT", vtt, StringComparison.Ordinal);
        Assert.Contains("25:00:00.000 --> 25:00:01.230", vtt, StringComparison.Ordinal);
        Assert.Contains(@"中\{文\}\\行\N二", ass, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 字幕目录同语言官方优先并确定排序()
    {
        var api = new FakeSubtitleApi
        {
            Tracks =
            [
                new("zh-CN", "中文 AI", SubtitleSourceType.AiGenerated, "2", "u2"),
                new("en", "English", SubtitleSourceType.Official, "3", "u3"),
                new("ZH-cn", "中文", SubtitleSourceType.Official, "1", "u1"),
            ],
        };

        var tracks = await new SubtitleCatalogService(api)
            .GetPreferredTracksAsync(1, 2, "cookie");

        Assert.Equal(2, tracks.Count);
        Assert.Equal("en", tracks[0].StableLanguageKey);
        Assert.Equal(SubtitleSourceType.Official, tracks[1].SourceType);
        Assert.Equal("1", tracks[1].PlatformTrackId);
    }

    [Fact]
    public void 弹幕XML_JSON_ASS均确定性输出并正确转义()
    {
        var items = new[]
        {
            new DanmakuElem { Id = 2, Progress = 1000, Mode = 1, Fontsize = 25, Color = 0xff0000, Content = "<二>&" },
            new DanmakuElem { Id = 1, Progress = 500, Mode = 5, Fontsize = 25, Color = 0xffffff, Content = "一{a}" },
            new DanmakuElem { Id = 2, Progress = 1000, Mode = 1, Fontsize = 25, Color = 0xff0000, Content = "重复" },
        };

        var xml = new XmlDanmakuFormatter().Format(items);
        var json = new JsonDanmakuFormatter().Format(items);
        var ass = new AssDanmakuFormatter().Format(items);

        Assert.Contains("&lt;二&gt;&amp;", xml, StringComparison.Ordinal);
        Assert.True(xml.IndexOf("一{a}", StringComparison.Ordinal) < xml.IndexOf("&lt;二", StringComparison.Ordinal));
        Assert.Contains("progressMilliseconds", json, StringComparison.Ordinal);
        Assert.Contains("PlayResX: 1920", ass, StringComparison.Ordinal);
        Assert.Contains(@"一\{a\}", ass, StringComparison.Ordinal);
        Assert.Equal(ass, new AssDanmakuFormatter().Format(items));
    }

    [Fact]
    public void 附加资源摘要支持幂等合并和旧文本兼容()
    {
        var first = new ExtrasExecutionSummary
        {
            Items = [new("subtitle:zh:srt:external", ExtrasItemStatus.Failed, "network")],
        };
        var merged = first.Merge([new("subtitle:zh:srt:external", ExtrasItemStatus.Success)]);
        var roundTrip = ExtrasExecutionSummaryCodec.Deserialize(ExtrasExecutionSummaryCodec.Serialize(merged));
        var legacy = ExtrasExecutionSummaryCodec.Deserialize("subtitle: FAIL");

        Assert.False(roundTrip.HasRetryableFailures);
        Assert.Equal(ExtrasItemStatus.Success, Assert.Single(roundTrip.Items).Status);
        Assert.Equal(ExtrasItemStatus.LegacyUnknown, Assert.Single(legacy.Items).Status);
    }

    [Fact]
    public async Task 软字幕成功后原子替换主文件并返回逐语言成功()
    {
        using var directory = new TemporaryDirectory();
        var main = Path.Combine(directory.Path, "video.mp4");
        await File.WriteAllTextAsync(main, "base");
        var api = CreateSingleTrackApi();
        var muxer = new CopyingSubtitleMuxer("muxed");
        var handler = CreateSubtitleHandler(api, muxer, new PassingSubtitleVerifier());
        var context = CreateExtrasContext(directory.Path, main, SubtitleDeliveryMode.SoftMuxed);

        var result = await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("muxed", await File.ReadAllTextAsync(main));
        Assert.Equal(ExtrasItemStatus.Success, Assert.Single(result.Items).Status);
        Assert.Equal(1, muxer.CallCount);
    }

    [Fact]
    public async Task 软字幕验证失败保留原主文件并产生可重试结果()
    {
        using var directory = new TemporaryDirectory();
        var main = Path.Combine(directory.Path, "video.mp4");
        await File.WriteAllTextAsync(main, "base");
        var api = CreateSingleTrackApi();
        var handler = CreateSubtitleHandler(api, new CopyingSubtitleMuxer("candidate"), new FailingSubtitleVerifier());

        var result = await handler.ExecuteAsync(
            CreateExtrasContext(directory.Path, main, SubtitleDeliveryMode.SoftMuxed), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("base", await File.ReadAllTextAsync(main));
        Assert.Equal(ExtrasItemStatus.PartialSuccess, Assert.Single(result.Items).Status);
        Assert.True(Assert.Single(result.Items).IsRetryable);
    }

    [Fact]
    public async Task 所选语言部分缺失时逐语言记录Unavailable()
    {
        using var directory = new TemporaryDirectory();
        var main = Path.Combine(directory.Path, "video.mp4");
        await File.WriteAllTextAsync(main, "base");
        var api = CreateSingleTrackApi();
        var handler = CreateSubtitleHandler(api, new CopyingSubtitleMuxer("unused"), new PassingSubtitleVerifier());
        var source = CreateExtrasContext(directory.Path, main, SubtitleDeliveryMode.External);
        var context = CopyContext(source, subtitle: new SubtitleOptions
        {
            SelectionMode = SubtitleSelectionMode.SelectedLanguages,
            LanguageKeys = ["zh-CN", "en"],
            OutputFormat = SubtitleOutputFormat.Srt,
            DeliveryMode = SubtitleDeliveryMode.External,
        });

        var result = await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.Contains(result.Items, item => item.Key.StartsWith("subtitle:zh-cn", StringComparison.Ordinal)
            && item.Status == ExtrasItemStatus.Success);
        Assert.Contains(result.Items, item => item.Key.StartsWith("subtitle:en", StringComparison.Ordinal)
            && item.Status == ExtrasItemStatus.Unavailable && !item.IsRetryable);
    }

    [Fact]
    public async Task 弹幕单段失败仍生成各格式PartialSuccess()
    {
        using var directory = new TemporaryDirectory();
        var api = new FakeDanmakuApi(CreateDanmakuBytes(), failSegment: 2);
        var handler = new DanmakuExtrasHandler(api,
            new DanmakuFormatterRegistry([new XmlDanmakuFormatter(), new JsonDanmakuFormatter()]),
            new FixedDanmakuRequestPacer(TimeSpan.Zero));
        var context = CreateExtrasContext(directory.Path, Path.Combine(directory.Path, "video.mp4"), SubtitleDeliveryMode.External);
        context = CopyContext(context, duration: 720, danmaku: new DanmakuOptions
        {
            Formats = [DanmakuOutputFormat.Xml, DanmakuOutputFormat.Json],
        });

        var result = await handler.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(2, result.OutputFiles.Count);
        Assert.All(result.Items, item => Assert.Equal(ExtrasItemStatus.PartialSuccess, item.Status));
        Assert.All(result.Items, item => Assert.Equal([2], item.FailedSegments));
    }

    [Fact]
    public async Task 弹幕独立重试只补失败分段并复用成功缓存()
    {
        using var directory = new TemporaryDirectory();
        var api = new FakeDanmakuApi(CreateDanmakuBytes(), failSegment: 2);
        var handler = new DanmakuExtrasHandler(api,
            new DanmakuFormatterRegistry([new XmlDanmakuFormatter()]),
            new FixedDanmakuRequestPacer(TimeSpan.Zero));
        var source = CreateExtrasContext(directory.Path, Path.Combine(directory.Path, "video.mp4"), SubtitleDeliveryMode.External);
        var initial = CopyContext(source, duration: 720,
            danmaku: new DanmakuOptions { Formats = [DanmakuOutputFormat.Xml] });
        var first = await handler.ExecuteAsync(initial, CancellationToken.None);
        Assert.Equal(ExtrasItemStatus.PartialSuccess, Assert.Single(first.Items).Status);

        api.FailSegment = 0;
        api.RequestedSegments.Clear();
        var retry = CopyContext(initial,
            retryKeys: new HashSet<string>(["danmaku:xml"], StringComparer.OrdinalIgnoreCase),
            retryFailedSegments: new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
            {
                ["danmaku:xml"] = [2],
            });
        var second = await handler.ExecuteAsync(retry, CancellationToken.None);

        Assert.Equal([2], api.RequestedSegments);
        Assert.Equal(ExtrasItemStatus.Success, Assert.Single(second.Items).Status);
    }

    [Fact]
    public async Task SQLite重复迁移并往返结构化附加资源快照()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        await store.InitAsync();
        await store.InsertBatchAsync([new DownloadTaskRecord
        {
            TaskId = "g9",
            DocumentId = "doc",
            SubmissionSnapshotVersion = 3,
            Status = "done",
            SubtitleOptions = new SubtitleOptions
            {
                SelectionMode = SubtitleSelectionMode.SelectedLanguages,
                LanguageKeys = ["zh-CN"],
                OutputFormat = SubtitleOutputFormat.Ass,
                DeliveryMode = SubtitleDeliveryMode.ExternalAndSoftMuxed,
            },
            DanmakuOptions = new DanmakuOptions { Formats = [DanmakuOutputFormat.Xml, DanmakuOutputFormat.Json] },
            ExtrasResultSummary = ExtrasExecutionSummaryCodec.Serialize(new ExtrasExecutionSummary
            {
                Items = [new("danmaku:xml", ExtrasItemStatus.Success)],
            }),
        }]);

        var task = Assert.Single(await store.GetAllAsync());
        Assert.Equal(["zh-CN"], task.SubtitleOptions.LanguageKeys);
        Assert.Equal(SubtitleDeliveryMode.ExternalAndSoftMuxed, task.SubtitleOptions.DeliveryMode);
        Assert.Equal(2, task.DanmakuOptions.Formats.Count);
        Assert.Equal(ExtrasItemStatus.Success, Assert.Single(task.ExtrasExecutionSummary.Items).Status);
    }

    [Fact]
    public async Task Coordinator附加资源重试互斥且不改变主任务完成状态()
    {
        using var paths = new TestDataPaths();
        var main = Path.Combine(paths.RootDirectory, "completed.mp4");
        Directory.CreateDirectory(paths.RootDirectory);
        await File.WriteAllTextAsync(main, "media");
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(new DownloadTaskRecord
        {
            TaskId = "extras-retry",
            DocumentId = "doc",
            Status = "done",
            OutputFilePath = main,
            ExtrasResultSummary = ExtrasExecutionSummaryCodec.Serialize(new ExtrasExecutionSummary
            {
                Items = [new("danmaku:xml", ExtrasItemStatus.PartialSuccess, FailedSegments: [2])],
            }),
        });
        var executor = new BlockingExtrasRetryExecutor();
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedBiliDownloaderEventBus(), new NoOpDownloadProgressTracker(),
            executor, paths);

        var first = coordinator.RetryFailedExtrasAsync("extras-retry");
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.RetryFailedExtrasAsync("extras-retry"));
        executor.Release.TrySetResult();
        var summary = await first;

        Assert.Equal(1, executor.CallCount);
        Assert.Equal("done", repository.Tasks.Single().Status);
        Assert.Equal(ExtrasItemStatus.Success, Assert.Single(summary.Items).Status);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 配置ViewModel只在用户命令时检测且失败保留旧语言()
    {
        var calls = 0;
        var fail = false;
        var vm = new DownloadConfigViewModel(new InMemorySettingsRepository(),
            subtitleDiscovery: _ =>
            {
                calls++;
                if (fail) throw new IOException("catalog unavailable");
                return Task.FromResult<IReadOnlyList<SubtitleLanguageAvailability>>
                ([new("zh-CN", "中文", SubtitleSourceType.Official, 2, 3)]);
            });
        vm.IsSubtitleEnabled = true;
        Assert.Equal(0, calls);

        await vm.DetectSubtitlesCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);
        var language = Assert.Single(vm.SubtitleLanguageOptions);
        language.IsSelected = true;
        vm.SelectedSubtitleSelectionMode = SubtitleSelectionMode.SelectedLanguages;
        Assert.Equal(["zh-CN"], vm.SubtitleOptions.LanguageKeys);

        fail = true;
        await vm.DetectSubtitlesCommand.ExecuteAsync(null);
        Assert.Equal(2, calls);
        Assert.Single(vm.SubtitleLanguageOptions);
        Assert.Equal(["zh-CN"], vm.SubtitleOptions.LanguageKeys);
    }

    [Fact]
    public void Ffprobe字幕验证同时检查编码语言和标题()
    {
        const string json = """
            {"streams":[{"codec_type":"subtitle","codec_name":"mov_text","tags":{"language":"zh-CN","title":"中文"}}]}
            """;
        var tracks = new[] { new SubtitleMuxTrack("x.srt", "zh-CN", "中文", SubtitleOutputFormat.Srt) };

        FfprobeSubtitleTrackVerifier.ValidateJson(json, tracks, OutputContainer.Mp4);
        Assert.Throws<MediaValidationException>(() =>
            FfprobeSubtitleTrackVerifier.ValidateJson(json,
                [new SubtitleMuxTrack("x.srt", "en", "English", SubtitleOutputFormat.Srt)], OutputContainer.Mp4));
        Assert.Throws<MediaValidationException>(() =>
            FfprobeSubtitleTrackVerifier.ValidateJson(json,
                [tracks[0], new SubtitleMuxTrack("y.srt", "en", "English", SubtitleOutputFormat.Srt)],
                OutputContainer.Mp4));
    }

    private static FakeSubtitleApi CreateSingleTrackApi() => new()
    {
        Tracks = [new("zh-CN", "中文", SubtitleSourceType.Official, "1", "memory://zh")],
        Cues = [new SubtitleCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "字幕", 0)],
    };

    private static SubtitleExtrasHandler CreateSubtitleHandler(
        FakeSubtitleApi api, ISubtitleMediaMuxer muxer, ISubtitleTrackVerifier verifier)
        => new(new SubtitleCatalogService(api), new SubtitleContentProvider(api),
            new SubtitleFormatterRegistry([new SrtSubtitleFormatter(), new AssSubtitleFormatter(), new VttSubtitleFormatter()]),
            muxer, verifier);

    private static ExtrasContext CreateExtrasContext(
        string directory, string mainPath, SubtitleDeliveryMode delivery) => new()
    {
        TaskId = "task",
        Aid = 1,
        Cid = 2,
        Duration = 1,
        OutputDirectory = directory,
        BaseFileName = "video",
        MainOutputPath = mainPath,
        TempDirectory = Path.Combine(directory, "temp"),
        OutputContainer = OutputContainer.Mp4,
        ApiService = new BiliApiService(),
        SubtitleOptions = new SubtitleOptions
        {
            SelectionMode = SubtitleSelectionMode.All,
            OutputFormat = SubtitleOutputFormat.Srt,
            DeliveryMode = delivery,
        },
        DanmakuOptions = DanmakuOptions.LegacyEnabled,
    };

    private static ExtrasContext CopyContext(
        ExtrasContext source,
        int? duration = null,
        DanmakuOptions? danmaku = null,
        SubtitleOptions? subtitle = null,
        IReadOnlySet<string>? retryKeys = null,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? retryFailedSegments = null) => new()
    {
        TaskId = source.TaskId,
        Aid = source.Aid,
        Cid = source.Cid,
        Duration = duration ?? source.Duration,
        OutputDirectory = source.OutputDirectory,
        BaseFileName = source.BaseFileName,
        MainOutputPath = source.MainOutputPath,
        TempDirectory = source.TempDirectory,
        OutputContainer = source.OutputContainer,
        ApiService = source.ApiService,
        SubtitleOptions = subtitle ?? source.SubtitleOptions,
        DanmakuOptions = danmaku ?? source.DanmakuOptions,
        RetryItemKeys = retryKeys ?? source.RetryItemKeys,
        RetryFailedSegments = retryFailedSegments ?? source.RetryFailedSegments,
    };

    private static byte[] CreateDanmakuBytes()
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, new DanmakuEvent
        {
            Elems = [new DanmakuElem { Id = 1, Progress = 100, Mode = 1, Fontsize = 25, Color = 0xffffff, Content = "测试" }],
        });
        return stream.ToArray();
    }

    private sealed class FakeSubtitleApi : IBiliSubtitleApi
    {
        public IReadOnlyList<SubtitleTrackDescriptor> Tracks { get; init; } = [];
        public IReadOnlyList<SubtitleCue> Cues { get; init; } = [];
        public Task<IReadOnlyList<SubtitleTrackDescriptor>> GetSubtitleTracksAsync(
            long aid, long cid, string cookie, CancellationToken cancellationToken = default)
            => Task.FromResult(Tracks);
        public Task<IReadOnlyList<SubtitleCue>> GetSubtitleCuesAsync(
            string subtitleUrl, string cookie, CancellationToken cancellationToken = default)
            => Task.FromResult(Cues);
    }

    private sealed class FakeDanmakuApi(byte[] payload, int failSegment) : IBiliDanmakuApi
    {
        public int FailSegment { get; set; } = failSegment;
        public List<int> RequestedSegments { get; } = [];
        public Task<byte[]> GetDanmakuSegmentAsync(
            long oid, int segmentIndex, long aid, string cookie, CancellationToken cancellationToken = default)
        {
            RequestedSegments.Add(segmentIndex);
            return segmentIndex == FailSegment
                ? Task.FromException<byte[]>(new IOException("segment failed"))
                : Task.FromResult(payload);
        }
    }

    private sealed class CopyingSubtitleMuxer(string content) : ISubtitleMediaMuxer
    {
        public int CallCount { get; private set; }
        public async Task MuxSubtitlesAsync(
            string inputMediaPath, IReadOnlyList<SubtitleMuxTrack> tracks, string outputPath,
            OutputContainer outputContainer, CancellationToken cancellationToken = default)
        {
            CallCount++;
            await File.WriteAllTextAsync(outputPath, content, cancellationToken);
        }
    }

    private sealed class PassingSubtitleVerifier : ISubtitleTrackVerifier
    {
        public Task VerifyAsync(string mediaPath, IReadOnlyList<SubtitleMuxTrack> expectedTracks,
            OutputContainer outputContainer, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FailingSubtitleVerifier : ISubtitleTrackVerifier
    {
        public Task VerifyAsync(string mediaPath, IReadOnlyList<SubtitleMuxTrack> expectedTracks,
            OutputContainer outputContainer, CancellationToken cancellationToken = default)
            => Task.FromException(new MediaValidationException("bad tracks"));
    }

    private sealed class BlockingExtrasRetryExecutor : IDownloadTaskExecutor, IExtrasRetryExecutor
    {
        public int CallCount { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DownloadExecutionResult> ExecuteAsync(
            DownloadTaskRecord task, Action<DownloadProgressInfo> onProgress,
            Action<long, long> onBytesChanged, CancellationToken cancellationToken)
            => throw new InvalidOperationException("本测试不应执行媒体下载。");

        public async Task<string?> ExecuteFailedExtrasAsync(
            DownloadTaskRecord task, CancellationToken cancellationToken)
        {
            CallCount++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return ExtrasExecutionSummaryCodec.Serialize(new ExtrasExecutionSummary
            {
                Items = [new("danmaku:xml", ExtrasItemStatus.Success)],
            });
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "BiliDownloaderG9", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
