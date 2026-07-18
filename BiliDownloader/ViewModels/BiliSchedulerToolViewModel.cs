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
/// BiliSchedulerTool ViewModel：接收下载任务、持久化到 SQLite、执行下载、回传进度
/// </summary>
public partial class BiliSchedulerToolViewModel : Tool
{
    private readonly IMessengerService _messengerService;
    private readonly DownloadTaskStore _taskStore;
    private readonly BiliApiService _apiService = new();
    private readonly BiliDownloadService _downloadService = new();

    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;
    private bool _isProcessing;
    private bool _initialized;

    // SQLite 进度写入节流：每 500ms 最多写一次，避免高频并发写入
    private DateTime _lastProgressDbWrite = DateTime.MinValue;
    private DateTime _lastBytesDbWrite = DateTime.MinValue;
    private static readonly TimeSpan DbWriteInterval = TimeSpan.FromMilliseconds(500);

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

    public BiliSchedulerToolViewModel()
    {
        _messengerService = new MessengerService();
        _taskStore = new DownloadTaskStore();

        ClearDoneCommand = new RelayCommand(ClearDoneTasks);
        BrowseFfmpegCommand = new AsyncRelayCommand(BrowseFfmpegAsync);
        BrowseOutputDirCommand = new AsyncRelayCommand(BrowseOutputDirAsync);
        DeleteTaskCommand = new AsyncRelayCommand<DownloadTaskRecord>(DeleteTaskAsync);
        RetryTaskCommand = new AsyncRelayCommand<DownloadTaskRecord>(RetryTaskAsync);

        // 默认输出目录：程序根目录/视频下载
        var appDir = Path.GetDirectoryName(typeof(BiliSchedulerToolViewModel).Assembly.Location) ?? "";
        DefaultOutputDirectory = Path.Combine(appDir, "视频下载");

        // 注册监听：接收 Document 发来的下载任务
        _messengerService.Register<BiliSchedulerToolViewModel, SubmitDownloadTaskMessage>(
            this, (vm, msg) =>
            {
                _ = vm.HandleSubmitMessageAsync(msg);
            });
    }

    /// <summary>
    /// 初始化：建表 + 加载历史任务（不自动恢复下载）
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            await _taskStore.InitAsync();

            // 加载全局默认输出目录
            var savedDir = await _taskStore.GetSettingAsync("default_output_dir");
            if (!string.IsNullOrEmpty(savedDir))
                DefaultOutputDirectory = savedDir;

            // 检测 ffmpeg
            await CheckFfmpegAsync();

            SchedulerStatus = "调度器已初始化";

            // 加载所有任务到 UI
            var allTasks = await _taskStore.GetAllAsync();

            // 将异常退出前仍在运行的任务标记为"已中断"，不自动恢复
            var interruptedCount = 0;
            foreach (var t in allTasks)
            {
                if (t.Status is "downloading_video" or "downloading_audio" or "merging")
                {
                    t.Status = "interrupted";
                    await _taskStore.UpdateProgressAsync(t.TaskId, t.Progress, "interrupted");
                    interruptedCount++;
                }
            }

            Tasks.Clear();
            foreach (var t in allTasks)
                Tasks.Add(t);

            UpdateCounts();

            if (interruptedCount > 0)
            {
                SchedulerStatus = $"已加载 {allTasks.Count} 个任务，{interruptedCount} 个已中断（需手动恢复）";
            }
            else
            {
                SchedulerStatus = $"已加载 {allTasks.Count} 个任务";
            }
        }
        catch (Exception ex)
        {
            SchedulerStatus = $"初始化失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 处理 Document 提交的下载任务
    /// </summary>
    private async Task HandleSubmitMessageAsync(SubmitDownloadTaskMessage msg)
    {
        try
        {
            // 确保已初始化
            await InitializeAsync();

            // 将消息中的 Items 拆分为多条 DownloadTaskRecord 存入 SQLite
            var records = new List<DownloadTaskRecord>();
            var subFolder = msg.UseGroupFolder
                ? BiliDownloadService.SanitizeFileName(msg.SeriesTitle)
                : string.Empty;

            foreach (var item in msg.Items)
            {
                var record = new DownloadTaskRecord
                {
                    TaskId = item.ItemId,
                    DocumentId = msg.SourceDocumentId,
                    SeriesTitle = msg.SeriesTitle,
                    ItemTitle = item.Title,
                    Aid = item.Aid,
                    Bvid = item.Bvid,
                    Cid = item.Cid,
                    QualityId = msg.QualityId,
                    AudioQualityId = msg.AudioQualityId,
                    OutputDirectory = msg.OutputDirectory,
                    SubFolder = subFolder,
                    Cookie = msg.Cookie,
                    Status = "pending",
                    CreatedAt = DateTime.Now,
                };
                records.Add(record);
            }

            // 批量插入 SQLite
            await _taskStore.InsertBatchAsync(records);

            // 添加到 UI
            foreach (var r in records)
                Tasks.Add(r);

            UpdateCounts();
            SchedulerStatus = $"已接收 {records.Count} 个新任务";

            // 启动后台处理
            StartProcessing();
        }
        catch (Exception ex)
        {
            SchedulerStatus = $"接收任务失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 启动后台处理队列（幂等：如果已在处理则不重复启动）
    /// </summary>
    private void StartProcessing()
    {
        if (_isProcessing) return;
        _isProcessing = true;
        _processingCts = new CancellationTokenSource();
        _processingTask = ProcessQueueAsync(_processingCts.Token);
    }

    /// <summary>
    /// 后台处理队列：逐个取未完成任务执行下载
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 查找下一个未完成的任务
                var nextTask = Tasks.FirstOrDefault(t =>
                    t.Status is "pending" or "downloading_video" or "downloading_audio" or "merging");

                if (nextTask == null)
                {
                    SchedulerStatus = "所有任务已完成";
                    _isProcessing = false;
                    UpdateCounts();
                    return;
                }

                // 更新临时目录
                if (string.IsNullOrWhiteSpace(nextTask.TempDirectory))
                {
                    var tempBase = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "BiliDownloader", "temp");
                    nextTask.TempDirectory = Path.Combine(tempBase, nextTask.TaskId);
                    await _taskStore.UpdateTempDirectoryAsync(nextTask.TaskId, nextTask.TempDirectory);
                }

                SchedulerStatus = $"正在下载: {nextTask.ItemTitle}";

                // 广播状态变更：开始下载（自动恢复通知）
                BroadcastStatusChanged(nextTask);

                try
                {
                    await _downloadService.DownloadItemAsync(
                        nextTask,
                        _apiService,
                        (info) =>
                        {
                            nextTask.Progress = info.OverallProgress;
                            nextTask.VideoProgress = info.VideoProgress;
                            nextTask.AudioProgress = info.AudioProgress;
                            nextTask.MergeProgress = info.MergeProgress;
                            nextTask.SpeedText = info.SpeedText;
                            nextTask.Status = info.Stage switch
                            {
                                "video" => "downloading_video",
                                "audio" => "downloading_audio",
                                "merging" => "merging",
                                "done" => "done",
                                _ => nextTask.Status,
                            };

                            // 节流写 SQLite：关键状态变更（done/merging）立即写入，其他节流
                            var now = DateTime.UtcNow;
                            var isCriticalState = info.Stage is "done" or "merging";
                            if (isCriticalState || (now - _lastProgressDbWrite) >= DbWriteInterval)
                            {
                                _lastProgressDbWrite = now;
                                _ = _taskStore.UpdateStageProgressAsync(
                                    nextTask.TaskId, nextTask.Progress, nextTask.Status,
                                    nextTask.VideoProgress, nextTask.AudioProgress,
                                    nextTask.MergeProgress, nextTask.SpeedText);
                            }

                            // UI 广播不受节流影响，始终即时更新
                            BroadcastProgress(nextTask);
                            UpdateCounts();
                        },
                        (videoBytes, audioBytes) =>
                        {
                            // 节流保存字节数用于断点续传
                            var now = DateTime.UtcNow;
                            if ((now - _lastBytesDbWrite) >= DbWriteInterval)
                            {
                                _lastBytesDbWrite = now;
                                _ = _taskStore.UpdateBytesAsync(nextTask.TaskId, videoBytes, audioBytes);
                            }
                        },
                        ct);

                    // 标记完成
                    nextTask.Status = "done";
                    nextTask.Progress = 100;
                    nextTask.VideoProgress = 100;
                    nextTask.AudioProgress = 100;
                    nextTask.MergeProgress = 100;
                    nextTask.SpeedText = "";
                    await _taskStore.UpdateStageProgressAsync(
                        nextTask.TaskId, 100, "done", 100, 100, 100, "");
                    BroadcastProgress(nextTask);
                    BroadcastStatusChanged(nextTask);
                }
                catch (OperationCanceledException)
                {
                    // 用户取消，保持当前状态，下次恢复
                    nextTask.Status = "pending";
                    await _taskStore.UpdateProgressAsync(nextTask.TaskId, nextTask.Progress, "pending");
                    BroadcastProgress(nextTask);
                    break;
                }
                catch (Exception ex)
                {
                    nextTask.Status = "failed";
                    nextTask.ErrorMessage = ex.Message;
                    await _taskStore.UpdateProgressAsync(nextTask.TaskId, nextTask.Progress, "failed", ex.Message);
                    BroadcastProgress(nextTask);
                    BroadcastStatusChanged(nextTask);
                }

                UpdateCounts();
            }
        }
        finally
        {
            _isProcessing = false;
            _processingTask = null;
        }
    }

    /// <summary>
    /// 广播进度消息给对应的 Document
    /// </summary>
    private void BroadcastProgress(DownloadTaskRecord task)
    {
        try
        {
            _messengerService.Send(new DownloadTaskProgressMessage(
                targetDocumentId: task.DocumentId,
                taskId: task.TaskId,
                itemTitle: task.ItemTitle,
                progress: task.Progress,
                status: task.Status,
                errorMessage: task.ErrorMessage,
                videoProgress: task.VideoProgress,
                audioProgress: task.AudioProgress,
                mergeProgress: task.MergeProgress,
                speedText: task.SpeedText));
        }
        catch { /* 忽略广播失败 */ }
    }

    /// <summary>
    /// 广播状态变更事件给对应的 Document（用于调度器自主操作通知）
    /// </summary>
    private void BroadcastStatusChanged(DownloadTaskRecord task)
    {
        try
        {
            _messengerService.Send(new DownloadTaskStatusChangedMessage(
                targetDocumentId: task.DocumentId,
                taskId: task.TaskId,
                newStatus: task.Status,
                progress: task.Progress,
                videoProgress: task.VideoProgress,
                audioProgress: task.AudioProgress,
                mergeProgress: task.MergeProgress,
                speedText: task.SpeedText));
        }
        catch { /* 忽略广播失败 */ }
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
    /// 删除单个任务：下载中的先停止，然后删除记录、清理临时文件、通知 Document
    /// </summary>
    private async Task DeleteTaskAsync(DownloadTaskRecord? task)
    {
        if (task == null) return;

        try
        {
            var isActive = task.Status is "downloading_video" or "downloading_audio" or "merging";

            // 正在下载中：先停止处理队列（等待下载循环完全退出）
            if (isActive)
            {
                await StopProcessingAsync();
            }

            // 从 SQLite 删除
            await _taskStore.DeleteByIdAsync(task.TaskId);

            // 从 UI 集合移除
            Tasks.Remove(task);

            // 通知对应 Document 移除对应项
            try
            {
                _messengerService.Send(new DownloadTaskDeletedMessage(task.DocumentId, task.TaskId));
            }
            catch { /* 忽略广播失败 */ }

            // 清理临时文件目录
            try
            {
                if (!string.IsNullOrWhiteSpace(task.TempDirectory) && Directory.Exists(task.TempDirectory))
                {
                    Directory.Delete(task.TempDirectory, true);
                }
            }
            catch { /* 忽略清理失败 */ }

            UpdateCounts();
            SchedulerStatus = $"已删除任务: {task.ItemTitle}";

            // 如果停止了处理队列，重新启动
            if (isActive)
            {
                StartProcessing();
            }
        }
        catch (Exception ex)
        {
            SchedulerStatus = $"删除任务失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 重试/恢复任务：failed 重置为 pending，interrupted 恢复为 pending（保留已下载进度）
    /// </summary>
    private async Task RetryTaskAsync(DownloadTaskRecord? task)
    {
        if (task == null || (task.Status != "failed" && task.Status != "interrupted")) return;

        try
        {
            var wasFailed = task.Status == "failed";

            // failed 任务重置进度；interrupted 保留已下载字节数用于断点续传
            if (wasFailed)
            {
                task.Progress = 0;
                task.ErrorMessage = null;
                task.VideoBytesDownloaded = 0;
                task.AudioBytesDownloaded = 0;
                await _taskStore.UpdateBytesAsync(task.TaskId, 0, 0);
            }

            task.Status = "pending";

            // 更新 SQLite
            await _taskStore.UpdateProgressAsync(task.TaskId, task.Progress, "pending");

            // 广播状态变更通知 Document
            BroadcastStatusChanged(task);

            SchedulerStatus = $"重试任务: {task.ItemTitle}";

            // 重新启动处理队列
            StartProcessing();
        }
        catch (Exception ex)
        {
            SchedulerStatus = $"重试任务失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 停止处理队列（异步等待处理循环完全退出）
    /// </summary>
    public async Task StopProcessingAsync()
    {
        if (_processingCts != null)
        {
            _processingCts.Cancel();
            if (_processingTask != null)
            {
                try { await _processingTask; } catch (OperationCanceledException) { }
            }
            _processingCts.Dispose();
            _processingCts = null;
            _processingTask = null;
        }
        _isProcessing = false;
        SchedulerStatus = "调度器已停止";
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
                : "ffmpeg 未找到，请将 ffmpeg.exe 放入 D:\\soft\\FFMEPG 目录或手动浏览选择";
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
