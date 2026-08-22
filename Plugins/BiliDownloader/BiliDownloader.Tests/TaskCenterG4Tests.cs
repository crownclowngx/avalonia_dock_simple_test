using System.Diagnostics;
using BiliDownloader.Converters;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.ViewModels.BiliScheduler;

namespace BiliDownloader.Tests;

/// <summary>
/// G4: 任务中心产品化测试。
/// 覆盖范围：筛选排序引擎（纯函数）、VM 集成（多选/批量/确认）、转换器、性能。
/// 设计思考：按代码抽象层级分组——纯单元测试（引擎/转换器）不需要 Coordinator，
/// VM 集成测试需要完整的 Coordinator + 仓储 + 确认服务替身。
/// </summary>
public sealed class TaskCenterG4Tests
{
    // ──────────────────────────────────────────────────────────────────────
    // 筛选排序引擎（纯函数单元测试，无外部依赖）
    // ──────────────────────────────────────────────────────────────────────

    #region 筛选引擎

    [Fact]
    public void 标题模糊筛选包含匹配且空搜索返回全部()
    {
        var tasks = new List<DownloadTaskRecord>
        {
            MakeTask("t1", "原神PV合集", DownloadTaskStatus.Ready),
            MakeTask("t2", "崩坏星穹铁道EP", DownloadTaskStatus.Ready),
            MakeTask("t3", "原神角色演示", DownloadTaskStatus.Ready),
        };

        // 搜索"原神"应返回 2 条
        var criteria = new TaskFilterCriteria("原神", "all", "all");
        var result = TaskFilterSortEngine.Apply(tasks, criteria, TaskSortField.CreatedAt, false);
        Assert.Equal(2, result.Count);
        Assert.All(result, t => Assert.Contains("原神", t.ItemTitle));

        // 空搜索返回全部
        var emptyCriteria = new TaskFilterCriteria("", "all", "all");
        var all = TaskFilterSortEngine.Apply(tasks, emptyCriteria, TaskSortField.CreatedAt, false);
        Assert.Equal(3, all.Count);

        // null 搜索返回全部
        var nullCriteria = new TaskFilterCriteria(null, "all", "all");
        var allNull = TaskFilterSortEngine.Apply(tasks, nullCriteria, TaskSortField.CreatedAt, false);
        Assert.Equal(3, allNull.Count);
    }

    [Fact]
    public void 标题筛选不区分大小写()
    {
        var tasks = new List<DownloadTaskRecord>
        {
            MakeTask("t1", "MyVideo Title", DownloadTaskStatus.Ready),
        };

        var criteria = new TaskFilterCriteria("myvideo", "all", "all");
        var result = TaskFilterSortEngine.Apply(tasks, criteria, TaskSortField.CreatedAt, false);
        Assert.Single(result);
    }

    [Fact]
    public void 状态分组筛选各状态组正确过滤()
    {
        var tasks = new List<DownloadTaskRecord>
        {
            MakeTask("t1", "运行中", DownloadTaskStatus.DownloadingVideo),
            MakeTask("t2", "失败", DownloadTaskStatus.Failed),
            MakeTask("t3", "中断", DownloadTaskStatus.Interrupted),
            MakeTask("t4", "等待登录", DownloadTaskStatus.WaitingForLogin),
            MakeTask("t5", "完成", DownloadTaskStatus.Completed),
            MakeTask("t6", "暂停", DownloadTaskStatus.Paused),
            MakeTask("t7", "取消", DownloadTaskStatus.Canceled),
            MakeTask("t8", "排队", DownloadTaskStatus.Ready),
            MakeTask("t9", "合并中", DownloadTaskStatus.Merging),
        };

        // "running" 包含 DownloadingVideo 和 Merging
        var running = TaskFilterSortEngine.Apply(tasks,
            new TaskFilterCriteria(null, "running", "all"), TaskSortField.CreatedAt, false);
        Assert.Equal(2, running.Count);

        // "failed" 只有 1 条
        var failed = TaskFilterSortEngine.Apply(tasks,
            new TaskFilterCriteria(null, "failed", "all"), TaskSortField.CreatedAt, false);
        Assert.Single(failed);
        Assert.Equal("t2", failed[0].TaskId);

        // "done" 只有 1 条
        var done = TaskFilterSortEngine.Apply(tasks,
            new TaskFilterCriteria(null, "done", "all"), TaskSortField.CreatedAt, false);
        Assert.Single(done);
        Assert.Equal("t5", done[0].TaskId);

        // "all" 返回全部
        var all = TaskFilterSortEngine.Apply(tasks,
            new TaskFilterCriteria(null, "all", "all"), TaskSortField.CreatedAt, false);
        Assert.Equal(9, all.Count);
    }

    [Fact]
    public void DocumentId筛选精确匹配()
    {
        var tasks = new List<DownloadTaskRecord>
        {
            MakeTask("t1", "A", DownloadTaskStatus.Ready, documentId: "doc-1"),
            MakeTask("t2", "B", DownloadTaskStatus.Ready, documentId: "doc-2"),
            MakeTask("t3", "C", DownloadTaskStatus.Ready, documentId: "doc-1"),
        };

        var criteria = new TaskFilterCriteria(null, "all", "doc-1");
        var result = TaskFilterSortEngine.Apply(tasks, criteria, TaskSortField.CreatedAt, false);
        Assert.Equal(2, result.Count);
        Assert.All(result, t => Assert.Equal("doc-1", t.DocumentId));
    }

    [Fact]
    public void 组合筛选多条件AND语义()
    {
        var tasks = new List<DownloadTaskRecord>
        {
            MakeTask("t1", "原神PV", DownloadTaskStatus.Failed, documentId: "doc-1"),
            MakeTask("t2", "原神EP", DownloadTaskStatus.Completed, documentId: "doc-1"),
            MakeTask("t3", "原神PV", DownloadTaskStatus.Failed, documentId: "doc-2"),
        };

        // 标题"原神" + 状态"failed" + Document"doc-1" → 只有 t1
        var criteria = new TaskFilterCriteria("原神", "failed", "doc-1");
        var result = TaskFilterSortEngine.Apply(tasks, criteria, TaskSortField.CreatedAt, false);
        Assert.Single(result);
        Assert.Equal("t1", result[0].TaskId);
    }

    #endregion

    #region 排序引擎

    [Fact]
    public void 按创建时间升序降序排序()
    {
        var baseTime = new DateTime(2026, 1, 1);
        var tasks = new List<DownloadTaskRecord>
        {
            MakeTask("t1", "A", DownloadTaskStatus.Ready, createdAt: baseTime.AddMinutes(3)),
            MakeTask("t2", "B", DownloadTaskStatus.Ready, createdAt: baseTime.AddMinutes(1)),
            MakeTask("t3", "C", DownloadTaskStatus.Ready, createdAt: baseTime.AddMinutes(2)),
        };

        var criteria = new TaskFilterCriteria(null, "all", "all");

        // 升序：t2 → t3 → t1
        var asc = TaskFilterSortEngine.Apply(tasks, criteria, TaskSortField.CreatedAt, false);
        Assert.Equal(new[] { "t2", "t3", "t1" }, asc.Select(t => t.TaskId).ToArray());

        // 降序：t1 → t3 → t2
        var desc = TaskFilterSortEngine.Apply(tasks, criteria, TaskSortField.CreatedAt, true);
        Assert.Equal(new[] { "t1", "t3", "t2" }, desc.Select(t => t.TaskId).ToArray());
    }

    [Fact]
    public void 按标题排序()
    {
        var tasks = new List<DownloadTaskRecord>
        {
            MakeTask("t1", "Banana", DownloadTaskStatus.Ready),
            MakeTask("t2", "apple", DownloadTaskStatus.Ready),
            MakeTask("t3", "Cherry", DownloadTaskStatus.Ready),
        };

        var criteria = new TaskFilterCriteria(null, "all", "all");
        var asc = TaskFilterSortEngine.Apply(tasks, criteria, TaskSortField.Title, false);
        // 不区分大小写排序：apple → Banana → Cherry
        Assert.Equal(new[] { "t2", "t1", "t3" }, asc.Select(t => t.TaskId).ToArray());
    }

    [Fact]
    public void 空集合和单元素边界安全()
    {
        var criteria = new TaskFilterCriteria("test", "failed", "doc");

        // 空集合
        var empty = TaskFilterSortEngine.Apply(new List<DownloadTaskRecord>(), criteria, TaskSortField.CreatedAt, false);
        Assert.Empty(empty);

        // 单元素
        var single = new List<DownloadTaskRecord> { MakeTask("t1", "test", DownloadTaskStatus.Failed) };
        var result = TaskFilterSortEngine.Apply(single, criteria, TaskSortField.Title, true);
        Assert.Single(result);
    }

    [Fact]
    public void ParseSortBy解析排序键()
    {
        Assert.Equal((TaskSortField.CreatedAt, true), TaskFilterSortEngine.ParseSortBy("created_desc"));
        Assert.Equal((TaskSortField.CreatedAt, false), TaskFilterSortEngine.ParseSortBy("created_asc"));
        Assert.Equal((TaskSortField.Status, false), TaskFilterSortEngine.ParseSortBy("status"));
        Assert.Equal((TaskSortField.Title, false), TaskFilterSortEngine.ParseSortBy("title"));
        // 未知键默认为创建时间降序
        Assert.Equal((TaskSortField.CreatedAt, true), TaskFilterSortEngine.ParseSortBy("unknown"));
    }

    #endregion

    // ──────────────────────────────────────────────────────────────────────
    // VM 集成测试（需要 Coordinator + 仓储 + 确认服务）
    // ──────────────────────────────────────────────────────────────────────

    #region VM 集成

    [Fact]
    public async Task 一百个任务加载后筛选结果正确()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        // 生成 100 个任务：50 个 pending，30 个 done，20 个 failed
        for (int i = 0; i < 50; i++)
            repository.Seed(MakeTask($"pending-{i}", $"视频{i}", DownloadTaskStatus.Ready));
        for (int i = 0; i < 30; i++)
            repository.Seed(MakeTask($"done-{i}", $"完成{i}", DownloadTaskStatus.Completed));
        for (int i = 0; i < 20; i++)
            repository.Seed(MakeTask($"failed-{i}", $"失败{i}", DownloadTaskStatus.Failed));

        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedBiliDownloaderEventBus(),
            new NoOpDownloadProgressTracker(), new FakeDownloadTaskExecutor(), paths);
        var vm = new SchedulerTaskListViewModel(coordinator, repository, _ => { });

        await vm.ReloadTasksAsync();
        Assert.Equal(100, vm.Tasks.Count);
        Assert.Equal(100, vm.FilteredTasks.Count);
        Assert.Equal(100, vm.FilteredCount);

        // 筛选失败任务
        vm.StatusFilter = "failed";
        Assert.Equal(20, vm.FilteredTasks.Count);
        Assert.All(vm.FilteredTasks, t =>
            Assert.Equal(DownloadTaskStatus.Failed, DownloadTaskStatusMapper.FromStorageString(t.Status)));

        // 搜索"视频"
        vm.StatusFilter = "all";
        vm.SearchText = "视频";
        await AsyncTest.EventuallyAsync(() => vm.FilteredTasks.Count == 50);

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 全选筛选结果快照语义_全选后修改筛选已选不变()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(
            MakeTask("t1", "A", DownloadTaskStatus.Failed),
            MakeTask("t2", "B", DownloadTaskStatus.Failed),
            MakeTask("t3", "C", DownloadTaskStatus.Completed));
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedBiliDownloaderEventBus(),
            new NoOpDownloadProgressTracker(), new FakeDownloadTaskExecutor(), paths);
        var vm = new SchedulerTaskListViewModel(coordinator, repository, _ => { });
        await vm.ReloadTasksAsync();

        // 筛选失败任务 → 全选
        vm.StatusFilter = "failed";
        Assert.Equal(2, vm.FilteredTasks.Count);
        vm.SelectAllFilteredCommand.Execute(null);
        Assert.Equal(2, vm.SelectedCount);

        // 修改筛选条件 → 已选数量不变（IsSelected 保留在全量 Tasks 上）
        vm.StatusFilter = "all";
        Assert.Equal(3, vm.FilteredTasks.Count);
        Assert.Equal(2, vm.SelectedCount);

        // 取消选择
        vm.ClearSelectionCommand.Execute(null);
        Assert.Equal(0, vm.SelectedCount);

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 批量删除确认通过后删除选定任务()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(
            MakeTask("t1", "A", DownloadTaskStatus.Failed),
            MakeTask("t2", "B", DownloadTaskStatus.Failed),
            MakeTask("t3", "C", DownloadTaskStatus.Completed));
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedBiliDownloaderEventBus(),
            new NoOpDownloadProgressTracker(), new FakeDownloadTaskExecutor(), paths);
        var confirmService = new FakeConfirmationService { Result = true };
        var vm = new SchedulerTaskListViewModel(coordinator, repository, _ => { }, confirmService);
        await vm.ReloadTasksAsync();

        // 选中失败任务
        vm.StatusFilter = "failed";
        vm.SelectAllFilteredCommand.Execute(null);
        Assert.Equal(2, vm.SelectedCount);

        // 执行批量删除
        await vm.BatchDeleteCommand.ExecuteAsync(null);

        // 验证：确认服务被调用，消息包含数量
        Assert.Equal(1, confirmService.CallCount);
        Assert.Contains("2 个任务", confirmService.LastMessage);

        // 验证：任务被删除
        Assert.Single(vm.Tasks);
        Assert.Equal("t3", vm.Tasks[0].TaskId);

        // 重置筛选后验证 FilteredCount（当前 StatusFilter 仍为 "failed"，剩余任务为 done）
        vm.StatusFilter = "all";
        Assert.Equal(1, vm.FilteredCount);

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 批量删除确认拒绝时不执行操作()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(
            MakeTask("t1", "A", DownloadTaskStatus.Failed),
            MakeTask("t2", "B", DownloadTaskStatus.Completed));
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedBiliDownloaderEventBus(),
            new NoOpDownloadProgressTracker(), new FakeDownloadTaskExecutor(), paths);
        var confirmService = new FakeConfirmationService { Result = false };
        var vm = new SchedulerTaskListViewModel(coordinator, repository, _ => { }, confirmService);
        await vm.ReloadTasksAsync();

        // 选中并尝试删除
        vm.SelectAllFilteredCommand.Execute(null);
        await vm.BatchDeleteCommand.ExecuteAsync(null);

        // 验证：确认服务被调用但任务未被删除
        Assert.Equal(1, confirmService.CallCount);
        Assert.Equal(2, vm.Tasks.Count);

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 批量重试只处理失败和中断任务()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(
            MakeTask("t1", "A", DownloadTaskStatus.Failed),
            MakeTask("t2", "B", DownloadTaskStatus.Interrupted),
            MakeTask("t3", "C", DownloadTaskStatus.Completed));
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedBiliDownloaderEventBus(),
            new NoOpDownloadProgressTracker(), executor, paths);
        var vm = new SchedulerTaskListViewModel(coordinator, repository, _ => { });
        await vm.ReloadTasksAsync();

        // 全选（包含 done 任务）
        vm.SelectAllFilteredCommand.Execute(null);
        Assert.Equal(3, vm.SelectedCount);

        // 批量重试 → 只处理 failed + interrupted
        await vm.BatchRetryCommand.ExecuteAsync(null);

        // 验证：done 任务状态不变
        var doneTask = vm.Tasks.Single(t => t.TaskId == "t3");
        Assert.Equal("done", doneTask.Status);

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 进度更新不触发FilteredTasks集合变更()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        // 使用 Ready 状态，Coordinator 才会调度执行
        repository.Seed(MakeTask("t1", "A", DownloadTaskStatus.Ready));
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedBiliDownloaderEventBus(),
            new NoOpDownloadProgressTracker(), executor, paths);
        var vm = new SchedulerTaskListViewModel(coordinator, repository, _ => { });
        await vm.ReloadTasksAsync();

        // 记录集合变更次数
        int collectionChangedCount = 0;
        vm.FilteredTasks.CollectionChanged += (_, _) => collectionChangedCount++;

        // 启动调度器，让 FakeDownloadTaskExecutor 执行任务并触发进度事件
        coordinator.StartProcessingAsync();
        await AsyncTest.EventuallyAsync(() => executor.ExecuteCount > 0);

        // 等待任务完成（FakeDownloadTaskExecutor 会快速完成）
        await AsyncTest.EventuallyAsync(() =>
            repository.Tasks.Single(x => x.TaskId == "t1").Status == "done");

        // 设计契约验证：进度更新期间 FilteredTasks 不应被重建。
        // Coordinator 在任务生命周期中会触发多次状态变更（fetching_metadata → downloading → done），
        // 每次状态变更触发一次集合重建，但进度更新不会。
        // 单任务最多约 7 次状态变更，如果进度也触发重建则会达到数十次。
        // 阈值 10 足以区分“仅状态变更触发”和“进度也触发”两种情况。
        Assert.True(collectionChangedCount <= 10,
            $"集合重建次数过多: {collectionChangedCount}，进度更新可能触发了不必要的重建");

        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 状态变更触发FilteredTasks重建()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(
            MakeTask("t1", "A", DownloadTaskStatus.Ready),
            MakeTask("t2", "B", DownloadTaskStatus.Ready));
        var executor = new FakeDownloadTaskExecutor();
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedBiliDownloaderEventBus(),
            new NoOpDownloadProgressTracker(), executor, paths);
        var vm = new SchedulerTaskListViewModel(coordinator, repository, _ => { });
        await vm.ReloadTasksAsync();
    
        // 筛选“排队中”任务 → 应该看到 2 个
        vm.StatusFilter = "pending";
        Assert.Equal(2, vm.FilteredTasks.Count);
    
        // 启动调度器，让任务执行完成（状态从 pending → done）
        coordinator.StartProcessingAsync();
        await AsyncTest.EventuallyAsync(() =>
            repository.Tasks.All(x => x.Status == "done"));
    
        // 等待 VM 响应状态变更事件并重建 FilteredTasks
        await AsyncTest.EventuallyAsync(() => vm.FilteredTasks.Count == 0);
    
        // 验证：状态变更后筛选结果重建，"pending" 筛选不再包含已完成任务
        Assert.Equal(0, vm.FilteredCount);
    
        // 切换到 "done" 筛选 → 应该看到 2 个
        vm.StatusFilter = "done";
        Assert.Equal(2, vm.FilteredTasks.Count);
    
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task AvailableDocuments从任务中动态提取()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(
            MakeTask("t1", "A", DownloadTaskStatus.Ready, documentId: "doc-B"),
            MakeTask("t2", "B", DownloadTaskStatus.Ready, documentId: "doc-A"),
            MakeTask("t3", "C", DownloadTaskStatus.Ready, documentId: "doc-B"));
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedBiliDownloaderEventBus(),
            new NoOpDownloadProgressTracker(), new FakeDownloadTaskExecutor(), paths);
        var vm = new SchedulerTaskListViewModel(coordinator, repository, _ => { });
        await vm.ReloadTasksAsync();

        // 验证：包含 "all" + 去重排序后的 DocumentId
        Assert.Equal(3, vm.AvailableDocuments.Count);
        Assert.Equal("all", vm.AvailableDocuments[0]);
        Assert.Equal("doc-A", vm.AvailableDocuments[1]);
        Assert.Equal("doc-B", vm.AvailableDocuments[2]);

        await coordinator.ShutdownAsync();
    }

    #endregion

    // ──────────────────────────────────────────────────────────────────────
    // 转换器测试
    // ──────────────────────────────────────────────────────────────────────

    #region ByteSizeConverter

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-1, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(367001600, "350.0 MB")]
    [InlineData(1073741824, "1.00 GB")]
    [InlineData(2684354560, "2.50 GB")]
    public void ByteSizeConverter各量级格式正确(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeConverter.FormatBytes(bytes));
    }

    [Fact]
    public void ByteSizeConverter覆盖Xaml输入类型和反向转换拒绝()
    {
        var converter = new ByteSizeConverter();
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        Assert.Equal("1.0 KB", converter.Convert(1024L, typeof(string), null, culture));
        Assert.Equal("2.0 KB", converter.Convert(2048, typeof(string), null, culture));
        Assert.Equal("3.0 KB", converter.Convert(3072d, typeof(string), null, culture));
        Assert.Equal("0 B", converter.Convert("invalid", typeof(string), null, culture));
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack("1 KB", typeof(long), null, culture));
    }

    [Fact]
    public void 任务项投影完整映射展示字段并原子刷新运行态()
    {
        var record = new DownloadTaskRecord
        {
            TaskId = "task-1",
            DocumentId = "document-123456789",
            SourceDocumentTitle = "来源工作台",
            ItemTitle = "视频",
            Status = "failed",
            Progress = 10,
            VideoProgress = 20,
            AudioProgress = 30,
            MergeProgress = 40,
            SpeedText = "1 MB/s",
            BytesPerSecond = 1024,
            VideoBytesDownloaded = 100,
            AudioBytesDownloaded = 200,
            ExpectedVideoBytes = 1000,
            ExpectedAudioBytes = 2000,
            QualityId = 80,
            OutputDirectory = "root",
            SubFolder = "series",
            ErrorType = "network",
            ErrorMessage = "raw error",
            IsRetryable = true,
        };
        var item = new DownloadTaskItemViewModel(record);

        Assert.Equal("task-1", item.TaskId);
        Assert.Equal("视频", item.ItemTitle);
        Assert.Equal("failed", item.Status);
        Assert.NotEmpty(item.StatusDisplayText);
        Assert.Equal(10, item.Progress);
        Assert.Equal(20, item.VideoProgress);
        Assert.Equal(30, item.AudioProgress);
        Assert.Equal(40, item.MergeProgress);
        Assert.Equal("1 MB/s", item.SpeedText);
        Assert.NotNull(item.EstimatedRemainingText);
        Assert.Equal(300, item.TotalDownloadedBytes);
        Assert.Equal(3000, item.TotalExpectedBytes);
        Assert.Equal("1080P", item.QualityDisplayText);
        Assert.Equal(Path.Combine("root", "series"), item.FullOutputPath);
        Assert.Equal("来源工作台", item.SourceDocumentDisplay);
        Assert.NotEqual("raw error", item.ErrorMessage);
        Assert.True(item.HasFailureAction);
        Assert.NotNull(item.FailurePresentation);
        Assert.NotNull(item.PrimaryFailureAction);
        Assert.Equal(record, item.PrimaryFailureActionRequest.Task);
        _ = item.SecondaryFailureAction;
        _ = item.SecondaryFailureActionRequest;
        _ = item.HasSecondaryFailureAction;

        var source = new DownloadTaskRecord
        {
            Progress = 51,
            VideoProgress = 52,
            AudioProgress = 53,
            MergeProgress = 54,
            SpeedText = "2 MB/s",
            BytesPerSecond = 2048,
            VideoBytesDownloaded = 400,
            AudioBytesDownloaded = 500,
            Status = "downloading_video",
            ErrorMessage = null,
            ErrorType = null,
            IsRetryable = false,
            OutputFilePath = "final.mp4",
            ExpectedVideoBytes = 4000,
            ExpectedAudioBytes = 5000,
        };

        item.RefreshFrom(source);

        Assert.Equal(51, item.Progress);
        Assert.Equal(52, item.VideoProgress);
        Assert.Equal(53, item.AudioProgress);
        Assert.Equal(54, item.MergeProgress);
        Assert.Equal("2 MB/s", item.SpeedText);
        Assert.Equal("downloading_video", item.Status);
        Assert.Equal("final.mp4", item.FullOutputPath);
        Assert.False(item.HasFailureAction);
        _ = item.SecondaryFailureActionRequest;

        record.SourceDocumentTitle = "";
        Assert.StartsWith("工作台 document", item.SourceDocumentDisplay, StringComparison.Ordinal);
        record.DocumentId = "";
        Assert.Equal("未知工作台", item.SourceDocumentDisplay);
    }

    #endregion

    // ──────────────────────────────────────────────────────────────────────
    // 模型计算属性测试
    // ──────────────────────────────────────────────────────────────────────

    #region 模型计算属性

    [Fact]
    public void 模型计算属性正确计算()
    {
        var task = new DownloadTaskRecord
        {
            ExpectedVideoBytes = 1_000_000_000,
            ExpectedAudioBytes = 50_000_000,
            VideoBytesDownloaded = 500_000_000,
            AudioBytesDownloaded = 25_000_000,
            QualityId = 80,
            OutputDirectory = "/videos",
            SubFolder = "原神",
        };

        Assert.Equal(1_050_000_000, task.TotalExpectedBytes);
        Assert.Equal(525_000_000, task.TotalDownloadedBytes);
        Assert.Equal("1080P", task.QualityDisplayText);
        Assert.Equal(Path.Combine("/videos", "原神"), task.FullOutputPath);
    }

    [Fact]
    public void 质量显示文本映射正确()
    {
        Assert.Equal("8K", new DownloadTaskRecord { QualityId = 127 }.QualityDisplayText);
        Assert.Equal("4K", new DownloadTaskRecord { QualityId = 120 }.QualityDisplayText);
        Assert.Equal("1080P60", new DownloadTaskRecord { QualityId = 116 }.QualityDisplayText);
        Assert.Equal("720P", new DownloadTaskRecord { QualityId = 64 }.QualityDisplayText);
        Assert.Equal("Q999", new DownloadTaskRecord { QualityId = 999 }.QualityDisplayText);
    }

    [Fact]
    public void 无子文件夹时FullOutputPath为OutputDirectory()
    {
        var task = new DownloadTaskRecord
        {
            OutputDirectory = "/videos",
            SubFolder = "",
        };
        Assert.Equal("/videos", task.FullOutputPath);
    }

    #endregion

    // ──────────────────────────────────────────────────────────────────────
    // 性能测试
    // ──────────────────────────────────────────────────────────────────────

    #region 性能

    [Fact]
    public void 一百任务筛选性能在五毫秒内()
    {
        // 生成 100 个任务
        var tasks = new List<DownloadTaskRecord>();
        var baseTime = DateTime.Now;
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(MakeTask($"task-{i}", $"视频标题{i}号", DownloadTaskStatus.Ready,
                documentId: $"doc-{i % 5}",
                createdAt: baseTime.AddSeconds(i)));
        }

        var criteria = new TaskFilterCriteria("视频", "pending", "doc-2");
        var sw = Stopwatch.StartNew();

        // 执行 100 次筛选取平均
        for (int i = 0; i < 100; i++)
            TaskFilterSortEngine.Apply(tasks, criteria, TaskSortField.CreatedAt, true);

        sw.Stop();
        var avgMs = sw.Elapsed.TotalMilliseconds / 100;

        // 断言：平均每次筛选 < 5ms（实际应 < 0.1ms）
        Assert.True(avgMs < 5, $"筛选性能不达标: 平均 {avgMs:F3}ms");
    }

    [Fact]
    public async Task 一百任务批量操作在合理时间内完成()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        for (int i = 0; i < 100; i++)
            repository.Seed(MakeTask($"task-{i}", $"视频{i}", DownloadTaskStatus.Failed));

        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedBiliDownloaderEventBus(),
            new NoOpDownloadProgressTracker(), new FakeDownloadTaskExecutor(), paths);
        var confirmService = new FakeConfirmationService { Result = true };
        var vm = new SchedulerTaskListViewModel(coordinator, repository, _ => { }, confirmService);
        await vm.ReloadTasksAsync();

        // 全选 + 批量删除
        vm.SelectAllFilteredCommand.Execute(null);
        Assert.Equal(100, vm.SelectedCount);

        var sw = Stopwatch.StartNew();
        await vm.BatchDeleteCommand.ExecuteAsync(null);
        sw.Stop();

        // 断言：100 个任务批量删除 < 10 秒
        Assert.True(sw.Elapsed.TotalSeconds < 10, $"批量操作过慢: {sw.Elapsed.TotalSeconds:F2}s");
        Assert.Empty(vm.Tasks);

        await coordinator.ShutdownAsync();
    }

    #endregion

    // ──────────────────────────────────────────────────────────────────────
    // 辅助方法
    // ──────────────────────────────────────────────────────────────────────

    private static DownloadTaskRecord MakeTask(
        string id,
        string title,
        DownloadTaskStatus status,
        string documentId = "doc",
        DateTime? createdAt = null)
    {
        return new DownloadTaskRecord
        {
            TaskId = id,
            ItemTitle = title,
            DocumentId = documentId,
            Status = DownloadTaskStatusMapper.ToStorageString(status),
            CreatedAt = createdAt ?? DateTime.Now,
            OutputDirectory = "/test/output",
        };
    }
}
