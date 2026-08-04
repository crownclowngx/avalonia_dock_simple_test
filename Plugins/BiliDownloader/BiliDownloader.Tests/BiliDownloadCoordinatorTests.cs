using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;

namespace BiliDownloader.Tests;

public sealed class BiliDownloadCoordinatorTests
{
    [Fact]
    public async Task 并发初始化只执行一次_运行中任务迁移为已中断且不自动下载()
    {
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(
            CreateRecord("ready", DownloadTaskStatus.Ready),
            CreateRecord("interrupted", DownloadTaskStatus.Interrupted),
            CreateRecord("failed", DownloadTaskStatus.Failed),
            CreateRecord("video", DownloadTaskStatus.DownloadingVideo),
            CreateRecord("merging", DownloadTaskStatus.Merging),
            CreateRecord("done", DownloadTaskStatus.Completed));
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);

        await Task.WhenAll(
            coordinator.InitializeAsync(),
            coordinator.InitializeAsync(),
            coordinator.InitializeAsync());

        Assert.Equal(1, repository.InitializeCount);
        Assert.Equal(0, executor.ExecuteCount);
        Assert.Equal("pending", Find(repository, "ready").Status);
        Assert.Equal("interrupted", Find(repository, "interrupted").Status);
        Assert.Equal("failed", Find(repository, "failed").Status);
        Assert.Equal("interrupted", Find(repository, "video").Status);
        Assert.Equal("interrupted", Find(repository, "merging").Status);
        Assert.Equal("done", Find(repository, "done").Status);

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 加载历史任务只读取投影_不会调用下载执行器()
    {
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(CreateRecord("ready", DownloadTaskStatus.Ready));
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);

        var tasks = await coordinator.LoadAllTasksAsync();

        Assert.Single(tasks);
        Assert.Equal(0, executor.ExecuteCount);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 明确提交时先持久化_再执行并最终完成()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor
        {
            OnExecute = () => repository.CallLog.Add("executor:execute"),
        };
        var coordinator = CreateCoordinator(repository, executor);

        await coordinator.InitializeAsync();
        await coordinator.SubmitTasksAsync(CreateSubmitMessage("task-1"), "unused");
        await WaitUntilAsync(() => Find(repository, "task-1").Status == "done");

        var insertIndex = repository.CallLog.IndexOf("repository:insert");
        var executeIndex = repository.CallLog.IndexOf("executor:execute");
        Assert.True(insertIndex >= 0);
        Assert.True(executeIndex > insertIndex);
        Assert.Equal(1, executor.ExecuteCount);

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 执行器异常会持久化为失败状态()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor
        {
            Handler = (_, _) => Task.FromException<DownloadExecutionResult>(
                new InvalidOperationException("模拟下载失败")),
        };
        var coordinator = CreateCoordinator(repository, executor);

        await coordinator.SubmitTasksAsync(CreateSubmitMessage("failed-task"), "unused");
        await WaitUntilAsync(() => Find(repository, "failed-task").Status == "failed");

        Assert.Equal("模拟下载失败", Find(repository, "failed-task").ErrorMessage);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 宿主关闭会取消活动执行器_等待退出并持久化为已中断()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor
        {
            Handler = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new DownloadExecutionResult(null, null);
            },
        };
        var coordinator = CreateCoordinator(repository, executor);

        await coordinator.SubmitTasksAsync(CreateSubmitMessage("active-task"), "unused");
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await coordinator.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.ShutdownAsync();

        Assert.Equal("interrupted", Find(repository, "active-task").Status);
        Assert.Equal(1, executor.ExecuteCount);
    }

    [Fact]
    public async Task 批量提交会完整映射字段并清洗分组目录()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);
        var message = new SubmitDownloadTaskMessage(
            sourceDocumentId: "doc-map",
            seriesTitle: "系列:标题",
            items:
            [
                new DownloadItemInfo
                {
                    ItemId = "mapped",
                    Title = "第一集",
                    Aid = 11,
                    Bvid = "BV1MAPPED00",
                    Cid = 22,
                    Duration = 33,
                    MediaType = BiliMediaType.Bangumi,
                    EpId = 44,
                    SeasonId = 55,
                    CoverUrl = "https://example.invalid/cover.jpg",
                },
            ],
            qualityId: 120,
            audioQualityId: 30280,
            outputDirectory: "output",
            useGroupFolder: true,
            extrasConfig: Services.Download.Extras.ExtrasType.Subtitle
                | Services.Download.Extras.ExtrasType.Cover);

        await coordinator.SubmitTasksAsync(message, "ignored");
        await AsyncTest.EventuallyAsync(() => executor.ExecuteCount == 1);

        var task = Assert.Single(repository.Tasks);
        Assert.Equal("doc-map", task.DocumentId);
        Assert.Equal("系列:标题", task.SeriesTitle);
        Assert.Equal("第一集", task.ItemTitle);
        Assert.Equal(11, task.Aid);
        Assert.Equal("BV1MAPPED00", task.Bvid);
        Assert.Equal(22, task.Cid);
        Assert.Equal(120, task.QualityId);
        Assert.Equal(30280, task.AudioQualityId);
        Assert.Equal("output", task.OutputDirectory);
        Assert.Equal(Services.Naming.FileNameSanitizer.Sanitize("系列:标题"), task.SubFolder);
        Assert.Equal("bangumi", task.MediaType);
        Assert.Equal(44, task.EpId);
        Assert.Equal(55, task.SeasonId);
        Assert.Equal(6, task.ExtrasConfig);
        Assert.Equal("https://example.invalid/cover.jpg", task.CoverUrl);

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 并发限制会钳制并确保队列完整排空()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new FakeDownloadTaskExecutor
        {
            Handler = async (_, ct) =>
            {
                await release.Task.WaitAsync(ct);
                return new DownloadExecutionResult(null, null);
            },
        };
        var coordinator = CreateCoordinator(repository, executor);
        coordinator.SetMaxConcurrentDownloads(2);
        var message = CreateSubmitMessage("one");
        message.Items.AddRange(
        [
            new DownloadItemInfo { ItemId = "two", Title = "two" },
            new DownloadItemInfo { ItemId = "three", Title = "three" },
            new DownloadItemInfo { ItemId = "four", Title = "four" },
        ]);

        await coordinator.SubmitTasksAsync(message, "unused");
        await AsyncTest.EventuallyAsync(() => executor.ExecuteCount == 2);
        Assert.Equal(2, executor.MaxActiveCount);

        release.TrySetResult();
        await AsyncTest.EventuallyAsync(() =>
            repository.Tasks.Count == 4
            && repository.Tasks.All(x => x.Status == "done"));
        Assert.Equal(4, executor.ExecuteCount);
        Assert.Equal(2, executor.MaxActiveCount);

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 活动队列被新Ready任务唤醒并立即占用空闲槽()
    {
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(
            CreateRecord("blocked", DownloadTaskStatus.Ready),
            CreateRecord("restored", DownloadTaskStatus.Interrupted));
        var blockedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new FakeDownloadTaskExecutor
        {
            Handler = async (task, ct) =>
            {
                if (task.TaskId == "blocked")
                {
                    blockedStarted.TrySetResult();
                    await releaseBlocked.Task.WaitAsync(ct);
                }
                return new DownloadExecutionResult($"output-{task.TaskId}", null);
            },
        };
        var coordinator = CreateCoordinator(repository, executor);
        coordinator.SetMaxConcurrentDownloads(2);

        coordinator.StartProcessingAsync();
        await blockedStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.RetryTaskAsync(Find(repository, "restored"));

        await AsyncTest.EventuallyAsync(() =>
            executor.ExecutedTasks.Any(task => task.TaskId == "restored")
            && Find(repository, "restored").Status == "done");
        Assert.Equal("pending", Find(repository, "blocked").Status);
        Assert.Equal(2, executor.MaxActiveCount);

        releaseBlocked.TrySetResult();
        await AsyncTest.EventuallyAsync(() => Find(repository, "blocked").Status == "done");
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 普通停止会把活动任务放回队列且之后可重新启动()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor
        {
            Handler = async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new DownloadExecutionResult(null, null);
            },
        };
        var coordinator = CreateCoordinator(repository, executor);

        await coordinator.SubmitTasksAsync(CreateSubmitMessage("resume"), "unused");
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.StopProcessingAsync();

        Assert.Equal("pending", Find(repository, "resume").Status);
        Assert.False(coordinator.IsProcessing);

        executor.Handler = (_, _) => Task.FromResult(new DownloadExecutionResult(null, null));
        coordinator.StartProcessingAsync();
        await AsyncTest.EventuallyAsync(() => Find(repository, "resume").Status == "done");
        Assert.Equal(2, executor.ExecuteCount);

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 失败与中断重试都保留可信断点_只有重新开始才清零()
    {
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(
            CreateRecord("failed-reset", DownloadTaskStatus.Failed),
            CreateRecord("interrupted-resume", DownloadTaskStatus.Interrupted));
        Find(repository, "failed-reset").Progress = 42;
        Find(repository, "failed-reset").ErrorMessage = "old";
        Find(repository, "failed-reset").VideoBytesDownloaded = 100;
        Find(repository, "failed-reset").AudioBytesDownloaded = 200;
        Find(repository, "interrupted-resume").Progress = 55;
        Find(repository, "interrupted-resume").VideoBytesDownloaded = 300;
        var snapshots = new Dictionary<string, (double Progress, string? Error, long Video, long Audio)>();
        var executor = new FakeDownloadTaskExecutor
        {
            OnExecute = () =>
            {
                var task = repository.Tasks.Single(x =>
                    x.Status == "pending"
                    && !snapshots.ContainsKey(x.TaskId));
                snapshots[task.TaskId] = (
                    task.Progress,
                    task.ErrorMessage,
                    task.VideoBytesDownloaded,
                    task.AudioBytesDownloaded);
            },
        };
        var coordinator = CreateCoordinator(repository, executor);

        await coordinator.RetryTaskAsync(Find(repository, "failed-reset"));
        await AsyncTest.EventuallyAsync(() => snapshots.ContainsKey("failed-reset"));
        await coordinator.RetryTaskAsync(Find(repository, "interrupted-resume"));
        await AsyncTest.EventuallyAsync(() => snapshots.Count == 2);

        Assert.Equal((42d, null, 100L, 200L), snapshots["failed-reset"]);
        Assert.Equal((55d, null, 300L, 0L), snapshots["interrupted-resume"]);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 删除任务会清理临时目录并发送定向通知()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var messenger = new RecordingMessengerService();
        var task = CreateRecord("delete-me", DownloadTaskStatus.Completed);
        task.TempDirectory = Path.Combine(paths.TempDirectory, task.TaskId);
        Directory.CreateDirectory(task.TempDirectory);
        await File.WriteAllTextAsync(Path.Combine(task.TempDirectory, "part.tmp"), "data");
        repository.Seed(task);
        var coordinator = new BiliDownloadCoordinator(
            repository,
            messenger,
            new NoOpDownloadProgressTracker(),
            new FakeDownloadTaskExecutor(),
            paths);

        await coordinator.DeleteTaskAsync(
            task,
            new DeleteTaskOptions(DeleteTemporaryFiles: true, DeleteOutputFile: false));

        Assert.Empty(repository.Tasks);
        Assert.False(Directory.Exists(task.TempDirectory));
        var deleted = Assert.IsType<DownloadTaskDeletedMessage>(
            Assert.Single(messenger.SentMessages));
        Assert.Equal("document-1", deleted.TargetDocumentId);
        Assert.Equal("delete-me", deleted.TaskId);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 进度回调和附加资源摘要会投影到事件及仓储()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor
        {
            OnCallbacks = (progress, bytes) =>
            {
                progress(new DownloadProgressInfo
                {
                    Stage = "video",
                    OverallProgress = 25,
                    VideoProgress = 50,
                    SpeedText = "1 MB/s",
                });
                bytes(123, 45);
            },
            Handler = (_, _) => Task.FromResult(
                new DownloadExecutionResult("final.mp4", "subtitle: OK")),
        };
        var coordinator = CreateCoordinator(repository, executor);
        var progressEvents = 0;
        var statusEvents = 0;
        coordinator.TaskProgressChanged += _ => progressEvents++;
        coordinator.TaskStatusChanged += _ => statusEvents++;

        await coordinator.SubmitTasksAsync(CreateSubmitMessage("events"), "unused");
        await AsyncTest.EventuallyAsync(() => Find(repository, "events").Status == "done");

        Assert.Equal(1, progressEvents);
        Assert.Equal(1, statusEvents);
        Assert.Equal("subtitle: OK", Find(repository, "events").ExtrasResultSummary);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 下载异常中的敏感信息会在持久化前脱敏()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor
        {
            Handler = (_, _) => Task.FromException<DownloadExecutionResult>(
                new InvalidOperationException(
                    "Cookie: SESSDATA=secret; https://api.test/play?w_rid=signed")),
        };
        var coordinator = CreateCoordinator(repository, executor);

        await coordinator.SubmitTasksAsync(CreateSubmitMessage("safe-error"), "unused");
        await AsyncTest.EventuallyAsync(() => Find(repository, "safe-error").Status == "failed");

        var error = Find(repository, "safe-error").ErrorMessage;
        Assert.DoesNotContain("secret", error, StringComparison.Ordinal);
        Assert.DoesNotContain("signed", error, StringComparison.Ordinal);
        Assert.Contains("<redacted>", error, StringComparison.Ordinal);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 关闭幂等且关闭后拒绝新的执行命令()
    {
        var coordinator = CreateCoordinator(
            new InMemoryDownloadTaskRepository(),
            new FakeDownloadTaskExecutor());

        await Task.WhenAll(coordinator.ShutdownAsync(), coordinator.ShutdownAsync());

        Assert.Throws<InvalidOperationException>(() => coordinator.StartProcessingAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SubmitTasksAsync(CreateSubmitMessage("late"), "unused"));
    }

    private static BiliDownloadCoordinator CreateCoordinator(
        InMemoryDownloadTaskRepository repository,
        FakeDownloadTaskExecutor executor)
        => new(
            repository,
            new IsolatedMessengerService(),
            new NoOpDownloadProgressTracker(),
            executor,
            new TestDataPaths());

    private static SubmitDownloadTaskMessage CreateSubmitMessage(string taskId)
        => new(
            sourceDocumentId: "document-1",
            seriesTitle: "测试系列",
            items:
            [
                new DownloadItemInfo
                {
                    ItemId = taskId,
                    Title = "测试任务",
                    Aid = 1,
                    Bvid = "BV1TEST",
                    Cid = 2,
                },
            ],
            qualityId: 80,
            audioQualityId: 0,
            outputDirectory: "不应写入的测试目录");

    private static DownloadTaskRecord CreateRecord(string taskId, DownloadTaskStatus status)
        => new()
        {
            TaskId = taskId,
            DocumentId = "document-1",
            ItemTitle = taskId,
            Status = DownloadTaskStatusMapper.ToStorageString(status),
        };

    private static DownloadTaskRecord Find(InMemoryDownloadTaskRepository repository, string taskId)
        => repository.Tasks.Single(x => x.TaskId == taskId);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("等待测试条件超时。");
            }

            await Task.Delay(10);
        }
    }
}
