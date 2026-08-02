using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.ViewModels.BiliScheduler;

/// <summary>
/// 任务列表子 ViewModel：负责任务展示、筛选排序、多选批量控制和任务 CRUD。
/// 设计思考（G4 重构）：
/// - 保留原有 Tasks 集合作为全量事实源（现有测试依赖此属性）；
/// - 新增 FilteredTasks 作为 UI 绑定目标，通过 TaskFilterSortEngine 纯函数计算；
/// - 进度更新只修改对象属性（INPC 自动通知 UI），不触发集合重建；
/// - 批量命令使用快照语义：执行时捕获当前选中列表，后续集合变更不影响已捕获的操作范围。
/// </summary>
public partial class SchedulerTaskListViewModel : ObservableObject
{
    private readonly BiliDownloadCoordinator _coordinator;
    private readonly IDownloadTaskRepository _taskStore;
    private readonly IConfirmationService _confirmationService;
    private readonly Action<string> _onStatusMessage;
    private readonly IFileRevealService _fileRevealService;
    private readonly IUiDispatcher _uiDispatcher;

    /// <summary>
    /// G4: O(1) 任务索引，替代原有的 FirstOrDefault 线性查找。
    /// 设计思考：5 并发下载时进度回调频率高（每 500ms × 5 = 10次/秒），
    /// 原 FirstOrDefault 在 100 条时虽仍快速，但 Dictionary 查找更确定且语义更清晰。
    /// </summary>
    private readonly Dictionary<string, DownloadTaskRecord> _taskIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DownloadTaskItemViewModel> _itemIndex = new(StringComparer.Ordinal);

    /// <summary>
    /// G4: 批量操作进行中标志。
    /// 设计思考：批量命令执行期间，Coordinator 事件可能触发 ApplyFilterAndSort，
    /// 导致集合重建与批量操作交叉，产生闪烁或不一致。
    /// 此标志在批量操作期间暂停事件驱动的刷新，完成后一次性刷新。
    /// </summary>
    private bool _isBatchOperating;
    private CancellationTokenSource? _searchDebounce;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private int _completedCount;

    // ── G4: 筛选/排序状态 ──

    /// <summary>标题模糊搜索关键词</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>状态分组筛选（"all"/"running"/"failed"/"interrupted"/"waiting_login"/"done"/"paused"/"canceled"/"pending"）</summary>
    [ObservableProperty]
    private string _statusFilter = "all";

    /// <summary>Document 筛选（"all" 或具体 DocumentId）</summary>
    [ObservableProperty]
    private string _documentFilter = "all";

    /// <summary>排序键（"created_desc"/"created_asc"/"status"/"title"）</summary>
    [ObservableProperty]
    private string _sortBy = "created_desc";

    public IReadOnlyList<TaskChoiceOption> StatusOptions { get; } =
    [
        new("all", "全部状态"), new("running", "进行中"), new("failed", "失败"),
        new("interrupted", "已中断"), new("waiting_login", "等待登录"),
        new("done", "已完成"), new("paused", "已暂停"), new("canceled", "已取消"),
        new("pending", "排队中"),
    ];
    public IReadOnlyList<TaskChoiceOption> SortOptions { get; } =
    [
        new("created_desc", "最新创建"), new("created_asc", "最早创建"),
        new("status", "按状态"), new("title", "按标题"),
    ];
    public IReadOnlyList<TaskChoiceOption> DateOptions { get; } =
    [
        new("all", "全部时间"), new("today", "今天"),
        new("7d", "最近 7 天"), new("30d", "最近 30 天"),
    ];

    [ObservableProperty] private TaskChoiceOption? _selectedStatusOption;
    [ObservableProperty] private TaskChoiceOption? _selectedSortOption;
    [ObservableProperty] private TaskChoiceOption? _selectedDateOption;

    [ObservableProperty]
    private TaskDateRange _dateRange = TaskDateRange.All;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasLoadError;

    [ObservableProperty]
    private string _loadErrorMessage = "";

    public bool HasTasks => Tasks.Count > 0;
    public bool HasFilteredTasks => FilteredTasks.Count > 0;
    public bool ShowEmptyState => !IsLoading && !HasLoadError && !HasTasks;
    public bool ShowNoResultsState => !IsLoading && !HasLoadError && HasTasks && !HasFilteredTasks;
    public bool HasSelection => SelectedCount > 0;

    // ── G4: 统计属性 ──

    /// <summary>当前筛选结果数量</summary>
    [ObservableProperty]
    private int _filteredCount;

    /// <summary>当前选中任务数量</summary>
    [ObservableProperty]
    private int _selectedCount;

    /// <summary>
    /// 所有任务记录（全量事实源，现有测试和 Tool VM 依赖此属性）
    /// </summary>
    public ObservableCollection<DownloadTaskRecord> Tasks { get; } = new();

    /// <summary>
    /// G4: 筛选排序后的任务列表（UI 绑定目标）。
    /// 设计思考：UI 的 ListBox 绑定此集合而非 Tasks，
    /// 实现"全量数据"与"当前视图"的分离。
    /// </summary>
    public ObservableCollection<DownloadTaskItemViewModel> FilteredTasks { get; } = new();

    public ObservableCollection<DownloadTaskItemViewModel> TaskItems { get; } = new();

    /// <summary>
    /// G4: 可用的 Document 列表（供筛选下拉框使用）。
    /// 设计思考：从全量任务中动态提取去重的 DocumentId，
    /// 用户可据此筛选特定 Document 提交的任务批次。
    /// </summary>
    public ObservableCollection<string> AvailableDocuments { get; } = new();
    public ObservableCollection<TaskDocumentFilterOption> AvailableDocumentOptions { get; } = new();

    // ── 原有命令 ──

    public IAsyncRelayCommand ClearDoneCommand { get; }
    public IAsyncRelayCommand<DownloadTaskRecord> DeleteTaskCommand { get; }
    public IAsyncRelayCommand<DownloadTaskRecord> RetryTaskCommand { get; }
    public IAsyncRelayCommand<DownloadTaskRecord> OpenFileLocationCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }

    // G2: 单任务控制命令
    public IAsyncRelayCommand<DownloadTaskRecord> PauseTaskCommand { get; }
    public IAsyncRelayCommand<DownloadTaskRecord> ResumeTaskCommand { get; }
    public IAsyncRelayCommand<DownloadTaskRecord> CancelTaskCommand { get; }
    public IAsyncRelayCommand<DownloadTaskRecord> RestartTaskCommand { get; }
    public IAsyncRelayCommand PauseAllCommand { get; }
    public IAsyncRelayCommand ResumeAllCommand { get; }

    // ── G4: 多选与批量命令 ──

    /// <summary>全选当前筛选结果</summary>
    public IRelayCommand SelectAllFilteredCommand { get; }

    /// <summary>取消所有选择</summary>
    public IRelayCommand ClearSelectionCommand { get; }

    /// <summary>批量删除选中任务（破坏性，需确认）</summary>
    public IAsyncRelayCommand BatchDeleteCommand { get; }

    /// <summary>批量重试选中任务中的失败/中断任务</summary>
    public IAsyncRelayCommand BatchRetryCommand { get; }

    /// <summary>批量重新开始选中任务（破坏性，需确认）</summary>
    public IAsyncRelayCommand BatchRestartCommand { get; }

    /// <summary>批量暂停选中任务中的运行中任务</summary>
    public IAsyncRelayCommand BatchPauseCommand { get; }

    /// <summary>批量恢复选中任务中的暂停/等待登录任务</summary>
    public IAsyncRelayCommand BatchResumeCommand { get; }

    public SchedulerTaskListViewModel(
        BiliDownloadCoordinator coordinator,
        IDownloadTaskRepository taskStore,
        Action<string> onStatusMessage,
        IConfirmationService? confirmationService = null,
        IFileRevealService? fileRevealService = null,
        IUiDispatcher? uiDispatcher = null)
    {
        _coordinator = coordinator;
        _taskStore = taskStore;
        _onStatusMessage = onStatusMessage;
        // G4: 确认服务可选注入，未注入时使用空实现（始终确认），保持向后兼容
        _confirmationService = confirmationService ?? new SafeCancellationConfirmationService();
        _fileRevealService = fileRevealService ?? new FileRevealService();
        _uiDispatcher = uiDispatcher ?? new InlineUiDispatcher();
        SelectedStatusOption = StatusOptions[0];
        SelectedSortOption = SortOptions[0];
        SelectedDateOption = DateOptions[0];

        ClearDoneCommand = new AsyncRelayCommand(ClearDoneTasksAsync);
        DeleteTaskCommand = new AsyncRelayCommand<DownloadTaskRecord>(DeleteTaskAsync);
        RetryTaskCommand = new AsyncRelayCommand<DownloadTaskRecord>(RetryTaskAsync);
        OpenFileLocationCommand = new AsyncRelayCommand<DownloadTaskRecord>(OpenFileLocationAsync);
        StartCommand = new AsyncRelayCommand(StartAsync);
        StopCommand = new AsyncRelayCommand(StopAsync);

        // G2: 单任务控制命令初始化
        PauseTaskCommand = new AsyncRelayCommand<DownloadTaskRecord>(PauseTaskAsync);
        ResumeTaskCommand = new AsyncRelayCommand<DownloadTaskRecord>(ResumeTaskAsync);
        CancelTaskCommand = new AsyncRelayCommand<DownloadTaskRecord>(CancelTaskAsync);
        RestartTaskCommand = new AsyncRelayCommand<DownloadTaskRecord>(RestartTaskAsync);
        PauseAllCommand = new AsyncRelayCommand(() => _coordinator.PauseAllActiveAsync());
        ResumeAllCommand = new AsyncRelayCommand(() => _coordinator.ResumeAllPausedAsync());

        // G4: 多选与批量命令初始化
        SelectAllFilteredCommand = new RelayCommand(SelectAllFiltered);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        BatchDeleteCommand = new AsyncRelayCommand(BatchDeleteAsync);
        BatchRetryCommand = new AsyncRelayCommand(BatchRetryAsync);
        BatchRestartCommand = new AsyncRelayCommand(BatchRestartAsync);
        BatchPauseCommand = new AsyncRelayCommand(BatchPauseAsync);
        BatchResumeCommand = new AsyncRelayCommand(BatchResumeAsync);

        // 订阅 Coordinator 事件（任务进度/状态/列表变更）
        _coordinator.TaskProgressChanged += task =>
        {
            _uiDispatcher.Post(() =>
            {
                if (_itemIndex.TryGetValue(task.TaskId, out var uiItem))
                    uiItem.RefreshFrom(task);
                UpdateCounts();
            });
            // 设计思考：进度更新不触发 ApplyFilterAndSort，
            // 因为进度变化不影响筛选条件（标题/状态分组/Document 不变），
            // 仅通过 INPC 通知 UI 更新进度条和速度文本即可。
        };
        _coordinator.TaskStatusChanged += task =>
        {
            _uiDispatcher.Post(() =>
            {
                if (_itemIndex.TryGetValue(task.TaskId, out var uiItem))
                    uiItem.RefreshFrom(task);
                UpdateCounts();
                if (!_isBatchOperating)
                    ApplyFilterAndSort();
            });
        };
        _coordinator.TaskListChanged += () =>
        {
            _ = _uiDispatcher.InvokeAsync(ReloadTasksAsync);
        };
    }

    // ── G4: 筛选/排序属性变更回调 ──

    /// <summary>搜索关键词变化时重新筛选</summary>
    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = new CancellationTokenSource();
        _ = ApplySearchAfterDebounceAsync(_searchDebounce.Token);
    }

    private async Task ApplySearchAfterDebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(180, cancellationToken);
            await _uiDispatcher.InvokeAsync(ApplyFilterAndSort);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>状态筛选变化时重新筛选</summary>
    partial void OnStatusFilterChanged(string value) => ApplyFilterAndSort();

    /// <summary>Document 筛选变化时重新筛选</summary>
    partial void OnDocumentFilterChanged(string value) => ApplyFilterAndSort();

    /// <summary>排序方式变化时重新排序</summary>
    partial void OnSortByChanged(string value) => ApplyFilterAndSort();
    partial void OnDateRangeChanged(TaskDateRange value) => ApplyFilterAndSort();
    partial void OnSelectedStatusOptionChanged(TaskChoiceOption? value)
    {
        if (value is not null) StatusFilter = value.Value;
    }
    partial void OnSelectedSortOptionChanged(TaskChoiceOption? value)
    {
        if (value is not null) SortBy = value.Value;
    }
    partial void OnSelectedDateOptionChanged(TaskChoiceOption? value)
    {
        DateRange = value?.Value switch
        {
            "today" => TaskDateRange.Today,
            "7d" => TaskDateRange.Last7Days,
            "30d" => TaskDateRange.Last30Days,
            _ => TaskDateRange.All,
        };
    }
    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnIsLoadingChanged(bool value) => RefreshStateProperties();
    partial void OnHasLoadErrorChanged(bool value) => RefreshStateProperties();

    // ──────────────────────────────────────────────────────────────────────
    // 核心方法
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 从 Coordinator 加载所有任务到 UI
    /// </summary>
    public async Task ReloadTasksAsync()
    {
        IsLoading = true;
        HasLoadError = false;
        try
        {
            var allTasks = await _coordinator.LoadAllTasksAsync();
            Tasks.Clear();
            TaskItems.Clear();
            _taskIndex.Clear();
            _itemIndex.Clear();
            foreach (var t in allTasks)
            {
                Tasks.Add(t);
                _taskIndex[t.TaskId] = t;
                var item = new DownloadTaskItemViewModel(t);
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(DownloadTaskItemViewModel.IsSelected))
                        UpdateSelectedCount();
                };
                TaskItems.Add(item);
                _itemIndex[t.TaskId] = item;
            }
            UpdateCounts();
            UpdateAvailableDocuments();
            ApplyFilterAndSort();
        }
        catch (Exception ex)
        {
            HasLoadError = true;
            LoadErrorMessage = $"任务加载失败：{ex.Message}";
            _onStatusMessage(LoadErrorMessage);
        }
        finally
        {
            IsLoading = false;
            RefreshStateProperties();
        }
    }

    /// <summary>
    /// G4: 重新计算筛选排序结果并更新 FilteredTasks。
    /// 设计思考：
    /// - 委托 TaskFilterSortEngine 纯函数执行实际筛选排序（SRP）；
    /// - 使用 Clear + 逐条 Add 重填集合（100 条级别开销可忽略）；
    /// - 进度更新不调用此方法，仅状态变更/增删/筛选条件变化时调用。
    /// </summary>
    private void ApplyFilterAndSort()
    {
        var criteria = new TaskFilterCriteria(
            TitleContains: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            StatusGroup: StatusFilter,
            DocumentId: DocumentFilter,
            DateRange: DateRange);

        var (sortField, sortDescending) = TaskFilterSortEngine.ParseSortBy(SortBy);
        var result = TaskFilterSortEngine.Apply(Tasks.ToList(), criteria, sortField, sortDescending);

        FilteredTasks.Clear();
        foreach (var task in result)
            if (_itemIndex.TryGetValue(task.TaskId, out var item))
                FilteredTasks.Add(item);

        FilteredCount = FilteredTasks.Count;
        UpdateSelectedCount();
        RefreshStateProperties();
    }

    /// <summary>
    /// G4: 从全量任务中提取去重的 DocumentId 列表，供筛选下拉框使用。
    /// </summary>
    private void UpdateAvailableDocuments()
    {
        var documents = Tasks
            .Select(t => t.DocumentId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        AvailableDocuments.Clear();
        AvailableDocuments.Add("all");
        AvailableDocumentOptions.Clear();
        AvailableDocumentOptions.Add(new TaskDocumentFilterOption("all", "全部工作台"));
        foreach (var doc in documents)
        {
            AvailableDocuments.Add(doc);
            var task = Tasks.First(t => t.DocumentId == doc);
            var label = !string.IsNullOrWhiteSpace(task.SourceDocumentTitle)
                ? task.SourceDocumentTitle
                : $"工作台 {doc[..Math.Min(8, doc.Length)]}";
            AvailableDocumentOptions.Add(new TaskDocumentFilterOption(doc, label));
        }
    }

    private async Task StartAsync()
    {
        _coordinator.StartProcessingAsync();
        await Task.CompletedTask;
    }

    private async Task StopAsync()
    {
        await _coordinator.StopProcessingAsync();
    }

    /// <summary>
    /// 清除已完成的任务
    /// </summary>
    private async Task ClearDoneTasksAsync()
    {
        var count = Tasks.Count(t => t.Status == "done");
        if (count == 0) return;
        if (!await _confirmationService.ConfirmAsync(
                "清除已完成任务",
                $"将移除 {count} 条已完成任务记录，不会删除本地文件。确定继续？"))
            return;
        try
        {
            await _taskStore.DeleteDoneAsync();
            var doneTasks = Tasks.Where(t => t.Status == "done").ToList();
            foreach (var t in doneTasks)
            {
                RemoveProjection(t);
            }
            UpdateCounts();
            UpdateAvailableDocuments();
            ApplyFilterAndSort();
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 删除单个任务：委托给 Coordinator
    /// </summary>
    private async Task DeleteTaskAsync(DownloadTaskRecord? task)
    {
        if (task == null) return;

        try
        {
            var options = await GetDeleteOptionsAsync([task]);
            if (options is null) return;
            await _coordinator.DeleteTaskAsync(task, options);
            RemoveProjection(task);
            UpdateCounts();
            ApplyFilterAndSort();
        }
        catch (Exception ex)
        {
            _onStatusMessage($"删除任务失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 重试/恢复任务：委托给 Coordinator
    /// </summary>
    private async Task RetryTaskAsync(DownloadTaskRecord? task)
    {
        if (task == null) return;

        try
        {
            await _coordinator.RetryTaskAsync(task);
            UpdateCounts();
        }
        catch (Exception ex)
        {
            _onStatusMessage($"重试任务失败: {ex.Message}");
        }
    }

    public void UpdateCounts()
    {
        PendingCount = Tasks.Count(t =>
            t.Status is "pending" or "downloading_video" or "downloading_audio" or "merging");
        CompletedCount = Tasks.Count(t => t.Status == "done");
    }

    /// <summary>
    /// G4: 更新选中计数。
    /// 设计思考：统计全量 Tasks 中 IsSelected 的数量（而非仅 FilteredTasks），
    /// 确保用户切换筛选条件后仍能看到总选中数。
    /// </summary>
    private void UpdateSelectedCount()
    {
        SelectedCount = TaskItems.Count(t => t.IsSelected);
    }

    // ──────────────────────────────────────────────────────────────────────
    // G4: 多选操作
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// G4: 全选当前筛选结果。
    /// 设计思考：只选中当前 FilteredTasks 中的任务（而非全量 Tasks），
    /// 符合"全选筛选结果"的用户直觉。
    /// </summary>
    private void SelectAllFiltered()
    {
        foreach (var task in FilteredTasks)
            task.IsSelected = true;
        UpdateSelectedCount();
    }

    /// <summary>
    /// G4: 取消所有选择（全量清除，而非仅清除当前筛选结果中的选择）
    /// </summary>
    private void ClearSelection()
    {
        foreach (var task in TaskItems)
            task.IsSelected = false;
        UpdateSelectedCount();
    }

    // ──────────────────────────────────────────────────────────────────────
    // G4: 批量命令（带快照语义和确认机制）
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// G4: 获取当前选中任务的快照。
    /// 设计思考：批量命令在执行开始时捕获快照，后续集合变更不影响操作范围。
    /// 这满足了 ROADMAP 的"全选筛选结果只作用于命令开始时的结果快照"要求。
    /// </summary>
    private List<DownloadTaskRecord> GetSelectedSnapshot()
    {
        return TaskItems.Where(t => t.IsSelected).Select(t => t.Record).ToList();
    }

    /// <summary>
    /// G4: 批量删除选中任务。
    /// 破坏性操作：删除临时文件和成品文件，不可撤销，需要用户确认。
    /// </summary>
    private async Task BatchDeleteAsync()
    {
        var snapshot = GetSelectedSnapshot();
        if (snapshot.Count == 0) return;

        var options = await GetDeleteOptionsAsync(snapshot);
        if (options is null) return;

        _isBatchOperating = true;
        try
        {
            foreach (var task in snapshot)
            {
                try
                {
                    await _coordinator.DeleteTaskAsync(task, options);
                    RemoveProjection(task);
                }
                catch (Exception ex)
                {
                    _onStatusMessage($"删除任务 [{task.ItemTitle}] 失败: {ex.Message}");
                }
            }
            UpdateCounts();
            UpdateAvailableDocuments();
        }
        finally
        {
            _isBatchOperating = false;
            ApplyFilterAndSort();
        }
    }

    /// <summary>
    /// G4: 批量重试选中任务中的失败/中断任务。
    /// 非破坏性操作（恢复下载），不需要确认。
    /// 设计思考：只处理 Failed 和 Interrupted 状态的任务，跳过其他状态，
    /// 避免对已完成或运行中的任务产生副作用。
    /// </summary>
    private async Task BatchRetryAsync()
    {
        var snapshot = GetSelectedSnapshot()
            .Where(t =>
            {
                var status = DownloadTaskStatusMapper.FromStorageString(t.Status);
                return status is DownloadTaskStatus.Failed or DownloadTaskStatus.Interrupted;
            })
            .ToList();
        if (snapshot.Count == 0) return;

        _isBatchOperating = true;
        try
        {
            foreach (var task in snapshot)
            {
                try { await _coordinator.RetryTaskAsync(task); }
                catch (Exception ex) { _onStatusMessage($"重试任务 [{task.ItemTitle}] 失败: {ex.Message}"); }
            }
            UpdateCounts();
        }
        finally
        {
            _isBatchOperating = false;
            ApplyFilterAndSort();
        }
    }

    /// <summary>
    /// G4: 批量重新开始选中任务。
    /// 破坏性操作：清理旧断点和临时文件从零下载，需要用户确认。
    /// </summary>
    private async Task BatchRestartAsync()
    {
        var snapshot = GetSelectedSnapshot()
            .Where(t =>
            {
                var status = DownloadTaskStatusMapper.FromStorageString(t.Status);
                return status is DownloadTaskStatus.Failed
                    or DownloadTaskStatus.Interrupted
                    or DownloadTaskStatus.Canceled;
            })
            .ToList();
        if (snapshot.Count == 0) return;

        var confirmed = await _confirmationService.ConfirmAsync(
            "批量重新开始确认",
            $"即将重新开始 {snapshot.Count} 个任务（将清理旧断点从零下载）。确定继续？");
        if (!confirmed) return;

        _isBatchOperating = true;
        try
        {
            foreach (var task in snapshot)
            {
                try { await _coordinator.RestartTaskAsync(task.TaskId); }
                catch (Exception ex) { _onStatusMessage($"重新开始任务 [{task.ItemTitle}] 失败: {ex.Message}"); }
            }
            UpdateCounts();
        }
        finally
        {
            _isBatchOperating = false;
            ApplyFilterAndSort();
        }
    }

    /// <summary>
    /// G4: 批量暂停选中任务中的运行中任务。
    /// 非破坏性操作，不需要确认。
    /// </summary>
    private async Task BatchPauseAsync()
    {
        var snapshot = GetSelectedSnapshot()
            .Where(t => DownloadTaskStatusMapper.IsRunning(
                DownloadTaskStatusMapper.FromStorageString(t.Status)))
            .ToList();
        if (snapshot.Count == 0) return;

        _isBatchOperating = true;
        try
        {
            foreach (var task in snapshot)
            {
                try { await _coordinator.PauseTaskAsync(task.TaskId); }
                catch (Exception ex) { _onStatusMessage($"暂停任务 [{task.ItemTitle}] 失败: {ex.Message}"); }
            }
            UpdateCounts();
        }
        finally
        {
            _isBatchOperating = false;
            ApplyFilterAndSort();
        }
    }

    /// <summary>
    /// G4: 批量恢复选中任务中的暂停/等待登录任务。
    /// 非破坏性操作，不需要确认。
    /// </summary>
    private async Task BatchResumeAsync()
    {
        var snapshot = GetSelectedSnapshot()
            .Where(t =>
            {
                var status = DownloadTaskStatusMapper.FromStorageString(t.Status);
                return status is DownloadTaskStatus.Paused or DownloadTaskStatus.WaitingForLogin;
            })
            .ToList();
        if (snapshot.Count == 0) return;

        _isBatchOperating = true;
        try
        {
            foreach (var task in snapshot)
            {
                try { await _coordinator.ResumeTaskAsync(task.TaskId); }
                catch (Exception ex) { _onStatusMessage($"恢复任务 [{task.ItemTitle}] 失败: {ex.Message}"); }
            }
            UpdateCounts();
        }
        finally
        {
            _isBatchOperating = false;
            ApplyFilterAndSort();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // G2: 单任务控制
    // ──────────────────────────────────────────────────────────────────────

    #region G2 单任务控制

    private async Task PauseTaskAsync(DownloadTaskRecord? task)
    {
        if (task == null) return;
        try
        {
            await _coordinator.PauseTaskAsync(task.TaskId);
            UpdateCounts();
        }
        catch (Exception ex)
        {
            _onStatusMessage($"暂停任务失败: {ex.Message}");
        }
    }

    private async Task ResumeTaskAsync(DownloadTaskRecord? task)
    {
        if (task == null) return;
        try
        {
            await _coordinator.ResumeTaskAsync(task.TaskId);
            UpdateCounts();
        }
        catch (Exception ex)
        {
            _onStatusMessage($"恢复任务失败: {ex.Message}");
        }
    }

    private async Task CancelTaskAsync(DownloadTaskRecord? task)
    {
        if (task == null) return;
        try
        {
            await _coordinator.CancelTaskAsync(task.TaskId);
            UpdateCounts();
        }
        catch (Exception ex)
        {
            _onStatusMessage($"取消任务失败: {ex.Message}");
        }
    }

    private async Task RestartTaskAsync(DownloadTaskRecord? task)
    {
        if (task == null) return;
        try
        {
            await _coordinator.RestartTaskAsync(task.TaskId);
            UpdateCounts();
        }
        catch (Exception ex)
        {
            _onStatusMessage($"重新开始任务失败: {ex.Message}");
        }
    }

    #endregion

    // ──────────────────────────────────────────────────────────────────────
    // 打开文件所在位置
    // ──────────────────────────────────────────────────────────────────────

    #region 打开文件所在位置

    private async Task OpenFileLocationAsync(DownloadTaskRecord? task)
    {
        if (task == null) return;

        try
        {
            var target = !string.IsNullOrWhiteSpace(task.OutputFilePath)
                ? task.OutputFilePath
                : task.FullOutputPath;
            await _fileRevealService.RevealAsync(target);
        }
        catch (Exception ex)
        {
            _onStatusMessage($"打开文件位置失败: {ex.Message}");
        }
    }

    #endregion

    private void RemoveProjection(DownloadTaskRecord task)
    {
        Tasks.Remove(task);
        _taskIndex.Remove(task.TaskId);
        if (_itemIndex.Remove(task.TaskId, out var item))
            TaskItems.Remove(item);
        RefreshStateProperties();
    }

    private void RefreshStateProperties()
    {
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(HasFilteredTasks));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoResultsState));
        OnPropertyChanged(nameof(HasSelection));
    }

    private async Task<DeleteTaskOptions?> GetDeleteOptionsAsync(
        IReadOnlyCollection<DownloadTaskRecord> tasks)
    {
        if (_confirmationService is IUserPromptService prompts)
        {
            var result = await prompts.ConfirmDeleteAsync(
                tasks.Count,
                tasks.Any(t => !string.IsNullOrWhiteSpace(t.OutputFilePath)));
            return result.Confirmed
                ? new DeleteTaskOptions(result.DeleteTemporaryFiles, result.DeleteOutputFile)
                : null;
        }

        var confirmed = await _confirmationService.ConfirmAsync(
            "删除任务",
            $"将移除 {tasks.Count} 个任务记录，不会删除本地文件。确定继续？");
        return confirmed ? DeleteTaskOptions.RecordOnly : null;
    }
}
