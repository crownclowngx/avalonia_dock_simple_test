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
