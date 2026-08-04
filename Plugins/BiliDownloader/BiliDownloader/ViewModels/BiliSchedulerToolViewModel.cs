using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
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

    #region 子 ViewModel

    public SchedulerTaskListViewModel TaskList { get; }
    public SchedulerSettingsViewModel Settings { get; }

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
        IDownloadFailurePresentationPolicy? failurePolicy = null)
    {
        _coordinator = coordinator;
        _lifecycleManager = lifecycleManager;

        TaskList = new SchedulerTaskListViewModel(coordinator, taskStore,
            onStatusMessage: msg => SchedulerStatus = msg,
            confirmationService: confirmationService,
            fileRevealService: fileRevealService,
            uiDispatcher: uiDispatcher,
            failureActionService: failureActionService,
            failurePolicy: failurePolicy);

        Settings = new SchedulerSettingsViewModel(settingsStore, ffmpegService, ffmpegInstaller);

        // 订阅 Coordinator 全局状态事件
        _coordinator.SchedulerStatusChanged += status => SchedulerStatus = status;
        _coordinator.IsProcessingChanged += processing => IsProcessing = processing;

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
}
