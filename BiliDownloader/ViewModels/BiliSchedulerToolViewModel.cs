using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using BiliDownloader.Services;
using BiliDownloader.ViewModels.BiliScheduler;

namespace BiliDownloader.ViewModels;

/// <summary>
/// BiliSchedulerTool 协调器 ViewModel：组合子 VM，编排初始化，路由全局状态。
/// 所有具体逻辑委托给子 ViewModel（TaskList、Settings）。
/// </summary>
public partial class BiliSchedulerToolViewModel : Tool
{
    private readonly BiliDownloadCoordinator _coordinator;
    private bool _initialized;

    [ObservableProperty]
    private string _schedulerStatus = "调度器就绪";

    #region 子 ViewModel

    public SchedulerTaskListViewModel TaskList { get; }
    public SchedulerSettingsViewModel Settings { get; }

    #endregion

    public BiliSchedulerToolViewModel(BiliDownloadCoordinator coordinator)
    {
        _coordinator = coordinator;
        var taskStore = new DownloadTaskStore();

        TaskList = new SchedulerTaskListViewModel(coordinator, taskStore,
            onStatusMessage: msg => SchedulerStatus = msg);

        Settings = new SchedulerSettingsViewModel(taskStore);

        // 订阅 Coordinator 全局状态事件
        _coordinator.SchedulerStatusChanged += status => SchedulerStatus = status;
    }

    /// <summary>
    /// 初始化：Coordinator 建表 -> 加载设置 -> 检测 ffmpeg -> 加载任务
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            // Coordinator 初始化（建表 + 迁移 Interrupted）
            await _coordinator.InitializeAsync();

            // 加载设置（ffmpeg 路径 + 默认输出目录）
            await Settings.LoadSettingsAsync();

            // 检测 ffmpeg
            await Settings.CheckFfmpegAsync();

            // 加载所有任务到 UI
            await TaskList.ReloadTasksAsync();

            var totalCount = TaskList.Tasks.Count;
            SchedulerStatus = $"已加载 {totalCount} 个任务";

            // 统计中断数量
            var interruptedCount = TaskList.Tasks.Count(t => t.Status == "interrupted");
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
