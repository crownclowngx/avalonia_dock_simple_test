using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using MyAvaloniaManagementCommon.Message;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 下载任务协调器：负责任务状态机、后台执行队列、进度持久化和生命周期管理。
/// 从 BiliSchedulerToolViewModel 中提取，使下载编排不再依赖 UI 生命周期。
/// </summary>
public class BiliDownloadCoordinator
{
    private static readonly IPluginLogger Log = PluginLog.For<BiliDownloadCoordinator>();

    #region 懒单例

    private static readonly Lazy<BiliDownloadCoordinator> _instance = new(CreateInstance);
    public static BiliDownloadCoordinator Instance => _instance.Value;

    private static BiliDownloadCoordinator CreateInstance()
    {
        return new BiliDownloadCoordinator(
            new DownloadTaskStore(),
            new BiliCredentialProvider(),
            new MessengerService());
    }

    #endregion

    private readonly IDownloadTaskRepository _repository;
    private readonly IBiliCredentialProvider _credentialProvider;
    private readonly IMessengerService _messengerService;
    private readonly BiliApiService _apiService = new();
    private readonly BiliDownloadService _downloadService = new();

    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;
    private bool _isProcessing;
    private bool _initialized;

    // SQLite 进度写入节流
    private DateTime _lastProgressDbWrite = DateTime.MinValue;
    private DateTime _lastBytesDbWrite = DateTime.MinValue;
    private static readonly TimeSpan DbWriteInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>任务进度变更事件（UI 订阅）</summary>
    public event Action<DownloadTaskRecord>? TaskProgressChanged;

    /// <summary>任务状态变更事件（UI 订阅）</summary>
    public event Action<DownloadTaskRecord>? TaskStatusChanged;

    /// <summary>任务列表变更事件（新增/删除后触发）</summary>
    public event Action? TaskListChanged;

    /// <summary>调度器状态文本变更事件</summary>
    public event Action<string>? SchedulerStatusChanged;

    public BiliDownloadCoordinator(
        IDownloadTaskRepository repository,
        IBiliCredentialProvider credentialProvider,
        IMessengerService messengerService)
    {
        _repository = repository;
        _credentialProvider = credentialProvider;
        _messengerService = messengerService;

        // 注册监听：接收 Document 发来的下载任务提交消息
        _messengerService.Register<BiliDownloadCoordinator, SubmitDownloadTaskMessage>(
            this, (coordinator, msg) =>
            {
                _ = coordinator.HandleSubmitMessageAsync(msg);
            });
    }

    /// <summary>
    /// 处理 Document 提交的下载任务消息（由消息总线回调触发）
    /// </summary>
    private async Task HandleSubmitMessageAsync(SubmitDownloadTaskMessage msg)
    {
        await SubmitTasksAsync(msg, msg.OutputDirectory);
    }

    /// <summary>
    /// 初始化：建表 + 迁移 Interrupted 状态 + 检测 ffmpeg（不启动下载）
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await _commandLock.WaitAsync();
        try
        {
            if (_initialized) return;

            await _repository.InitAsync();

            // 将异常退出前仍在运行的任务标记为"已中断"
            var allTasks = await _repository.GetAllAsync();
            foreach (var t in allTasks)
            {
                if (t.Status is "downloading_video" or "downloading_audio" or "merging" or
                    "fetching_metadata")
                {
                    t.Status = "interrupted";
                    await _repository.UpdateProgressAsync(t.TaskId, t.Progress, "interrupted");
                }
            }

            _initialized = true;
            SchedulerStatusChanged?.Invoke("协调器已初始化");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// 接收新任务：批量插入 SQLite 并启动处理队列
    /// </summary>
    public async Task SubmitTasksAsync(SubmitDownloadTaskMessage msg, string defaultOutputDirectory)
    {
        await _commandLock.WaitAsync();
        try
        {
            await InitializeAsync();

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
#pragma warning disable CS0618 // Cookie 已标记为 Obsolete，过渡期仍使用 msg.Cookie 供下载服务使用
                    Cookie = _credentialProvider.IsLoggedIn ? _credentialProvider.GetCookieHeader() : msg.Cookie,
#pragma warning restore CS0618
                    Status = "pending",
                    CreatedAt = DateTime.Now,
                    LastUpdatedAt = DateTime.Now,
                };
                records.Add(record);
            }

            await _repository.InsertBatchAsync(records);
            TaskListChanged?.Invoke();
            SchedulerStatusChanged?.Invoke($"已接收 {records.Count} 个新任务");

            // 启动后台处理
            StartProcessingInternal();
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// 加载所有任务（供 Tool ViewModel 初始化 UI）
    /// </summary>
    public async Task<List<DownloadTaskRecord>> LoadAllTasksAsync()
    {
        await InitializeAsync();
        return await _repository.GetAllAsync();
    }

    /// <summary>
    /// 启动处理队列（幂等）
    /// </summary>
    public void StartProcessingAsync()
    {
        StartProcessingInternal();
    }

    /// <summary>
    /// 停止处理队列（异步等待处理循环完全退出）
    /// </summary>
    public async Task StopProcessingAsync()
    {
        await _commandLock.WaitAsync();
        try
        {
            await StopProcessingInternalAsync();
            SchedulerStatusChanged?.Invoke("调度器已停止");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// 删除任务：先停止→等待退出→删记录→清文件
    /// </summary>
    public async Task DeleteTaskAsync(DownloadTaskRecord task)
    {
        await _commandLock.WaitAsync();
        try
        {
            var isActive = task.Status is "downloading_video" or "downloading_audio" or "merging" or
                "fetching_metadata";

            if (isActive)
            {
                await StopProcessingInternalAsync();
            }

            await _repository.DeleteByIdAsync(task.TaskId);

            // 通知对应 Document 移除
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

            TaskListChanged?.Invoke();
            SchedulerStatusChanged?.Invoke($"已删除任务: {task.ItemTitle}");

            // 如果停止了处理队列，重新启动
            if (isActive)
            {
                StartProcessingInternal();
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// 重试/恢复任务
    /// </summary>
    public async Task RetryTaskAsync(DownloadTaskRecord task)
    {
        if (task.Status != "failed" && task.Status != "interrupted") return;

        await _commandLock.WaitAsync();
        try
        {
            var wasFailed = task.Status == "failed";

            if (wasFailed)
            {
                task.Progress = 0;
                task.ErrorMessage = null;
                task.VideoBytesDownloaded = 0;
                task.AudioBytesDownloaded = 0;
                await _repository.UpdateBytesAsync(task.TaskId, 0, 0);
            }

            task.Status = "pending";
            await _repository.UpdateProgressAsync(task.TaskId, task.Progress, "pending");

            BroadcastStatusChanged(task);
            SchedulerStatusChanged?.Invoke($"重试任务: {task.ItemTitle}");

            StartProcessingInternal();
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// 有序关闭：取消当前任务 + 等待退出 + Flush 最终进度
    /// </summary>
    public async Task ShutdownAsync()
    {
        await _commandLock.WaitAsync();
        try
        {
            if (_processingCts != null && _processingTask != null)
            {
                _processingCts.Cancel();
                try { await _processingTask; } catch (OperationCanceledException) { }
            }
            SchedulerStatusChanged?.Invoke("协调器已关闭");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    #region 内部方法

    private void StartProcessingInternal()
    {
        if (_isProcessing) return;
        _isProcessing = true;
        _processingCts = new CancellationTokenSource();
        _processingTask = ProcessQueueAsync(_processingCts.Token);
    }

    private async Task StopProcessingInternalAsync()
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
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 查找下一个待处理任务（从内存列表或数据库重新加载）
                var allTasks = await _repository.GetAllAsync();
                var nextTask = allTasks.FirstOrDefault(t =>
                    t.Status is "pending" or "downloading_video" or "downloading_audio" or "merging");

                if (nextTask == null)
                {
                    SchedulerStatusChanged?.Invoke("所有任务已完成");
                    _isProcessing = false;
                    return;
                }

                // 确保临时目录
                if (string.IsNullOrWhiteSpace(nextTask.TempDirectory))
                {
                    var tempBase = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "BiliDownloader", "temp");
                    nextTask.TempDirectory = Path.Combine(tempBase, nextTask.TaskId);
                    await _repository.UpdateTempDirectoryAsync(nextTask.TaskId, nextTask.TempDirectory);
                }

                SchedulerStatusChanged?.Invoke($"正在下载: {nextTask.ItemTitle}");
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

                            // 节流写 SQLite
                            var now = DateTime.UtcNow;
                            var isCriticalState = info.Stage is "done" or "merging";
                            if (isCriticalState || (now - _lastProgressDbWrite) >= DbWriteInterval)
                            {
                                _lastProgressDbWrite = now;
                                _ = _repository.UpdateStageProgressAsync(
                                    nextTask.TaskId, nextTask.Progress, nextTask.Status,
                                    nextTask.VideoProgress, nextTask.AudioProgress,
                                    nextTask.MergeProgress, nextTask.SpeedText);
                            }

                            // UI 通知不受节流影响
                            BroadcastProgress(nextTask);
                            TaskProgressChanged?.Invoke(nextTask);
                        },
                        (videoBytes, audioBytes) =>
                        {
                            var now = DateTime.UtcNow;
                            if ((now - _lastBytesDbWrite) >= DbWriteInterval)
                            {
                                _lastBytesDbWrite = now;
                                _ = _repository.UpdateBytesAsync(nextTask.TaskId, videoBytes, audioBytes);
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
                    nextTask.LastUpdatedAt = DateTime.Now;
                    await _repository.UpdateStageProgressAsync(
                        nextTask.TaskId, 100, "done", 100, 100, 100, "");
                    BroadcastProgress(nextTask);
                    BroadcastStatusChanged(nextTask);
                    TaskStatusChanged?.Invoke(nextTask);
                }
                catch (OperationCanceledException)
                {
                    nextTask.Status = "pending";
                    await _repository.UpdateProgressAsync(nextTask.TaskId, nextTask.Progress, "pending");
                    BroadcastProgress(nextTask);
                    break;
                }
                catch (Exception ex)
                {
                    nextTask.Status = "failed";
                    nextTask.ErrorMessage = ex.Message;
                    nextTask.LastUpdatedAt = DateTime.Now;
                    Log.Error($"任务 {nextTask.TaskId} 下载失败: {ex.Message}", ex);
                    await _repository.UpdateProgressAsync(nextTask.TaskId, nextTask.Progress, "failed", ex.Message);
                    BroadcastProgress(nextTask);
                    BroadcastStatusChanged(nextTask);
                    TaskStatusChanged?.Invoke(nextTask);
                }
            }
        }
        finally
        {
            _isProcessing = false;
            _processingTask = null;
        }
    }

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

    #endregion
}
