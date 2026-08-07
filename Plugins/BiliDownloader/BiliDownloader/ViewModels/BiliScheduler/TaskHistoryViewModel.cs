using System.Collections.ObjectModel;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.History;
using BiliDownloader.Services.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiliDownloader.ViewModels.BiliScheduler;

/// <summary>
/// 历史中心编排 ViewModel。查询、文件系统、导出、重新下载和用户提示分别通过窄接口注入；
/// 本类只维护会话选择、已知文件状态和命令时序，不包含 SQL、CSV 或路径异常判断。
/// </summary>
public partial class TaskHistoryViewModel : ObservableObject
{
    private readonly ITaskHistoryQueryService _history;
    private readonly IOutputFileStatusService _fileStatus;
    private readonly ITaskHistoryExporter _exporter;
    private readonly ITaskHistoryRedownloadService _redownload;
    private readonly IDownloadSubmissionService _submission;
    private readonly IDownloadFailureActionService _failureActions;
    private readonly IUserPromptService _prompts;
    private readonly IHistoryExportDestinationPicker _destinationPicker;
    private readonly IFileRevealService _fileReveal;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<string> _onStatusMessage;
    private readonly HashSet<string> _selectedTaskIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FilePresenceStatus> _knownFileStatuses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskHistoryItemViewModel> _itemIndex = new(StringComparer.Ordinal);
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _checkCancellation;
    private CancellationTokenSource? _searchDebounce;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusFilter = "all";
    [ObservableProperty] private string _documentFilter = "all";
    [ObservableProperty] private string _dateFilter = "all";
    [ObservableProperty] private string _codecFilter = "all";
    [ObservableProperty] private string _containerFilter = "all";
    [ObservableProperty] private string _outputModeFilter = "all";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isCheckingFiles;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private int _filteredCount;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public ObservableCollection<TaskHistoryItemViewModel> Items { get; } = new();
    public ObservableCollection<TaskDocumentFilterOption> DocumentOptions { get; } =
        [new TaskDocumentFilterOption("all", "全部工作台")];

    public IReadOnlyList<TaskChoiceOption> StatusOptions { get; } =
    [
        new("all", "全部历史状态"), new("done", "已完成"),
        new("failed", "失败"), new("canceled", "已取消"),
    ];
    public IReadOnlyList<TaskChoiceOption> DateOptions { get; } =
    [
        new("all", "全部时间"), new("today", "今天"),
        new("7d", "最近 7 天"), new("30d", "最近 30 天"),
    ];
    public IReadOnlyList<TaskChoiceOption> CodecOptions { get; } =
    [
        new("all", "全部编码"), new("unknown", "未知编码"), new("AutoCompatibility", "自动兼容"),
        new("Avc", "AVC"), new("Hevc", "HEVC"), new("Av1", "AV1"),
    ];
    public IReadOnlyList<TaskChoiceOption> ContainerOptions { get; } =
    [
        new("all", "全部容器"), new("unknown", "未知容器"),
        new("Mp4", "MP4"), new("Mkv", "MKV"), new("NativeAudio", "原生音频"),
    ];
    public IReadOnlyList<TaskChoiceOption> OutputModeOptions { get; } =
    [
        new("all", "全部输出模式"), new("unknown", "未知模式"),
        new("AudioVideo", "音视频"), new("VideoOnly", "仅视频"), new("AudioOnly", "仅音频"),
    ];

    public bool HasItems => Items.Count > 0;
    public bool ShowEmptyState => !IsLoading && !HasItems && string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasSelection => SelectedCount > 0;

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand ClearSelectionCommand { get; }
    public IAsyncRelayCommand<TaskHistoryItemViewModel> CheckItemCommand { get; }
    public IAsyncRelayCommand CheckSelectedCommand { get; }
    public IAsyncRelayCommand CheckFilteredCommand { get; }
    public IRelayCommand CancelCheckCommand { get; }
    public IAsyncRelayCommand ExportSelectedCsvCommand { get; }
    public IAsyncRelayCommand ExportSelectedJsonCommand { get; }
    public IAsyncRelayCommand ExportFilteredCsvCommand { get; }
    public IAsyncRelayCommand ExportFilteredJsonCommand { get; }
    public IAsyncRelayCommand<TaskHistoryItemViewModel> RetryOriginalCommand { get; }
    public IAsyncRelayCommand<TaskHistoryItemViewModel> RedownloadCommand { get; }
    public IAsyncRelayCommand<TaskHistoryItemViewModel> RevealCommand { get; }

    public TaskHistoryViewModel(
        ITaskHistoryQueryService history,
        IOutputFileStatusService fileStatus,
        ITaskHistoryExporter exporter,
        ITaskHistoryRedownloadService redownload,
        IDownloadSubmissionService submission,
        IDownloadFailureActionService failureActions,
        IUserPromptService prompts,
        IHistoryExportDestinationPicker destinationPicker,
        IFileRevealService fileReveal,
        IUiDispatcher uiDispatcher,
        Action<string>? onStatusMessage = null)
    {
        _history = history;
        _fileStatus = fileStatus;
        _exporter = exporter;
        _redownload = redownload;
        _submission = submission;
        _failureActions = failureActions;
        _prompts = prompts;
        _destinationPicker = destinationPicker;
        _fileReveal = fileReveal;
        _dispatcher = uiDispatcher;
        _onStatusMessage = onStatusMessage ?? (_ => { });

        RefreshCommand = new AsyncRelayCommand(ReloadAsync);
        SelectAllCommand = new RelayCommand(SelectAll);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        CheckItemCommand = new AsyncRelayCommand<TaskHistoryItemViewModel>(CheckItemAsync);
        CheckSelectedCommand = new AsyncRelayCommand(CheckSelectedAsync);
        CheckFilteredCommand = new AsyncRelayCommand(CheckFilteredAsync);
        CancelCheckCommand = new RelayCommand(() => _checkCancellation?.Cancel());
        ExportSelectedCsvCommand = new AsyncRelayCommand(() => ExportAsync(TaskHistoryExportFormat.Csv, selectedOnly: true));
        ExportSelectedJsonCommand = new AsyncRelayCommand(() => ExportAsync(TaskHistoryExportFormat.Json, selectedOnly: true));
        ExportFilteredCsvCommand = new AsyncRelayCommand(() => ExportAsync(TaskHistoryExportFormat.Csv, selectedOnly: false));
        ExportFilteredJsonCommand = new AsyncRelayCommand(() => ExportAsync(TaskHistoryExportFormat.Json, selectedOnly: false));
        RetryOriginalCommand = new AsyncRelayCommand<TaskHistoryItemViewModel>(RetryOriginalAsync);
        RedownloadCommand = new AsyncRelayCommand<TaskHistoryItemViewModel>(RedownloadAsync);
        RevealCommand = new AsyncRelayCommand<TaskHistoryItemViewModel>(RevealAsync);
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;
        _ = DebounceReloadAsync(token);
    }

    private async Task DebounceReloadAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(180, token);
            await ReloadAsync();
        }
        catch (OperationCanceledException) { }
    }

    partial void OnStatusFilterChanged(string value) => _ = ReloadAsync();
    partial void OnDocumentFilterChanged(string value) => _ = ReloadAsync();
    partial void OnDateFilterChanged(string value) => _ = ReloadAsync();
    partial void OnCodecFilterChanged(string value) => _ = ReloadAsync();
    partial void OnContainerFilterChanged(string value) => _ = ReloadAsync();
    partial void OnOutputModeFilterChanged(string value) => _ = ReloadAsync();
    partial void OnIsLoadingChanged(bool value) => RefreshState();
    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(HasSelection));

    public async Task ReloadAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var query = BuildQuery();
            var loaded = new List<TaskHistoryEntry>();
            string? cursor = null;
            do
            {
                var page = await _history.QueryPageAsync(query, new TaskHistoryPageRequest(100, cursor), token);
                loaded.AddRange(page.Items);
                cursor = page.NextCursor;
                if (!page.HasMore) break;
                await Task.Yield();
            } while (true);

            var documents = await _history.GetDocumentOptionsAsync(token);
            await _dispatcher.InvokeAsync(() =>
            {
                Items.Clear();
                _itemIndex.Clear();
                foreach (var entry in loaded) AddItem(entry);
                FilteredCount = Items.Count;
                DocumentOptions.Clear();
                DocumentOptions.Add(new TaskDocumentFilterOption("all", "全部工作台"));
                foreach (var document in documents)
                    DocumentOptions.Add(new TaskDocumentFilterOption(document.Id, document.Label));
                RefreshState();
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ErrorMessage = $"历史加载失败：{SensitiveDataSanitizer.Sanitize(ex.Message)}";
            _onStatusMessage(ErrorMessage);
        }
        finally
        {
            if (!token.IsCancellationRequested) IsLoading = false;
        }
    }

    public TaskHistoryQuery BuildQuery()
    {
        IReadOnlySet<DownloadTaskStatus>? statuses = StatusFilter switch
        {
            "done" => new HashSet<DownloadTaskStatus> { DownloadTaskStatus.Completed },
            "failed" => new HashSet<DownloadTaskStatus> { DownloadTaskStatus.Failed },
            "canceled" => new HashSet<DownloadTaskStatus> { DownloadTaskStatus.Canceled },
            _ => null,
        };
        var createdFrom = DateFilter switch
        {
            "today" => DateTime.Today,
            "7d" => DateTime.Now.AddDays(-7),
            "30d" => DateTime.Now.AddDays(-30),
            _ => (DateTime?)null,
        };
        return new TaskHistoryQuery(
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            DocumentFilter,
            statuses,
            createdFrom,
            ParseEnum<VideoCodecPreference>(CodecFilter),
            ParseEnum<OutputContainer>(ContainerFilter),
            ParseEnum<OutputMediaMode>(OutputModeFilter),
            IncludeUnknownVideoCodec: CodecFilter == "unknown",
            IncludeUnknownOutputContainer: ContainerFilter == "unknown",
            IncludeUnknownOutputMode: OutputModeFilter == "unknown");
    }

    private void AddItem(TaskHistoryEntry entry)
    {
        var item = new TaskHistoryItemViewModel(
            entry,
            _selectedTaskIds.Contains(entry.TaskId),
            _knownFileStatuses.GetValueOrDefault(entry.TaskId, FilePresenceStatus.Unknown));
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(TaskHistoryItemViewModel.IsSelected)) return;
            if (item.IsSelected) _selectedTaskIds.Add(item.TaskId);
            else _selectedTaskIds.Remove(item.TaskId);
            SelectedCount = _selectedTaskIds.Count;
        };
        Items.Add(item);
        _itemIndex[item.TaskId] = item;
    }

    private void SelectAll()
    {
        foreach (var item in Items) item.IsSelected = true;
    }

    private void ClearSelection()
    {
        _selectedTaskIds.Clear();
        foreach (var item in Items) item.IsSelected = false;
        SelectedCount = 0;
    }

    private Task CheckItemAsync(TaskHistoryItemViewModel? item) => item is null
        ? Task.CompletedTask
        : CheckFilesAsync([new OutputFileReference(item.TaskId, item.Entry.OutputFilePath)]);

    private Task CheckSelectedAsync()
    {
        var files = Items.Where(static item => item.IsSelected)
            .Select(static item => new OutputFileReference(item.TaskId, item.Entry.OutputFilePath))
            .ToArray();
        return CheckFilesAsync(files);
    }

    private async Task CheckFilteredAsync()
    {
        var files = new List<OutputFileReference>();
        await foreach (var entry in _history.StreamAsync(BuildQuery()))
            files.Add(new OutputFileReference(entry.TaskId, entry.OutputFilePath));
        await CheckFilesAsync(files);
    }

    private async Task CheckFilesAsync(IReadOnlyCollection<OutputFileReference> files)
    {
        if (files.Count == 0) return;
        _checkCancellation?.Cancel();
        _checkCancellation?.Dispose();
        _checkCancellation = new CancellationTokenSource();
        var token = _checkCancellation.Token;
        IsCheckingFiles = true;
        try
        {
            await _fileStatus.CheckManyAsync(files, async result =>
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    _knownFileStatuses[result.TaskId] = result.Status;
                    if (_itemIndex.TryGetValue(result.TaskId, out var item))
                        item.FilePresenceStatus = result.Status;
                });
            }, 4, token);
            _onStatusMessage($"已检查 {files.Count} 个历史文件。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _onStatusMessage("文件检查已取消，已完成结果予以保留。");
        }
        finally
        {
            IsCheckingFiles = false;
        }
    }

    private async Task ExportAsync(TaskHistoryExportFormat format, bool selectedOnly)
    {
        var ids = selectedOnly ? _selectedTaskIds.ToArray() : null;
        if (selectedOnly && ids!.Length == 0) return;
        var destination = await _destinationPicker.PickAsync(format);
        if (destination is null) return;
        IsExporting = true;
        try
        {
            var result = await _exporter.ExportAsync(new TaskHistoryExportRequest(
                destination.Path,
                destination.Format,
                BuildQuery(),
                ids,
                new Dictionary<string, FilePresenceStatus>(_knownFileStatuses)));
            _onStatusMessage($"已安全导出 {result.ExportedCount} 条历史记录。" );
        }
        catch (OperationCanceledException)
        {
            _onStatusMessage("历史导出已取消。");
        }
        catch (Exception ex)
        {
            _onStatusMessage($"历史导出失败：{SensitiveDataSanitizer.Sanitize(ex.Message)}");
        }
        finally { IsExporting = false; }
    }

    private async Task RetryOriginalAsync(TaskHistoryItemViewModel? item)
    {
        if (item is null || !item.CanRetryOriginal) return;
        var task = await _history.GetTaskByIdAsync(item.TaskId);
        if (task is null) return;
        var result = await _failureActions.ExecuteAsync(task, DownloadFailureActionKind.Retry);
        _onStatusMessage(result.Message);
        if (result.Success) await ReloadAsync();
    }

    private async Task RedownloadAsync(TaskHistoryItemViewModel? item)
    {
        if (item is null || !item.CanRedownload) return;
        try
        {
            var plan = await _redownload.CreatePlanAsync(item.TaskId);
            if (plan.RequiresCompatibilityConfirmation && !await _prompts.ConfirmAsync(
                    "旧任务兼容重建",
                    string.Join(Environment.NewLine, plan.CompatibilityWarnings) + Environment.NewLine + "是否继续预检？"))
                return;

            var report = await _submission.PreflightAsync(plan.Submission);
            if (report.IsBlocked)
            {
                _onStatusMessage("重新下载预检未通过：" + string.Join("；", report.GlobalIssues
                    .Concat(report.Items.SelectMany(static row => row.Issues))
                    .Where(static issue => issue.Severity == PreflightIssueSeverity.Blocking)
                    .Select(static issue => issue.Message).Distinct()));
                return;
            }
            var confirmed = !report.RequiresConfirmation || await _prompts.ConfirmSubmissionAsync(report);
            if (!confirmed) return;
            var result = await _submission.CommitAsync(new PreparedSubmission(report, confirmed));
            _onStatusMessage(result.Message);
            if (result.Status == SubmissionCommitStatus.Committed) await ReloadAsync();
        }
        catch (Exception ex)
        {
            _onStatusMessage($"重新下载失败：{SensitiveDataSanitizer.Sanitize(ex.Message)}");
        }
    }

    private async Task RevealAsync(TaskHistoryItemViewModel? item)
    {
        if (item is null || !item.CanReveal) return;
        try { await _fileReveal.RevealAsync(item.Entry.OutputFilePath); }
        catch (Exception ex) { _onStatusMessage($"定位文件失败：{SensitiveDataSanitizer.Sanitize(ex.Message)}"); }
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasSelection));
    }

    private static TEnum? ParseEnum<TEnum>(string value) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, true, out var parsed) ? parsed : null;
}
