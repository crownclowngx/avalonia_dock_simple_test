using System.Collections.ObjectModel;
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
    private bool _isProcessing;
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

    /// <summary>
    /// 所有任务记录（UI 绑定）
    /// </summary>
    public ObservableCollection<DownloadTaskRecord> Tasks { get; } = new();

    public IRelayCommand ClearDoneCommand { get; }
    public IAsyncRelayCommand DownloadFfmpegCommand { get; }
    public IAsyncRelayCommand BrowseFfmpegCommand { get; }

    public BiliSchedulerToolViewModel()
    {
        _messengerService = new MessengerService();
        _taskStore = new DownloadTaskStore();

        ClearDoneCommand = new RelayCommand(ClearDoneTasks);
        DownloadFfmpegCommand = new AsyncRelayCommand(DownloadFfmpegAsync);
        BrowseFfmpegCommand = new AsyncRelayCommand(BrowseFfmpegAsync);

        // 注册监听：接收 Document 发来的下载任务
        _messengerService.Register<BiliSchedulerToolViewModel, SubmitDownloadTaskMessage>(
            this, (vm, msg) =>
            {
                _ = vm.HandleSubmitMessageAsync(msg);
            });
    }

    /// <summary>
    /// 初始化：建表 + 加载未完成任务 + 自动恢复下载
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            await _taskStore.InitAsync();

            // 检测 ffmpeg
            await CheckFfmpegAsync();

            SchedulerStatus = "调度器已初始化";

            // 加载所有任务到 UI
            var allTasks = await _taskStore.GetAllAsync();
            Tasks.Clear();
            foreach (var t in allTasks)
                Tasks.Add(t);

            UpdateCounts();

            // 自动恢复未完成的任务
            var incomplete = allTasks.Where(t =>
                t.Status is "pending" or "downloading_video" or "downloading_audio" or "merging")
                .ToList();

            if (incomplete.Count > 0)
            {
                SchedulerStatus = $"恢复 {incomplete.Count} 个未完成任务...";
                StartProcessing();
            }

            // 若 ffmpeg 未就绪，自动尝试下载
            if (!FfmpegReady)
            {
                _ = DownloadFfmpegAsync();
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
                    OutputDirectory = msg.OutputDirectory,
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
        _ = ProcessQueueAsync(_processingCts.Token);
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

                try
                {
                    await _downloadService.DownloadItemAsync(
                        nextTask,
                        _apiService,
                        (progress, statusText) =>
                        {
                            nextTask.Progress = progress;
                            nextTask.Status = statusText switch
                            {
                                var s when s.Contains("视频") => "downloading_video",
                                var s when s.Contains("音频") => "downloading_audio",
                                var s when s.Contains("合并") => "merging",
                                var s when s.Contains("完成") => "done",
                                _ => nextTask.Status,
                            };

                            // 写 SQLite + 发消息
                            _ = _taskStore.UpdateProgressAsync(nextTask.TaskId, progress, nextTask.Status);
                            BroadcastProgress(nextTask);
                            UpdateCounts();
                        },
                        (videoBytes, audioBytes) =>
                        {
                            // 定期保存字节数用于断点续传
                            _ = _taskStore.UpdateBytesAsync(nextTask.TaskId, videoBytes, audioBytes);
                        },
                        ct);

                    // 标记完成
                    nextTask.Status = "done";
                    nextTask.Progress = 100;
                    await _taskStore.UpdateProgressAsync(nextTask.TaskId, 100, "done");
                    BroadcastProgress(nextTask);
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
                }

                UpdateCounts();
            }
        }
        finally
        {
            _isProcessing = false;
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
                errorMessage: task.ErrorMessage));
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
    /// 停止处理队列
    /// </summary>
    public void StopProcessing()
    {
        _processingCts?.Cancel();
        _processingCts = null;
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
                : "ffmpeg 未找到，将自动下载...";
        });
    }

    /// <summary>
    /// 手动触发 ffmpeg 下载
    /// </summary>
    private async Task DownloadFfmpegAsync()
    {
        try
        {
            FfmpegStatus = "准备下载 ffmpeg...";
            await FfmpegService.EnsureDownloadedAsync(
                status => FfmpegStatus = status);

            await CheckFfmpegAsync();
        }
        catch (Exception ex)
        {
            FfmpegStatus = $"ffmpeg 下载失败: {ex.Message}";
            FfmpegReady = false;
        }
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
}
