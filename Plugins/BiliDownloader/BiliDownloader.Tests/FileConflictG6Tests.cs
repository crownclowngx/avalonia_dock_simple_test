using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Persistence;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.Tests;

/// <summary>
/// G6 文件冲突与提交预检测试。所有网络大小和磁盘容量都通过小接口替换，
/// 只在每项测试独享的临时目录内创建零字节冲突文件，避免依赖开发机磁盘状态。
/// </summary>
public sealed class FileConflictG6Tests
{
    [Fact]
    public async Task 自动序号_同时避开磁盘文件与同批名称()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        File.WriteAllText(Path.Combine(paths.RootDirectory, "标题.mp4"), "old");
        var service = CreateService(paths, out _, size: 1_000, available: 100_000);
        var report = await service.InspectAsync(CreateSubmission(paths, FileConflictPolicy.AutoNumber, 2));

        Assert.False(report.IsBlocked);
        Assert.Equal(2, report.ReadyCount);
        Assert.EndsWith("标题 (1).mp4", report.Items[0].OutputFilePath);
        Assert.EndsWith("标题 (2).mp4", report.Items[1].OutputFilePath);
        Assert.True(report.RequiresConfirmation);
    }

    [Fact]
    public async Task 跳过策略_冲突项不进入可提交集合()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        File.WriteAllText(Path.Combine(paths.RootDirectory, "标题.xml"), "old");
        var service = CreateService(paths, out _, size: 1_000, available: 100_000);

        var report = await service.InspectAsync(CreateSubmission(paths, FileConflictPolicy.Skip));

        Assert.Equal(0, report.ReadyCount);
        Assert.Equal(1, report.SkipCount);
        Assert.False(report.IsBlocked);
    }

    [Fact]
    public async Task 覆盖策略_冲突必须产生明确警告()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        File.WriteAllText(Path.Combine(paths.RootDirectory, "标题_cover.jpg"), "old");
        var service = CreateService(paths, out _, size: 1_000, available: 100_000);

        var report = await service.InspectAsync(CreateSubmission(paths, FileConflictPolicy.Overwrite));

        Assert.Equal(1, report.ReadyCount);
        Assert.True(report.RequiresConfirmation);
        Assert.Contains(report.Items[0].Issues, issue => issue.Code == "overwrite");
    }

    [Fact]
    public async Task G9全部语言模式_提交时重新枚举对应字幕格式冲突()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        File.WriteAllText(Path.Combine(paths.RootDirectory, "标题.zh-CN.vtt"), "old");
        var service = CreateService(paths, out _, size: 1_000, available: 100_000);
        var source = CreateSubmission(paths, FileConflictPolicy.AutoNumber);
        var submission = source with
        {
            Profile = source.Profile with
            {
                SubtitleOptions = new SubtitleOptions
                {
                    SelectionMode = SubtitleSelectionMode.All,
                    OutputFormat = SubtitleOutputFormat.Vtt,
                    DeliveryMode = SubtitleDeliveryMode.External,
                },
            },
        };

        var report = await service.InspectAsync(submission);

        Assert.EndsWith("标题 (1).mp4", report.Items[0].OutputFilePath);
        Assert.True(report.Items[0].HasConflict);
    }

    [Fact]
    public async Task G9选择语言和弹幕格式_仅检查最终会发布的附加文件()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        File.WriteAllText(Path.Combine(paths.RootDirectory, "标题.zh-CN.ass"), "old-subtitle");
        File.WriteAllText(Path.Combine(paths.RootDirectory, "标题.json"), "old-danmaku");
        var service = CreateService(paths, out _, size: 1_000, available: 100_000);
        var source = CreateSubmission(paths, FileConflictPolicy.AutoNumber);
        var submission = source with
        {
            Profile = source.Profile with
            {
                SubtitleOptions = new SubtitleOptions
                {
                    SelectionMode = SubtitleSelectionMode.SelectedLanguages,
                    LanguageKeys = ["zh-CN"],
                    OutputFormat = SubtitleOutputFormat.Ass,
                    DeliveryMode = SubtitleDeliveryMode.ExternalAndSoftMuxed,
                },
                DanmakuOptions = new DanmakuOptions { Formats = [DanmakuOutputFormat.Json] },
            },
        };

        var report = await service.InspectAsync(submission);

        Assert.EndsWith("标题 (1).mp4", report.Items[0].OutputFilePath);
        Assert.True(report.Items[0].HasConflict);
    }

    [Fact]
    public async Task Ffmpeg未就绪_整批阻止()
    {
        using var paths = new TestDataPaths();
        var service = CreateService(paths, out _, 1_000, 100_000, false, false);

        var report = await service.InspectAsync(CreateSubmission(paths, FileConflictPolicy.AutoNumber));

        Assert.True(report.IsBlocked);
        Assert.Contains(report.GlobalIssues, issue => issue.Code == "ffmpeg");
    }

    [Fact]
    public async Task 匿名公开内容仍执行媒体大小预检()
    {
        using var paths = new TestDataPaths();
        var estimator = new CountingSizeEstimator();
        var service = new SubmissionPreflightService(
            new FakeCredentialProvider { IsLoggedIn = false },
            new FakeFfmpegService { ReadyOverride = true },
            new InMemoryDownloadTaskRepository(), estimator,
            new FixedCapacityProvider(100_000));

        var report = await service.InspectAsync(CreateSubmission(paths, FileConflictPolicy.AutoNumber));

        Assert.False(report.IsBlocked);
        Assert.Equal(1, estimator.CallCount);
    }

    [Fact]
    public async Task 磁盘空间不足_报告需要量与可用量并阻止()
    {
        using var paths = new TestDataPaths();
        var service = CreateService(paths, out _, size: 10_000, available: 9_999);

        var report = await service.InspectAsync(CreateSubmission(paths, FileConflictPolicy.AutoNumber));

        Assert.True(report.IsBlocked);
        Assert.Contains(report.GlobalIssues, issue => issue.Code == "disk_insufficient");
    }

    [Fact]
    public async Task 大小未知_产生可确认警告而非静默放行()
    {
        using var paths = new TestDataPaths();
        var service = CreateService(paths, out _, size: null, available: null);

        var report = await service.InspectAsync(CreateSubmission(paths, FileConflictPolicy.AutoNumber));

        Assert.False(report.IsBlocked);
        Assert.True(report.RequiresConfirmation);
        Assert.Contains(report.GlobalIssues, issue => issue.Code == "disk_unknown");
    }

    [Fact]
    public async Task 校验续传_同一任务事实和合法长度才允许恢复()
    {
        using var paths = new TestDataPaths();
        var service = CreateService(paths, out var repository, 1_000, 100_000);
        var temp = Path.Combine(paths.TempDirectory, "old-task");
        Directory.CreateDirectory(temp);
        await File.WriteAllBytesAsync(Path.Combine(temp, "video.tmp"), new byte[10]);
        repository.Seed(new DownloadTaskRecord
        {
            TaskId = "old-task", DocumentId = "doc", Aid = 1, Cid = 2,
            QualityId = 80, AudioQualityId = 0, Status = "interrupted",
            TempDirectory = temp, ExpectedVideoBytes = 100,
            OutputFilePath = Path.Combine(paths.RootDirectory, "标题.mp4"),
        });

        var report = await service.InspectAsync(CreateSubmission(paths, FileConflictPolicy.ResumeVerified));

        Assert.False(report.IsBlocked);
        Assert.True(report.Items[0].IsResume);
        Assert.Equal("old-task", report.Items[0].ResumeTaskId);
        Assert.Contains(report.Items[0].Issues, issue => issue.Code == "resume");
    }

    [Fact]
    public async Task 校验续传_缺少预期长度会阻止()
    {
        using var paths = new TestDataPaths();
        var service = CreateService(paths, out var repository, 1_000, 100_000);
        var temp = Path.Combine(paths.TempDirectory, "old-task");
        Directory.CreateDirectory(temp);
        await File.WriteAllBytesAsync(Path.Combine(temp, "video.tmp"), new byte[10]);
        repository.Seed(new DownloadTaskRecord
        {
            TaskId = "old-task", DocumentId = "doc", Aid = 1, Cid = 2,
            QualityId = 80, Status = "paused", TempDirectory = temp,
            OutputFilePath = Path.Combine(paths.RootDirectory, "标题.mp4"),
        });

        var report = await service.InspectAsync(CreateSubmission(paths, FileConflictPolicy.ResumeVerified));

        Assert.True(report.IsBlocked);
        Assert.Contains(report.Items[0].Issues, issue => issue.Code == "resume_invalid");
    }

    [Fact]
    public async Task 已有同任务ID_阻止静默替换数据库事实()
    {
        using var paths = new TestDataPaths();
        var service = CreateService(paths, out var repository, 1_000, 100_000);
        repository.Seed(new DownloadTaskRecord
        {
            TaskId = "item-1", DocumentId = "doc", ItemTitle = "旧事实", Status = "done",
        });

        var report = await service.InspectAsync(CreateSubmission(paths, FileConflictPolicy.AutoNumber));

        Assert.True(report.IsBlocked);
        Assert.Contains(report.Items[0].Issues, issue => issue.Code == "task_exists");
    }

    [Fact]
    public void 当前Document缺少可选冲突字段_默认自动序号()
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<DocumentSaveDataV3>("{\"DocumentId\":\"current\"}");
        Assert.NotNull(dto);
        Assert.Equal(FileConflictPolicy.AutoNumber, dto!.ConflictPolicy);
    }

    [Fact]
    public async Task SQLite路径保留_同一路径只允许一个活动任务且完成后释放()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var key = SubmissionPreflightService.NormalizePathKey(Path.Combine(paths.RootDirectory, "same.mp4"));
        var first = NewReservedRecord("one", key, paths);
        var second = NewReservedRecord("two", key, paths);

        await store.InsertBatchAsync([first]);
        await Assert.ThrowsAsync<SqliteException>(() => store.InsertBatchAsync([second]));
        await store.MarkCompletedAsync(first.TaskId, first.OutputFilePath, null, DateTime.Now);
        await store.InsertBatchAsync([second]);

        Assert.Equal(2, (await store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Coordinator提交前文件事实变化_拒绝旧预检报告()
    {
        using var paths = new TestDataPaths();
        var preflight = CreateService(paths, out var repository, 1_000, 100_000);
        var submission = CreateSubmission(paths, FileConflictPolicy.AutoNumber);
        var report = await preflight.InspectAsync(submission);
        File.WriteAllText(Path.Combine(paths.RootDirectory, "标题.mp4"), "external");
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedBiliDownloaderEventBus(), new NoOpDownloadProgressTracker(),
            new FakeDownloadTaskExecutor(), paths, new FakeCredentialProvider(),
            new DownloadRecoveryService(repository));

        var result = await coordinator.CommitPreparedAsync(
            new PreparedSubmission(report, false), preflight);

        Assert.Equal(SubmissionCommitStatus.Stale, result.Status);
        Assert.Empty(repository.Tasks);
        await coordinator.ShutdownAsync();
    }

    private static SubmissionPreflightService CreateService(
        TestDataPaths paths,
        out InMemoryDownloadTaskRepository repository,
        long? size,
        long? available,
        bool loggedIn = true,
        bool ffmpegReady = true)
    {
        repository = new InMemoryDownloadTaskRepository();
        return new SubmissionPreflightService(
            new FakeCredentialProvider { IsLoggedIn = loggedIn },
            new FakeFfmpegService { ReadyOverride = ffmpegReady },
            repository,
            new FixedSizeEstimator(size),
            new FixedCapacityProvider(available));
    }

    private static DownloadSubmission CreateSubmission(
        TestDataPaths paths, FileConflictPolicy policy, int count = 1)
        => new("doc", "工作台", "系列",
            new DownloadProfileSnapshot(80, 0, paths.RootDirectory, false, false,
                false, false, false, "{title}", ConflictPolicy: policy),
            Enumerable.Range(1, count)
                .Select(index => new DownloadSubmissionItem(
                    $"item-{index}", "标题", 1, "BV1", index + 1, 60,
                    BiliMediaType.Video, 0, 0, ""))
                .ToArray());

    private static DownloadTaskRecord NewReservedRecord(string id, string key, TestDataPaths paths)
        => new()
        {
            TaskId = id,
            DocumentId = "doc",
            ItemTitle = id,
            OutputDirectory = paths.RootDirectory,
            OutputFilePath = Path.Combine(paths.RootDirectory, "same.mp4"),
            OutputPathKey = key,
            Status = "pending",
        };

    private sealed class FixedSizeEstimator(long? size) : IMediaSizeEstimator
    {
        public Task<long?> EstimatePeakBytesAsync(
            DownloadSubmissionItem item, DownloadProfileSnapshot profile, CancellationToken cancellationToken)
            => Task.FromResult(size);
    }

    private sealed class FixedCapacityProvider(long? available) : IStorageCapacityProvider
    {
        public long? GetAvailableBytes(string path) => available;
    }

    private sealed class CountingSizeEstimator : IMediaSizeEstimator
    {
        public int CallCount { get; private set; }

        public Task<long?> EstimatePeakBytesAsync(
            DownloadSubmissionItem item, DownloadProfileSnapshot profile, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<long?>(1_000);
        }
    }
}
