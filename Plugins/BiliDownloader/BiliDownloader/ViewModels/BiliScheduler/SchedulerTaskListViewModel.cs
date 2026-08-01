using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.ViewModels.BiliScheduler;

/// <summary>
/// 任务列表子 ViewModel：负责任务展示、调度控制和任务 CRUD。
/// </summary>
public partial class SchedulerTaskListViewModel : ObservableObject
{
    private readonly BiliDownloadCoordinator _coordinator;
    private readonly IDownloadTaskRepository _taskStore;
    private readonly Action<string> _onStatusMessage;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private int _completedCount;

    /// <summary>
    /// 所有任务记录（UI 绑定）
    /// </summary>
    public ObservableCollection<DownloadTaskRecord> Tasks { get; } = new();

    public IRelayCommand ClearDoneCommand { get; }
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

    public SchedulerTaskListViewModel(
        BiliDownloadCoordinator coordinator,
        IDownloadTaskRepository taskStore,
        Action<string> onStatusMessage)
    {
        _coordinator = coordinator;
        _taskStore = taskStore;
        _onStatusMessage = onStatusMessage;

        ClearDoneCommand = new RelayCommand(ClearDoneTasks);
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

        // 订阅 Coordinator 事件（任务进度/状态/列表变更）
        _coordinator.TaskProgressChanged += task =>
        {
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
    /// 从 Coordinator 加载所有任务到 UI
    /// </summary>
    public async Task ReloadTasksAsync()
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

    #region 打开文件所在位置

    private async Task OpenFileLocationAsync(DownloadTaskRecord? task)
    {
        if (task == null) return;
        await Task.CompletedTask;

        try
        {
            var actualDir = string.IsNullOrEmpty(task.SubFolder)
                ? task.OutputDirectory
                : Path.Combine(task.OutputDirectory, task.SubFolder);

            var safeTitle = BiliDownloadService.SanitizeFileName(task.ItemTitle);
            var filePath = Path.Combine(actualDir, $"{safeTitle}.mp4");

            if (File.Exists(filePath))
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else if (Directory.Exists(actualDir))
            {
                Process.Start("explorer.exe", actualDir);
            }
            else
            {
                _onStatusMessage($"目录不存在: {actualDir}");
            }
        }
        catch (Exception ex)
        {
            _onStatusMessage($"打开文件位置失败: {ex.Message}");
        }
    }

    #endregion
}
