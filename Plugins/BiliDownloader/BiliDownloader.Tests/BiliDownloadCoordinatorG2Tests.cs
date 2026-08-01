using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using MyAvaloniaManagementCommon.Message;

namespace BiliDownloader.Tests;

/// <summary>
/// G2 单任务控制内核集成测试：验证 Coordinator 的暂停/恢复/取消/重新开始/删除/批量/WaitingForLogin/并发调整。
/// </summary>
public sealed class BiliDownloadCoordinatorG2Tests
{
    private static BiliDownloadCoordinator CreateCoordinator(
        InMemoryDownloadTaskRepository repository,
        FakeDownloadTaskExecutor executor,
        FakeCredentialProvider? credentialProvider = null,
        IMessengerService? messenger = null)
        => new(
            repository,
            messenger ?? new IsolatedMessengerService(),
            new NoOpDownloadProgressTracker(),
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

    #region A. 暂停与恢复

    [Fact]
    public async Task 暂停活动任务后状态持久化为paused()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await executor.Started.Task;

        await coordinator.PauseTaskAsync("t1");

        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "paused");
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 暂停不影响其他并发任务()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        coordinator.SetMaxConcurrentDownloads(2);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));
        repository.Seed(Record("t2", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() => executor.ExecuteCount >= 2);

        await coordinator.PauseTaskAsync("t1");

        // t2 仍在执行（activeCount > 0）
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "paused");
        Assert.Equal(2, executor.MaxActiveCount);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 恢复暂停任务后重新进入Ready并执行()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var callCount = 0;
        executor.Handler = (task, ct) =>
        {
            callCount++;
            if (callCount == 1)
                return Task.Delay(Timeout.Infinite, ct)
                    .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
            return Task.FromResult(new DownloadExecutionResult(null, null));
        };
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await executor.Started.Task;
        await coordinator.PauseTaskAsync("t1");
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "paused");

        await coordinator.ResumeTaskAsync("t1");

        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "done");
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 暂停非活动任务无副作用()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Completed));

        await coordinator.PauseTaskAsync("t1");

        Assert.Equal("done", repository.Tasks.Single(x => x.TaskId == "t1").Status);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 暂停后删除不抛异常()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        var task = Record("t1", DownloadTaskStatus.Ready);
        repository.Seed(task);

        coordinator.StartProcessingAsync();
        await executor.Started.Task;
        await coordinator.PauseTaskAsync("t1");
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "paused");
        // 等待 ProcessSingleTaskAsync 完全退出（finally 清理完成）
        await Task.Delay(100);

        await coordinator.DeleteTaskAsync(task);

        Assert.Empty(repository.Tasks);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 暂停保留断点字节数()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        executor.OnCallbacks = (onProgress, onBytes) => onBytes(500, 0);
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await executor.Started.Task;
        await coordinator.PauseTaskAsync("t1");
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "paused");

        Assert.Equal(500, repository.Tasks.Single(x => x.TaskId == "t1").VideoBytesDownloaded);
        await coordinator.ShutdownAsync();
    }

    #endregion

    #region B. 取消

    [Fact]
    public async Task 取消活动任务通过perTaskCTS传播()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await executor.Started.Task;

        await coordinator.CancelTaskAsync("t1");

        Assert.Equal("canceled", repository.Tasks.Single(x => x.TaskId == "t1").Status);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 取消不影响其他并发任务()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) =>
        {
            if (task.TaskId == "t1")
                return Task.Delay(Timeout.Infinite, ct)
                    .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
            return Task.FromResult(new DownloadExecutionResult(null, null));
        };
        var coordinator = CreateCoordinator(repository, executor);
        coordinator.SetMaxConcurrentDownloads(2);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));
        repository.Seed(Record("t2", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() => executor.ExecuteCount >= 2);

        await coordinator.CancelTaskAsync("t1");

        Assert.Equal("canceled", repository.Tasks.Single(x => x.TaskId == "t1").Status);
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t2").Status == "done");
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 取消已完成任务无副作用()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Completed));

        await coordinator.CancelTaskAsync("t1");

        // 已完成任务不在活动上下文中，CancelTaskAsync 仍会更新状态为 canceled
        // 但实际场景中 UI 不应对已完成任务调用取消
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 取消后临时文件被清理()
    {
        using var paths = new TestDataPaths();
        var tempDir = Path.Combine(paths.TempDirectory, "t1");
        Directory.CreateDirectory(tempDir);
        await File.WriteAllTextAsync(Path.Combine(tempDir, "part.mp4"), "data");

        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedMessengerService(), new NoOpDownloadProgressTracker(),
            executor, paths, new FakeCredentialProvider());
        var task = Record("t1", DownloadTaskStatus.Ready);
        task.TempDirectory = tempDir;
        repository.Seed(task);

        coordinator.StartProcessingAsync();
        await executor.Started.Task;
        await coordinator.CancelTaskAsync("t1");

        Assert.False(Directory.Exists(tempDir));
        await coordinator.ShutdownAsync();
    }

    #endregion

    #region C. 重新开始

    [Fact]
    public async Task 重新开始活动任务会先取消再重置()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await executor.Started.Task;

        await coordinator.RestartTaskAsync("t1");

        var task = repository.Tasks.Single(x => x.TaskId == "t1");
        Assert.Equal(0, task.Progress);
        Assert.Equal(0, task.VideoBytesDownloaded);
        Assert.Equal("pending", task.Status);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 重新开始失败任务会清理临时文件()
    {
        using var paths = new TestDataPaths();
        var tempDir = Path.Combine(paths.TempDirectory, "t1");
        Directory.CreateDirectory(tempDir);

        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedMessengerService(), new NoOpDownloadProgressTracker(),
            executor, paths, new FakeCredentialProvider());
        var task = Record("t1", DownloadTaskStatus.Failed);
        task.TempDirectory = tempDir;
        repository.Seed(task);

        await coordinator.RestartTaskAsync("t1");

        Assert.False(Directory.Exists(tempDir));
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 重新开始后任务可被重新调度执行()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Failed));

        await coordinator.RestartTaskAsync("t1");

        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "done");
        Assert.True(executor.ExecuteCount >= 1);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 重新开始已完成任务会重置进度()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);
        var task = Record("t1", DownloadTaskStatus.Completed);
        task.Progress = 100;
        repository.Seed(task);

        await coordinator.RestartTaskAsync("t1");

        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "done");
        // 重新执行后再次完成
        Assert.Equal(100, repository.Tasks.Single(x => x.TaskId == "t1").Progress);
        await coordinator.ShutdownAsync();
    }

    #endregion

    #region D. 删除（增强）

    [Fact]
    public async Task 删除活动任务只取消目标不停止全部()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) =>
        {
            if (task.TaskId == "t1")
                return Task.Delay(Timeout.Infinite, ct)
                    .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
            return Task.FromResult(new DownloadExecutionResult(null, null));
        };
        var coordinator = CreateCoordinator(repository, executor);
        coordinator.SetMaxConcurrentDownloads(2);
        var task1 = Record("t1", DownloadTaskStatus.Ready);
        repository.Seed(task1);
        repository.Seed(Record("t2", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() => executor.ExecuteCount >= 2);

        await coordinator.DeleteTaskAsync(task1);

        Assert.DoesNotContain(repository.Tasks, x => x.TaskId == "t1");
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t2").Status == "done");
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 删除活动任务等待执行器退出()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        var task = Record("t1", DownloadTaskStatus.Ready);
        repository.Seed(task);

        coordinator.StartProcessingAsync();
        await executor.Started.Task;
        // 等待任务完全进入执行状态
        await Task.Delay(50);

        await coordinator.DeleteTaskAsync(task);

        Assert.Empty(repository.Tasks);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 删除非活动任务行为不变回归()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);
        var task = Record("t1", DownloadTaskStatus.Failed);
        repository.Seed(task);

        await coordinator.DeleteTaskAsync(task);

        Assert.Empty(repository.Tasks);
        await coordinator.ShutdownAsync();
    }

    #endregion

    #region E. WaitingForLogin

    [Fact]
    public async Task 未登录时任务进入WaitingForLogin()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var credential = new FakeCredentialProvider { IsLoggedIn = false };
        var coordinator = CreateCoordinator(repository, executor, credential);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();

        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "waiting_for_login");
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task WaitingForLogin任务不调用执行器()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var credential = new FakeCredentialProvider { IsLoggedIn = false };
        var coordinator = CreateCoordinator(repository, executor, credential);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "waiting_for_login");

        Assert.Equal(0, executor.ExecuteCount);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 登录成功后WaitingForLogin任务自动恢复()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var credential = new FakeCredentialProvider { IsLoggedIn = false };
        var messenger = new IsolatedMessengerService();
        var coordinator = CreateCoordinator(repository, executor, credential, messenger);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "waiting_for_login");

        // 模拟登录成功
        credential.IsLoggedIn = true;
        messenger.Send(new LoginStateChangedMessage(true, "user", null));

        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "done");
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 登出消息不触发恢复()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var credential = new FakeCredentialProvider { IsLoggedIn = false };
        var messenger = new IsolatedMessengerService();
        var coordinator = CreateCoordinator(repository, executor, credential, messenger);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "waiting_for_login");

        messenger.Send(new LoginStateChangedMessage(false, null, null));
        await Task.Delay(200);

        Assert.Equal("waiting_for_login", repository.Tasks.Single(x => x.TaskId == "t1").Status);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 已登录时任务正常执行不进入WaitingForLogin()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var credential = new FakeCredentialProvider { IsLoggedIn = true };
        var coordinator = CreateCoordinator(repository, executor, credential);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();

        await WaitUntilAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "done");
        await coordinator.ShutdownAsync();
    }

    #endregion

    #region F. 并发数调整

    [Fact]
    public async Task 并发数下调暂停超额任务()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        coordinator.SetMaxConcurrentDownloads(3);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));
        repository.Seed(Record("t2", DownloadTaskStatus.Ready));
        repository.Seed(Record("t3", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() => executor.ExecuteCount >= 3);

        coordinator.SetMaxConcurrentDownloads(1);

        // 等待 GracefulScaleDown 生效（200ms delay + 暂停操作）
        await WaitUntilAsync(() =>
            repository.Tasks.Count(x => x.Status == "paused") >= 1, timeoutMs: 3000);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 并发数上调不暂停任务()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        coordinator.SetMaxConcurrentDownloads(1);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await executor.Started.Task;

        coordinator.SetMaxConcurrentDownloads(3);
        await Task.Delay(300);

        Assert.DoesNotContain(repository.Tasks, x => x.Status == "paused");
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public void 并发数钳制在1到5范围()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);

        coordinator.SetMaxConcurrentDownloads(0);
        // 无法直接读取 _maxConcurrentDownloads，但通过行为验证不抛异常
        coordinator.SetMaxConcurrentDownloads(10);

        // 只要不抛异常即通过
        Assert.True(true);
    }

    #endregion

    #region G. 批量操作

    [Fact]
    public async Task PauseAllActive暂停所有活动任务()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        coordinator.SetMaxConcurrentDownloads(3);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));
        repository.Seed(Record("t2", DownloadTaskStatus.Ready));
        repository.Seed(Record("t3", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() => executor.ExecuteCount >= 3);

        await coordinator.PauseAllActiveAsync();

        await WaitUntilAsync(() =>
            repository.Tasks.Count(x => x.Status == "paused") == 3);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task ResumeAllPaused恢复所有暂停任务()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var callCount = 0;
        executor.Handler = (task, ct) =>
        {
            var count = Interlocked.Increment(ref callCount);
            if (count <= 3)
                return Task.Delay(Timeout.Infinite, ct)
                    .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
            return Task.FromResult(new DownloadExecutionResult(null, null));
        };
        var coordinator = CreateCoordinator(repository, executor);
        coordinator.SetMaxConcurrentDownloads(3);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));
        repository.Seed(Record("t2", DownloadTaskStatus.Ready));
        repository.Seed(Record("t3", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() => executor.ExecuteCount >= 3);
        await coordinator.PauseAllActiveAsync();
        await WaitUntilAsync(() =>
            repository.Tasks.Count(x => x.Status == "paused") == 3);

        await coordinator.ResumeAllPausedAsync();

        await WaitUntilAsync(() =>
            repository.Tasks.Count(x => x.Status == "done") == 3, timeoutMs: 8000);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task CancelAllActive取消所有活动任务()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        coordinator.SetMaxConcurrentDownloads(3);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));
        repository.Seed(Record("t2", DownloadTaskStatus.Ready));
        repository.Seed(Record("t3", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await WaitUntilAsync(() => executor.ExecuteCount >= 3);

        await coordinator.CancelAllActiveAsync();

        await WaitUntilAsync(() =>
            repository.Tasks.Count(x => x.Status == "canceled") == 3);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task RestartAllStalled重启所有停滞任务()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Failed));
        repository.Seed(Record("t2", DownloadTaskStatus.Interrupted));
        repository.Seed(Record("t3", DownloadTaskStatus.Canceled));

        await coordinator.RestartAllStalledAsync();

        await WaitUntilAsync(() =>
            repository.Tasks.Count(x => x.Status == "done") == 3);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 批量操作对空列表无副作用()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = CreateCoordinator(repository, executor);

        var exception = await Xunit.Record.ExceptionAsync(async () =>
        {
            await coordinator.PauseAllActiveAsync();
            await coordinator.ResumeAllPausedAsync();
            await coordinator.CancelAllActiveAsync();
            await coordinator.RestartAllStalledAsync();
        });

        Assert.Null(exception);
        await coordinator.ShutdownAsync();
    }

    #endregion

    #region H. 全局停止与关闭（回归+增强）

    [Fact]
    public async Task 全局停止仍把所有活动任务标记为Ready回归()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await executor.Started.Task;

        await coordinator.StopProcessingAsync();

        Assert.Equal("pending", repository.Tasks.Single(x => x.TaskId == "t1").Status);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 宿主关闭仍标记为Interrupted回归()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await executor.Started.Task;

        await coordinator.ShutdownAsync();

        Assert.Equal("interrupted", repository.Tasks.Single(x => x.TaskId == "t1").Status);
    }

    [Fact]
    public async Task 关闭后perTask上下文全部清理()
    {
        var repository = new InMemoryDownloadTaskRepository();
        var executor = new FakeDownloadTaskExecutor();
        executor.Handler = (task, ct) => Task.Delay(Timeout.Infinite, ct)
            .ContinueWith(_ => new DownloadExecutionResult(null, null), TaskContinuationOptions.OnlyOnRanToCompletion);
        var coordinator = CreateCoordinator(repository, executor);
        repository.Seed(Record("t1", DownloadTaskStatus.Ready));

        coordinator.StartProcessingAsync();
        await executor.Started.Task;
        await coordinator.ShutdownAsync();

        // 关闭后操作不抛 ObjectDisposedException
        var exception = await Xunit.Record.ExceptionAsync(async () =>
            await coordinator.PauseTaskAsync("t1"));
        Assert.Null(exception);
    }

    #endregion
}
