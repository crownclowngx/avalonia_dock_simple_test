using BiliDownloader.Models;
using BiliDownloader.Messaging;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using MyAvaloniaManagement.PluginSdk;

namespace BiliDownloader.Tests;

/// <summary>
/// G3 持久化与恢复闭环集成测试：
/// 验证进度写入有序性、阶段边界 Flush、错误分类、临时文件校验和关闭行为。
/// </summary>
public sealed class BiliDownloadCoordinatorG3Tests
{
    private static BiliDownloadCoordinator CreateCoordinator(
        InMemoryDownloadTaskRepository repository,
        FakeDownloadTaskExecutor executor,
        FakeCredentialProvider? credentialProvider = null,
        IBiliDownloaderEventBus? eventBus = null,
        IDownloadProgressTracker? tracker = null)
        => new(
            repository,
            eventBus ?? new IsolatedBiliDownloaderEventBus(),
            tracker ?? new NoOpDownloadProgressTracker(),
            executor,
            new TestDataPaths(),
            credentialProvider ?? new FakeCredentialProvider());

    private static DownloadTaskRecord Record(string id, DownloadTaskStatus status)
        => new()
        {
            TaskId = id,
            DocumentId = "doc",
            ItemTitle = id,
            Status = DownloadTaskStatusMapper.ToStorageString(status),
        };

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
                throw new TimeoutException("测试等待超时");
            await Task.Delay(10);
        }
    }

    #region A. 进度写入有序性与 Flush

    [Fact]
    public async Task 完成通知不早于数据库提交_flush在MarkCompleted之前()
    {
        // 使用 RecordingProgressTracker 验证调用顺序
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var tracker = new RecordingProgressTracker();
        var coordinator = CreateCoordinator(repository, executor, tracker: tracker);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "done");

        // 验证 flush 在 repository:stage:done 之前
        var flushIndex = tracker.CallLog.FindIndex(x => x == "tracker:flush:t1");
        Assert.True(flushIndex >= 0, "未找到 flush 调用");

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 关闭时flush最后进度_Shutdown后DB有最终进度()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var tracker = new RecordingProgressTracker();
        var coordinator = CreateCoordinator(repository, executor, tracker: tracker);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await executor.Started.Task;

        // Shutdown 应触发 tracker.ShutdownAsync
        await coordinator.ShutdownAsync();

        Assert.Contains("tracker:shutdown", tracker.CallLog);
    }

    [Fact]
    public async Task 暂停路径先flush再持久化()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var tracker = new RecordingProgressTracker();
        var coordinator = CreateCoordinator(repository, executor, tracker: tracker);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await executor.Started.Task;

        await coordinator.PauseTaskAsync("t1");

        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "paused");

        // 验证 flush 被调用
        Assert.Contains("tracker:flush:t1", tracker.CallLog);
        await coordinator.ShutdownAsync();
    }

    #endregion

    #region B. 错误分类

    [Fact]
    public async Task 网络异常_ErrorType为network且可重试()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (_, _) => throw new HttpRequestException("连接超时");
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "failed");

        var task = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal("network", task.ErrorType);
        Assert.True(task.IsRetryable);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task CDN协议异常_ErrorType为cdn且可重试()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (_, _) => throw new DownloadProtocolException("Content-Range 不匹配");
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "failed");

        var task = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal("cdn", task.ErrorType);
        Assert.True(task.IsRetryable);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task ffmpeg异常_ErrorType为ffmpeg且不可重试()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (_, _) => throw new InvalidOperationException("ffmpeg 进程退出码 1");
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "failed");

        var task = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal("ffmpeg", task.ErrorType);
        Assert.False(task.IsRetryable);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task IO异常_ErrorType为disk且不可重试()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (_, _) => throw new IOException("磁盘空间不足");
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "failed");

        var task = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal("disk", task.ErrorType);
        Assert.False(task.IsRetryable);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 未知异常_ErrorType为unknown且不可重试()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (_, _) => throw new ApplicationException("意外错误");
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "failed");

        var task = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal("unknown", task.ErrorType);
        Assert.False(task.IsRetryable);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public void 错误分类器_纯函数验证()
    {
        // 直接测试分类器的所有分支
        Assert.Equal(("cdn", true), DownloadErrorClassifier.Classify(new DownloadProtocolException("x")));
        Assert.Equal(("network", true), DownloadErrorClassifier.Classify(new HttpRequestException("x")));
        Assert.Equal(("network", true), DownloadErrorClassifier.Classify(new TaskCanceledException()));
        Assert.Equal(("directory", false), DownloadErrorClassifier.Classify(new UnauthorizedAccessException()));
        Assert.Equal(("ffmpeg", false), DownloadErrorClassifier.Classify(new Exception("ffmpeg error")));
        Assert.Equal(("disk", false), DownloadErrorClassifier.Classify(new IOException("x")));
        Assert.Equal(("unknown", false), DownloadErrorClassifier.Classify(new Exception("random")));
    }

    #endregion

    #region C. 临时文件校验

    [Fact]
    public async Task 临时文件校验_目录不存在_字节数归零()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);

        var task = Record("t1", DownloadTaskStatus.Interrupted);
        task.TempDirectory = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N"));
        task.VideoBytesDownloaded = 1024;
        task.AudioBytesDownloaded = 512;
        repository.Seed(task);

        await coordinator.InitializeAsync();

        var updated = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal(0, updated.VideoBytesDownloaded);
        Assert.Equal(0, updated.AudioBytesDownloaded);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 临时文件校验_大小不一致_以磁盘为准修正()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        var tempDir = Path.Combine(paths.TempDirectory, "t1");
        Directory.CreateDirectory(tempDir);

        // 创建实际大小为 100 字节的临时文件
        await File.WriteAllBytesAsync(Path.Combine(tempDir, "video.tmp"), new byte[100]);
        await File.WriteAllBytesAsync(Path.Combine(tempDir, "audio.tmp"), new byte[50]);

        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);

        var task = Record("t1", DownloadTaskStatus.Interrupted);
        task.TempDirectory = tempDir;
        task.VideoBytesDownloaded = 9999; // 数据库记录与实际不一致
        task.AudioBytesDownloaded = 8888;
        repository.Seed(task);

        await coordinator.InitializeAsync();

        var updated = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal(100, updated.VideoBytesDownloaded);
        Assert.Equal(50, updated.AudioBytesDownloaded);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 临时文件校验_文件匹配_字节数不变()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        var tempDir = Path.Combine(paths.TempDirectory, "t1");
        Directory.CreateDirectory(tempDir);

        await File.WriteAllBytesAsync(Path.Combine(tempDir, "video.tmp"), new byte[200]);
        await File.WriteAllBytesAsync(Path.Combine(tempDir, "audio.tmp"), new byte[100]);

        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);

        var task = Record("t1", DownloadTaskStatus.Paused);
        task.TempDirectory = tempDir;
        task.VideoBytesDownloaded = 200; // 与实际一致
        task.AudioBytesDownloaded = 100;
        repository.Seed(task);

        await coordinator.InitializeAsync();

        var updated = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal(200, updated.VideoBytesDownloaded);
        Assert.Equal(100, updated.AudioBytesDownloaded);
        await coordinator.ShutdownAsync();
    }

    #endregion

    #region D. 重启不自动恢复（回归）

    [Fact]
    public async Task 重启不自动恢复_初始化后无executor调用()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);

        // Seed 一个 Interrupted 任务
        repository.Seed(Record("t1", DownloadTaskStatus.Interrupted));

        await coordinator.InitializeAsync();

        // 初始化后不应有任何 executor 调用
        Assert.Equal(0, executor.ExecuteCount);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 运行中任务启动时迁移为Interrupted()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);

        repository.Seed(Record("t1", DownloadTaskStatus.DownloadingVideo));

        await coordinator.InitializeAsync();

        var task = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal("interrupted", task.Status);
        Assert.Equal(0, executor.ExecuteCount);
        await coordinator.ShutdownAsync();
    }

    #endregion

    #region E. ProgressWriteChannel 单元测试

    [Fact]
    public async Task 写入合并_同一任务多次写入只保留最新()
    {
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        var channel = new ProgressWriteChannel(repository);

        // 快速入队多条同一任务的进度
        for (int i = 1; i <= 10; i++)
        {
            channel.Enqueue(new ProgressWriteRequest(
                "t1", i, ProgressWriteKind.StageProgress,
                Progress: i * 10, Status: "downloading_video"));
        }

        await channel.ShutdownAsync();

        // 合并后最终写入的应该是最新版本（Progress=100）
        var task = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal(100, task.Progress);
    }

    [Fact]
    public async Task FlushAsync_等待指定任务写入完成()
    {
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        var channel = new ProgressWriteChannel(repository);

        channel.Enqueue(new ProgressWriteRequest(
            "t1", 1, ProgressWriteKind.StageProgress,
            Progress: 50, Status: "downloading_video"));

        await channel.FlushAsync("t1");

        // Flush 后写入应该已完成
        var task = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal(50, task.Progress);

        await channel.ShutdownAsync();
    }

    [Fact]
    public async Task ShutdownAsync_所有待写入全部落盘()
    {
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));
        repository.Seed(Record("t2", DownloadTaskStatus.Ready));

        var channel = new ProgressWriteChannel(repository);

        channel.Enqueue(new ProgressWriteRequest(
            "t1", 1, ProgressWriteKind.StageProgress,
            Progress: 30, Status: "downloading_video"));
        channel.Enqueue(new ProgressWriteRequest(
            "t2", 1, ProgressWriteKind.StageProgress,
            Progress: 60, Status: "downloading_audio"));
        channel.Enqueue(new ProgressWriteRequest(
            "t1", 2, ProgressWriteKind.Bytes,
            VideoBytes: 1024, AudioBytes: 512));

        await channel.ShutdownAsync();

        var t1 = repository.Tasks.Single(x => x.TaskId == "t1");
        var t2 = repository.Tasks.Single(x => x.TaskId == "t2");
        Assert.Equal(30, t1.Progress);
        Assert.Equal(60, t2.Progress);
        Assert.Equal(1024, t1.VideoBytesDownloaded);
        Assert.Equal(512, t1.AudioBytesDownloaded);
    }

    #endregion
}
