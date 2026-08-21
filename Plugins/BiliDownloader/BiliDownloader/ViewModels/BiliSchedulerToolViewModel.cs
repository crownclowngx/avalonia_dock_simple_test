using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.History;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels.BiliScheduler;
using BiliDownloader.Constants;
using BiliDownloader.Plugin;

namespace BiliDownloader.ViewModels;

/// <summary>
/// BiliSchedulerTool 协调器 ViewModel：组合子 VM，编排初始化，路由全局状态。
/// 所有具体逻辑委托给子 ViewModel（TaskList、Settings）。
/// </summary>
public partial class BiliSchedulerToolViewModel : ObservableObject, IDisposable
{
    private readonly BiliDownloadCoordinator _coordinator;
    private readonly IBiliDownloaderPluginReadiness _readiness;
    private readonly IUiDispatcher _uiDispatcher;
    private bool _settingsInitialized;
    private int _disposed;

    [ObservableProperty]
    private string _schedulerStatus = "插件尚未初始化。";

    [ObservableProperty]
    private bool _isPluginReady;

    [ObservableProperty]
    private string _pluginReadinessMessage = "插件尚未初始化。";

    /// <summary>调度器是否正在处理任务（UI 绑定用）</summary>
    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActivitySelected))]
    [NotifyPropertyChangedFor(nameof(IsHistorySelected))]
    private string _selectedSection = "active";

    public bool IsActivitySelected => SelectedSection == "active";
    public bool IsHistorySelected => SelectedSection == "history";

    #region 子 ViewModel

    public SchedulerTaskListViewModel TaskList { get; }
    public SchedulerSettingsViewModel Settings { get; }
    public TaskHistoryViewModel? History { get; }

    #endregion

    public BiliSchedulerToolViewModel(
        BiliDownloadCoordinator coordinator,
        IDownloadTaskRepository taskStore,
        ISettingsRepository settingsStore,
        IFfmpegRuntimeLocator ffmpegService,
        IBiliDownloaderPluginReadiness readiness,
        IFfmpegPackageInstaller? ffmpegInstaller = null,
        IConfirmationService? confirmationService = null,
        IFileRevealService? fileRevealService = null,
        IUiDispatcher? uiDispatcher = null,
        IDownloadFailureActionService? failureActionService = null,
        IDownloadFailurePresentationPolicy? failurePolicy = null,
        ITaskHistoryQueryService? historyQuery = null,
        IOutputFileStatusService? outputFileStatus = null,
        ITaskHistoryExporter? historyExporter = null,
        ITaskHistoryRedownloadService? historyRedownload = null,
        IDownloadSubmissionService? submissionService = null,
        IUserPromptService? userPromptService = null,
        IHistoryExportDestinationPicker? historyDestinationPicker = null,
        IGlobalBandwidthLimitService? globalBandwidthLimit = null)
    {
        _coordinator = coordinator;
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _uiDispatcher = uiDispatcher ?? new AvaloniaUiDispatcher();
        TaskList = new SchedulerTaskListViewModel(coordinator, taskStore,
            onStatusMessage: msg => SchedulerStatus = msg,
            confirmationService: confirmationService,
            fileRevealService: fileRevealService,
            uiDispatcher: uiDispatcher,
            failureActionService: failureActionService,
            failurePolicy: failurePolicy,
            activeOnly: true);

        Settings = new SchedulerSettingsViewModel(
            settingsStore, ffmpegService, ffmpegInstaller, globalBandwidthLimit, userPromptService);

        // 旧测试和宿主兼容构造路径可以不提供 G6 服务；生产 DI 会完整注入所有依赖。
        // 使用可空组合而不是在这里临时 new SQLite 或文件选择器，避免破坏依赖倒置。
        if (historyQuery is not null
            && outputFileStatus is not null
            && historyExporter is not null
            && historyRedownload is not null
            && submissionService is not null
            && failureActionService is not null
            && historyDestinationPicker is not null)
        {
            var prompts = userPromptService
                ?? confirmationService as IUserPromptService
                ?? new SafeCancellationConfirmationService();
            History = new TaskHistoryViewModel(
                historyQuery,
                outputFileStatus,
                historyExporter,
                historyRedownload,
                submissionService,
                failureActionService,
                prompts,
                historyDestinationPicker,
                fileRevealService ?? new FileRevealService(),
                uiDispatcher ?? new AvaloniaUiDispatcher(),
                msg => SchedulerStatus = msg);
        }

        // 订阅 Coordinator 全局状态事件
        _coordinator.SchedulerStatusChanged += HandleSchedulerStatusChanged;
        _coordinator.IsProcessingChanged += HandleIsProcessingChanged;
        _coordinator.TaskListChanged += OnTaskListChanged;
        _coordinator.TaskStatusChanged += OnTaskStatusChanged;

        // 订阅并发下载数变更事件，同步到 Coordinator
        Settings.MaxConcurrentDownloadsChanged += OnMaxConcurrentDownloadsChanged;
        _readiness.Changed += OnReadinessChanged;
        ApplyReadiness(_readiness.Snapshot);
    }

    /// <summary>
    /// 激活 Tool 的界面投影。
    /// <para>
    /// 插件级 Coordinator 已由宿主生命周期初始化；这里仅加载设置和任务列表，
    /// 因而 Tool 被隐藏、恢复或重复附加到视觉树都不会控制下载后台服务的生存期。
    /// </para>
    /// </summary>
    public async Task ActivateAsync()
    {
        var readiness = _readiness.Snapshot;
        ApplyReadiness(readiness);
        if (!readiness.IsReady)
        {
            // readiness 门禁必须位于任何设置、SQLite、FFmpeg 或 Coordinator 读取之前。
            // 这使 Host 即使因布局恢复提前请求 View，也只能得到安全的不可用投影。
            SchedulerStatus = readiness.Message;
            return;
        }

        try
        {
            if (!_settingsInitialized)
            {
                _settingsInitialized = true;

                // 设置和本地 ffmpeg 路径只需初始化一次；这里只执行本地 `-version` 进程探测，
                // 不访问网络，也不会隐式进入安装流程。
                await Settings.LoadSettingsAsync();
                _coordinator.SetMaxConcurrentDownloads(Settings.MaxConcurrentDownloads);
                await Settings.CheckFfmpegAsync();
            }

            // 每次 Tool 重新进入视觉树都从事实源刷新投影，弥补隐藏期间可能错过的 UI 通知。
            await TaskList.ReloadTasksAsync();
            if (IsHistorySelected && History is not null)
                await History.ReloadAsync();

            var totalCount = TaskList.Tasks.Count;
            SchedulerStatus = $"已加载 {totalCount} 个任务";

            // 统计中断数量
            var interruptedCount = TaskList.Tasks.Count(t =>
                DownloadTaskStatusMapper.FromStorageString(t.Status) == DownloadTaskStatus.Interrupted);
            if (interruptedCount > 0)
            {
                SchedulerStatus = $"已加载 {totalCount} 个任务，{interruptedCount} 个已中断（需手动恢复）";
            }
        }
        catch (Exception ex)
        {
            SchedulerStatus = $"初始化失败: {ex.Message}";
        }
    }

    partial void OnSelectedSectionChanged(string value)
    {
        if (value == "history" && History is not null)
            _ = History.ReloadAsync();
    }

    [RelayCommand]
    private void SelectSection(string? section)
    {
        if (section is "active" or "history") SelectedSection = section;
    }

    private void RefreshVisibleHistory()
    {
        if (IsHistorySelected && History is not null)
            _ = History.ReloadAsync();
    }

    private void OnReadinessChanged(object? sender, EventArgs args)
    {
        var snapshot = _readiness.Snapshot;
        _uiDispatcher.Post(() =>
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            ApplyReadiness(snapshot);
            if (snapshot.IsReady)
                _ = ActivateAsync();
            else
                SchedulerStatus = snapshot.Message;
        });
    }

    private void ApplyReadiness(BiliDownloaderReadinessSnapshot snapshot)
    {
        IsPluginReady = snapshot.IsReady;
        PluginReadinessMessage = snapshot.Message;
    }

    private void HandleSchedulerStatusChanged(string status) =>
        _uiDispatcher.Post(() =>
        {
            if (Volatile.Read(ref _disposed) == 0) SchedulerStatus = status;
        });

    private void HandleIsProcessingChanged(bool processing) =>
        _uiDispatcher.Post(() =>
        {
            if (Volatile.Read(ref _disposed) == 0) IsProcessing = processing;
        });

    private void OnTaskListChanged() => RefreshVisibleHistory();

    private void OnTaskStatusChanged(DownloadTaskRecord _) => RefreshVisibleHistory();

    private void OnMaxConcurrentDownloadsChanged(int count) =>
        _coordinator.SetMaxConcurrentDownloads(count);

    /// <summary>
    /// 解除所有插件级强引用订阅。Tool 隐藏不会释放模型；只有 Host 关闭插件 Provider 时释放，
    /// 因而恢复隐藏 Tool 仍复用同一模型和 Coordinator。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _readiness.Changed -= OnReadinessChanged;
        _coordinator.SchedulerStatusChanged -= HandleSchedulerStatusChanged;
        _coordinator.IsProcessingChanged -= HandleIsProcessingChanged;
        _coordinator.TaskListChanged -= OnTaskListChanged;
        _coordinator.TaskStatusChanged -= OnTaskStatusChanged;
        Settings.MaxConcurrentDownloadsChanged -= OnMaxConcurrentDownloadsChanged;
        (TaskList as IDisposable)?.Dispose();
        (Settings as IDisposable)?.Dispose();
        (History as IDisposable)?.Dispose();
    }
}
