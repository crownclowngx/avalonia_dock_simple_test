using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.Message;
using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services;

namespace BiliDownloader.ViewModels;

/// <summary>
/// BiliSchedulerTool ViewModel：纯展示层，负责 UI 绑定和用户命令。
/// 所有下载编排逻辑委托给 BiliDownloadCoordinator。
/// </summary>
public partial class BiliSchedulerToolViewModel : Tool
{
    private readonly BiliDownloadCoordinator _coordinator;
    private readonly DownloadTaskStore _taskStore;
    private bool _initialized;

    [ObservableProperty]
    private string _schedulerStatus = "调度器就绪";

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private bool _ffmpegReady;

    [ObservableProperty]
    private string _ffmpegPath = "";

    [ObservableProperty]
    private string _ffmpegStatus = "检测中...";

    [ObservableProperty]
    private string _defaultOutputDirectory = "";

    /// <summary>
    /// 所有任务记录（UI 绑定）
    /// </summary>
    public ObservableCollection<DownloadTaskRecord> Tasks { get; } = new();

    public IRelayCommand ClearDoneCommand { get; }
    public IAsyncRelayCommand BrowseFfmpegCommand { get; }
    public IAsyncRelayCommand BrowseOutputDirCommand { get; }
    public IAsyncRelayCommand<DownloadTaskRecord> DeleteTaskCommand { get; }
    public IAsyncRelayCommand<DownloadTaskRecord> RetryTaskCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }

    public BiliSchedulerToolViewModel(BiliDownloadCoordinator coordinator)
    {
        _coordinator = coordinator;
        _taskStore = new DownloadTaskStore();

        ClearDoneCommand = new RelayCommand(ClearDoneTasks);
        BrowseFfmpegCommand = new AsyncRelayCommand(BrowseFfmpegAsync);
        BrowseOutputDirCommand = new AsyncRelayCommand(BrowseOutputDirAsync);
        DeleteTaskCommand = new AsyncRelayCommand<DownloadTaskRecord>(DeleteTaskAsync);
        RetryTaskCommand = new AsyncRelayCommand<DownloadTaskRecord>(RetryTaskAsync);
        StartCommand = new AsyncRelayCommand(StartAsync);
        StopCommand = new AsyncRelayCommand(StopAsync);

        // 默认输出目录：程序根目录/视频下载
        var appDir = Path.GetDirectoryName(typeof(BiliSchedulerToolViewModel).Assembly.Location) ?? "";
        DefaultOutputDirectory = Path.Combine(appDir, "视频下载");

        // 订阅 Coordinator 事件
        _coordinator.SchedulerStatusChanged += status => SchedulerStatus = status;
        _coordinator.TaskProgressChanged += task =>
        {
            // 通过 TaskId 找到 UI 集合中的对应对象，赋值触发 ObservableProperty 通知
            var uiTask = Tasks.FirstOrDefault(t => t.TaskId == task.TaskId);
            if (uiTask != null)
            {
                uiTask.Progress = task.Progress;
                uiTask.VideoProgress = task.VideoProgress;
                uiTask.AudioProgress = task.AudioProgress;
                uiTask.MergeProgress = task.MergeProgress;
                uiTask.SpeedText = task.SpeedText;
                uiTask.Status = task.Status;
                uiTask.ErrorMessage = task.ErrorMessage;
            }
            UpdateCounts();
        };
        _coordinator.TaskStatusChanged += task =>
        {
            var uiTask = Tasks.FirstOrDefault(t => t.TaskId == task.TaskId);
            if (uiTask != null)
            {
                uiTask.Status = task.Status;
                uiTask.Progress = task.Progress;
                uiTask.ErrorMessage = task.ErrorMessage;
            }
            UpdateCounts();
        };
        _coordinator.TaskListChanged += () =>
        {
            _ = ReloadTasksAsync();
        };
    }

    /// <summary>
    /// 初始化：委托给 Coordinator + 加载任务列表 + 检测 ffmpeg
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            // Coordinator 初始化（建表 + 迁移 Interrupted）
            await _coordinator.InitializeAsync();

            // 加载全局默认输出目录
            await _taskStore.InitAsync();
            var savedDir = await _taskStore.GetSettingAsync("default_output_dir");
            if (!string.IsNullOrEmpty(savedDir))
                DefaultOutputDirectory = savedDir;

            // 加载已保存的 ffmpeg 自定义路径
            var savedFfmpeg = await _taskStore.GetSettingAsync("ffmpeg_custom_path");
            if (!string.IsNullOrEmpty(savedFfmpeg))
                FfmpegService.CustomPath = savedFfmpeg;

            // 检测 ffmpeg
            await CheckFfmpegAsync();

            // 加载所有任务到 UI
            var allTasks = await _coordinator.LoadAllTasksAsync();
            Tasks.Clear();
            foreach (var t in allTasks)
                Tasks.Add(t);

            UpdateCounts();
            SchedulerStatus = $"已加载 {allTasks.Count} 个任务";

            // 统计中断数量
            var interruptedCount = allTasks.Count(t => t.Status == "interrupted");
            if (interruptedCount > 0)
            {
                SchedulerStatus = $"已加载 {allTasks.Count} 个任务，{interruptedCount} 个已中断（需手动恢复）";
            }
        }
        catch (Exception ex)
        {
            SchedulerStatus = $"初始化失败: {ex.Message}";
        }
    }

    private async Task ReloadTasksAsync()
    {
        try
        {
            var allTasks = await _coordinator.LoadAllTasksAsync();
            Tasks.Clear();
            foreach (var t in allTasks)
                Tasks.Add(t);
            UpdateCounts();
        }
        catch { /* 忽略重新加载失败 */ }
    }

    private async Task StartAsync()
    {
        await InitializeAsync();
        _coordinator.StartProcessingAsync();
    }

    private async Task StopAsync()
    {
        await _coordinator.StopProcessingAsync();
    }

    /// <summary>
    /// 清除已完成的任务
    /// </summary>
    private async void ClearDoneTasks()
    {
        try
        {
            await _taskStore.DeleteDoneAsync();
            var doneTasks = Tasks.Where(t => t.Status == "done").ToList();
            foreach (var t in doneTasks)
                Tasks.Remove(t);
            UpdateCounts();
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
            await _coordinator.DeleteTaskAsync(task);
            Tasks.Remove(task);
            UpdateCounts();
        }
        catch (Exception ex)
        {
            SchedulerStatus = $"删除任务失败: {ex.Message}";
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
            SchedulerStatus = $"重试任务失败: {ex.Message}";
        }
    }

    private void UpdateCounts()
    {
        PendingCount = Tasks.Count(t =>
            t.Status is "pending" or "downloading_video" or "downloading_audio" or "merging");
        CompletedCount = Tasks.Count(t => t.Status == "done");
    }

    #region ffmpeg 管理

    /// <summary>
    /// 检测 ffmpeg 是否就绪
    /// </summary>
    private async Task CheckFfmpegAsync()
    {
        await Task.Run(() =>
        {
            var path = FfmpegService.ResolveFfmpegPath();
            FfmpegReady = path != null;
            FfmpegPath = path ?? "";
            FfmpegStatus = FfmpegReady
                ? $"ffmpeg 就绪: {path}"
                : "ffmpeg 未找到，请将 ffmpeg.exe 放入工具目录或手动浏览选择";
        });
    }

    /// <summary>
    /// 浏览选择 ffmpeg.exe 路径
    /// </summary>
    private async Task BrowseFfmpegAsync()
    {
        try
        {
            var dialog = new Avalonia.Controls.OpenFileDialog
            {
                Title = "选择 ffmpeg.exe",
                Filters = new List<Avalonia.Controls.FileDialogFilter>
                {
                    new() { Name = "可执行文件", Extensions = { "exe" } },
                    new() { Name = "所有文件", Extensions = { "*" } }
                }
            };

            var parentWindow = GetParentWindow();
            if (parentWindow == null) return;

            var result = await dialog.ShowAsync(parentWindow);
            if (result == null || result.Length == 0) return;

            var selectedPath = result[0];
            FfmpegStatus = "正在验证 ffmpeg...";

            var valid = await FfmpegService.ValidatePathAsync(selectedPath);
            if (valid)
            {
                FfmpegService.CustomPath = selectedPath;
                await _taskStore.SetSettingAsync("ffmpeg_custom_path", selectedPath);
                await CheckFfmpegAsync();
            }
            else
            {
                FfmpegStatus = $"无效路径: {selectedPath}";
                FfmpegReady = false;
            }
        }
        catch (Exception ex)
        {
            FfmpegStatus = $"选择 ffmpeg 失败: {ex.Message}";
        }
    }

    private Avalonia.Controls.Window? GetParentWindow()
    {
        try
        {
            var app = Avalonia.Application.Current;
            return app?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region 输出目录管理

    /// <summary>
    /// DefaultOutputDirectory 变化时自动保存到 SQLite
    /// </summary>
    partial void OnDefaultOutputDirectoryChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && _initialized)
            _ = _taskStore.SetSettingAsync("default_output_dir", value);
    }

    /// <summary>
    /// 浏览选择默认输出目录
    /// </summary>
    private async Task BrowseOutputDirAsync()
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择默认下载输出目录"
            };

            var parentWindow = GetParentWindow();
            if (parentWindow != null)
            {
                var result = await dialog.ShowAsync(parentWindow);
                if (!string.IsNullOrEmpty(result))
                    DefaultOutputDirectory = result;
            }
        }
        catch (Exception ex)
        {
            SchedulerStatus = $"选择文件夹失败: {ex.Message}";
        }
    }

    #endregion
}
