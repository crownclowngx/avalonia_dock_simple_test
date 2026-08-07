using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.History;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels.BiliScheduler;
using MyAvaloniaManagementCommon.Plugin;

namespace BiliDownloader.ViewModels;

/// <summary>
/// BiliSchedulerTool 协调器 ViewModel：组合子 VM，编排初始化，路由全局状态。
/// 所有具体逻辑委托给子 ViewModel（TaskList、Settings）。
/// </summary>
public partial class BiliSchedulerToolViewModel : Tool
{
    private readonly BiliDownloadCoordinator _coordinator;
    private readonly PluginLifecycleManager _lifecycleManager;
    private bool _settingsInitialized;

    [ObservableProperty]
    private string _schedulerStatus = "调度器就绪";

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
        PluginLifecycleManager lifecycleManager,
        IFfmpegRuntimeLocator ffmpegService,
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
        IHistoryExportDestinationPicker? historyDestinationPicker = null)
    {
        _coordinator = coordinator;
        _lifecycleManager = lifecycleManager;

        TaskList = new SchedulerTaskListViewModel(coordinator, taskStore,
            onStatusMessage: msg => SchedulerStatus = msg,
            confirmationService: confirmationService,
            fileRevealService: fileRevealService,
            uiDispatcher: uiDispatcher,
            failureActionService: failureActionService,
            failurePolicy: failurePolicy,
            activeOnly: true);

        Settings = new SchedulerSettingsViewModel(settingsStore, ffmpegService, ffmpegInstaller);

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
        _coordinator.SchedulerStatusChanged += status => SchedulerStatus = status;
        _coordinator.IsProcessingChanged += processing => IsProcessing = processing;
        _coordinator.TaskListChanged += RefreshVisibleHistory;
        _coordinator.TaskStatusChanged += _ => RefreshVisibleHistory();

        // 订阅并发下载数变更事件，同步到 Coordinator
        Settings.MaxConcurrentDownloadsChanged += count =>
            _coordinator.SetMaxConcurrentDownloads(count);
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
        try
        {
            var lifecycleState = _lifecycleManager.GetState("BiliDownloader");
            if (lifecycleState?.Status == PluginLifecycleStatus.Failed)
            {
                SchedulerStatus = $"插件初始化失败: {lifecycleState.ErrorMessage}";
                return;
            }

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
}
