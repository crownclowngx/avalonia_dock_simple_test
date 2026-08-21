using BiliDownloader.Create;
using BiliDownloader.Constants;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.ViewModels;
using BiliDownloader.ViewModels.BiliScheduler;
using MyAvaloniaManagementCommon.ToolCreation;

namespace BiliDownloader.Tests;

public sealed class SchedulerViewModelTests
{
    [Fact]
    public async Task 任务列表加载计数启动重试删除与清理完成任务()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(
            Record("pending", DownloadTaskStatus.Ready),
            Record("done", DownloadTaskStatus.Completed),
            Record("failed", DownloadTaskStatus.Failed));
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = new BiliDownloadCoordinator(
            repository,
            new IsolatedHostEventBus(),
            new NoOpDownloadProgressTracker(),
            executor,
            paths);
        var messages = new List<string>();
        var vm = new SchedulerTaskListViewModel(
            coordinator, repository, messages.Add, new FakeConfirmationService { Result = true });

        await vm.ReloadTasksAsync();
        Assert.Equal(3, vm.Tasks.Count);
        Assert.Equal(1, vm.PendingCount);
        Assert.Equal(1, vm.CompletedCount);

        await vm.StartCommand.ExecuteAsync(null);
        await AsyncTest.EventuallyAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "pending").Status == "done"
            && vm.CompletedCount == 2);
        Assert.Equal(2, vm.CompletedCount);

        var failed = vm.Tasks.Single(x => x.TaskId == "failed");
        await vm.RetryTaskCommand.ExecuteAsync(failed);
        await AsyncTest.EventuallyAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "failed").Status == "done");

        vm.ClearDoneCommand.Execute(null);
        await AsyncTest.EventuallyAsync(() => repository.Tasks.Count == 0);
        await AsyncTest.EventuallyAsync(() => vm.Tasks.Count == 0);
        Assert.Equal(0, vm.PendingCount);
        Assert.Equal(0, vm.CompletedCount);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 删除命令委托协调器而Null命令无副作用()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var task = Record("delete", DownloadTaskStatus.Failed);
        repository.Seed(task);
        var coordinator = new BiliDownloadCoordinator(
            repository,
            new IsolatedHostEventBus(),
            new NoOpDownloadProgressTracker(),
            new FakeDownloadTaskExecutor(),
            paths);
        var vm = new SchedulerTaskListViewModel(
            coordinator, repository, _ => { }, new FakeConfirmationService { Result = true });
        await vm.ReloadTasksAsync();

        await vm.DeleteTaskCommand.ExecuteAsync(null);
        Assert.Single(vm.Tasks);
        await vm.DeleteTaskCommand.ExecuteAsync(task);

        Assert.Empty(repository.Tasks);
        Assert.Empty(vm.Tasks);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task Tool激活只初始化设置一次但每次刷新任务投影()
    {
        using var state = new StaticStateScope();
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var fakeFfmpeg = Path.Combine(paths.RootDirectory, "ffmpeg.exe");
        await File.WriteAllTextAsync(fakeFfmpeg, "marker");
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(Record("interrupted", DownloadTaskStatus.Interrupted));
        var settings = new InMemorySettingsRepository();
        settings.Seed("ffmpeg_custom_path", fakeFfmpeg);
        settings.Seed("max_concurrent_downloads", "2");
        var coordinator = new BiliDownloadCoordinator(
            repository,
            new IsolatedHostEventBus(),
            new NoOpDownloadProgressTracker(),
            new FakeDownloadTaskExecutor(),
            paths);
        var vm = new BiliSchedulerToolViewModel(
            coordinator,
            repository,
            settings,
            new FakeFfmpegService());

        await vm.ActivateAsync();
        Assert.Single(vm.TaskList.Tasks);
        Assert.Contains("1 个已中断", vm.SchedulerStatus, StringComparison.Ordinal);
        Assert.True(vm.Settings.FfmpegReady);
        Assert.Equal(1, settings.InitializeCount);

        repository.Seed(Record("new", DownloadTaskStatus.Completed));
        await vm.ActivateAsync();
        Assert.Equal(2, vm.TaskList.Tasks.Count);
        Assert.Equal(1, settings.InitializeCount);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public void Tool创建策略复用实例并返回稳定元数据()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var coordinator = new BiliDownloadCoordinator(
            repository,
            new IsolatedHostEventBus(),
            new NoOpDownloadProgressTracker(),
            new FakeDownloadTaskExecutor(),
            paths);
        var vm = new BiliSchedulerToolViewModel(
            coordinator,
            repository,
            new InMemorySettingsRepository(),
            new FakeFfmpegService());
        var strategy = new BiliSchedulerToolStrategy(() => vm);

        Assert.Same(vm, strategy.CreateTool());
        // 策略只负责创建实例；Dock 的字符串 ID 由宿主 Factory 根据元数据统一写入。
        Assert.Equal(string.Empty, vm.Id);
        Assert.Equal("Bilibili调度工具", vm.Title);
        Assert.True(vm.CanClose);
        var metadata = strategy.GetMetadata();
        Assert.Equal(SaveDocumentTypeIdConstant.SchedulerToolId, metadata.ToolTypeId);
        Assert.Equal(ToolDockSide.Right, metadata.DockSide);
    }

    private static DownloadTaskRecord Record(string id, DownloadTaskStatus status)
        => new()
        {
            TaskId = id,
            DocumentId = "doc",
            ItemTitle = id,
            Status = DownloadTaskStatusMapper.ToStorageString(status),
        };

}
