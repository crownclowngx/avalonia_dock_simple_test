using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using MyAvaloniaManagementCommon.Message;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 下载任务协调器：负责任务状态机、后台执行队列、进度持久化和生命周期管理。
/// 从 BiliSchedulerToolViewModel 中提取，使下载编排不再依赖 UI 生命周期。
/// </summary>
public sealed class BiliDownloadCoordinator
{
    private static readonly IPluginLogger Log = PluginLog.For<BiliDownloadCoordinator>();

    /// <summary>将存储字符串解析为枚举的快捷方法</summary>
    private static DownloadTaskStatus ParseStatus(string s) => DownloadTaskStatusMapper.FromStorageString(s);

    /// <summary>将枚举转换为存储字符串的快捷方法</summary>
    private static string ToStorage(DownloadTaskStatus s) => DownloadTaskStatusMapper.ToStorageString(s);

    private readonly IDownloadTaskRepository _repository;
    private readonly IMessengerService _messengerService;
    private readonly IDownloadProgressTracker _tracker;
    private readonly IDownloadTaskExecutor _executor;
    private readonly IBiliDataPaths _paths;

    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly object _lifecycleLock = new();
    private Task? _initializationTask;
    private Task? _shutdownTask;
    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;
    private bool _isProcessing;
    private volatile bool _isShuttingDown;

    /// <summary>调度器是否正在处理任务</summary>
    public bool IsProcessing => _isProcessing;
    private SemaphoreSlim _concurrencySemaphore = new(1, 1);
    private readonly List<Task> _activeTasks = new();
    private readonly object _activeTasksLock = new();

    /// <summary>任务进度变更事件（UI 订阅）</summary>
    public event Action<DownloadTaskRecord>? TaskProgressChanged;

    /// <summary>任务状态变更事件（UI 订阅）</summary>
    public event Action<DownloadTaskRecord>? TaskStatusChanged;

    /// <summary>任务列表变更事件（新增/删除后触发）</summary>
    public event Action? TaskListChanged;

    /// <summary>调度器状态文本变更事件</summary>
    public event Action<string>? SchedulerStatusChanged;

    /// <summary>调度器处理状态变更事件（true=正在处理，false=已停止）</summary>
    public event Action<bool>? IsProcessingChanged;

    public BiliDownloadCoordinator(
        IDownloadTaskRepository repository,
        IMessengerService messengerService,
        IDownloadProgressTracker tracker,
        IDownloadTaskExecutor executor,
        IBiliDataPaths paths)
    {
        _repository = repository;
        _messengerService = messengerService;
        _tracker = tracker;
        _executor = executor;
        _paths = paths;

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
        try
        {
            await SubmitTasksAsync(msg, msg.OutputDirectory);
        }
        catch (Exception ex)
        {
            // 消息总线回调无法把异常返回给发送方，因此必须在边界处观察并记录，
            // 避免 fire-and-forget 任务产生未观察异常。
            Log.Error($"接收 Document 提交任务失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 初始化任务仓储并迁移异常退出前的运行状态。
    /// 初始化任务由独立的生命周期锁保护，不复用命令锁，避免提交命令在持锁期间
    /// 再次进入 InitializeAsync 形成异步重入死锁。
    /// </summary>
    public Task InitializeAsync()
    {
        lock (_lifecycleLock)
        {
            return _initializationTask ??= InitializeCoreAsync();
        }
    }

    private async Task InitializeCoreAsync()
    {
        await _repository.InitAsync();

        // 启动阶段只迁移本地事实，不启动队列，也不调用下载执行器。
        var allTasks = await _repository.GetAllAsync();
        foreach (var task in allTasks)
        {
            if (DownloadTaskStatusMapper.IsRunning(ParseStatus(task.Status)))
            {
                task.Status = ToStorage(DownloadTaskStatus.Interrupted);
                await _repository.UpdateProgressAsync(
                    task.TaskId,
                    task.Progress,
                    ToStorage(DownloadTaskStatus.Interrupted));
            }
        }

        SchedulerStatusChanged?.Invoke("协调器已初始化");
    }

    /// <summary>
    /// 接收新任务：批量插入 SQLite 并启动处理队列
    /// </summary>
    public async Task SubmitTasksAsync(SubmitDownloadTaskMessage msg, string defaultOutputDirectory)
    {
        await InitializeAsync();
        await _commandLock.WaitAsync();
        try
        {
            ThrowIfShuttingDown();

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
                    Status = ToStorage(DownloadTaskStatus.Ready),
                    CreatedAt = DateTime.Now,
                    LastUpdatedAt = DateTime.Now,
                    MediaType = item.MediaType.ToString().ToLowerInvariant(),
                    EpId = item.EpId,
                    SeasonId = item.SeasonId,
                    ExtrasConfig = (int)msg.ExtrasConfig,
                    CoverUrl = item.CoverUrl,
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
        ThrowIfShuttingDown();
        StartProcessingInternal();
    }

    /// <summary>
    /// 设置并发下载数（1-5），重建 SemaphoreSlim 以应用新的并发限制
    /// </summary>
    public void SetMaxConcurrentDownloads(int max)
    {
        var clamped = Math.Clamp(max, 1, 5);
        _concurrencySemaphore = new SemaphoreSlim(clamped, clamped);
        SchedulerStatusChanged?.Invoke($"并发下载数已设置为: {clamped}");
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
            var isActive = DownloadTaskStatusMapper.IsRunning(ParseStatus(task.Status));

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
        var statusEnum = ParseStatus(task.Status);
        if (statusEnum != DownloadTaskStatus.Failed && statusEnum != DownloadTaskStatus.Interrupted) return;

        await _commandLock.WaitAsync();
        try
        {
            var wasFailed = statusEnum == DownloadTaskStatus.Failed;

            if (wasFailed)
            {
                task.Progress = 0;
                task.ErrorMessage = null;
                task.VideoBytesDownloaded = 0;
                task.AudioBytesDownloaded = 0;
                await _repository.UpdateBytesAsync(task.TaskId, 0, 0);
            }

            task.Status = ToStorage(DownloadTaskStatus.Ready);
            await _repository.UpdateProgressAsync(task.TaskId, task.Progress, ToStorage(DownloadTaskStatus.Ready));

            _tracker.BroadcastStatusChanged(task);
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
    public Task ShutdownAsync()
    {
        lock (_lifecycleLock)
        {
            return _shutdownTask ??= ShutdownCoreAsync();
        }
    }

    private async Task ShutdownCoreAsync()
    {
        _isShuttingDown = true;
        await _commandLock.WaitAsync();
        try
        {
            if (_processingCts != null)
            {
                _processingCts.Cancel();
            }

            var processingTask = _processingTask;
            if (processingTask != null)
            {
                try
                {
                    await processingTask;
                }
                catch (OperationCanceledException)
                {
                    // 关闭流程主动取消队列属于预期结果。
                }
            }

            Task[] activeTasks;
            lock (_activeTasksLock)
            {
                activeTasks = _activeTasks.ToArray();
            }

            if (activeTasks.Length > 0)
            {
                try
                {
                    await Task.WhenAll(activeTasks);
                }
                catch (OperationCanceledException)
                {
                    // 活动任务确认收到关闭取消后会把状态持久化为 Interrupted。
                }
            }

            _processingCts?.Dispose();
            _processingCts = null;
            _processingTask = null;
            _isProcessing = false;

            // Coordinator 注册在共享消息总线上；进程关闭前主动注销，避免继续接收提交消息。
            _messengerService.UnregisterAll(this);

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
        if (_isProcessing || _isShuttingDown) return;
        _isProcessing = true;
        IsProcessingChanged?.Invoke(true);
        _processingCts = new CancellationTokenSource();
        _processingTask = ProcessQueueAsync(_processingCts.Token);
    }

    private async Task StopProcessingInternalAsync()
    {
        if (_processingCts != null)
        {
            _processingCts.Cancel();
            var processingTask = _processingTask;
            if (processingTask != null)
            {
                try { await processingTask; } catch (OperationCanceledException) { }
            }
            _processingCts.Dispose();
            _processingCts = null;
            _processingTask = null;
        }
        _isProcessing = false;
        IsProcessingChanged?.Invoke(false);
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 清理已完成的任务引用
                lock (_activeTasksLock)
                {
                    _activeTasks.RemoveAll(t => t.IsCompleted);
                }

                // 查找所有待处理任务
                var allTasks = await _repository.GetAllAsync();
                var readyTasks = allTasks.Where(t => ParseStatus(t.Status) == DownloadTaskStatus.Ready).ToList();

                // 为每个 Ready 任务分配并发槽位
                foreach (var task in readyTasks)
                {
                    await _concurrencySemaphore.WaitAsync(ct);

                    // 确保临时目录
                    if (string.IsNullOrWhiteSpace(task.TempDirectory))
                    {
                        task.TempDirectory = Path.Combine(_paths.TempDirectory, task.TaskId);
                        await _repository.UpdateTempDirectoryAsync(task.TaskId, task.TempDirectory);
                    }

                    SchedulerStatusChanged?.Invoke($"正在下载: {task.ItemTitle}");
                    _tracker.BroadcastStatusChanged(task);

                    var downloadTask = ProcessSingleTaskAsync(task, ct);
                    lock (_activeTasksLock)
                    {
                        _activeTasks.Add(downloadTask);
                    }
                }

                // 等待任意一个任务完成后重新检查队列
                Task<Task>? completedTask = null;
                lock (_activeTasksLock)
                {
                    if (_activeTasks.Count > 0)
                        completedTask = Task.WhenAny(_activeTasks);
                }

                if (completedTask != null)
                {
                    var finishedTask = await completedTask;
                    await finishedTask;
                }
                else if (readyTasks.Count == 0)
                {
                    // 没有 Ready 任务也没有活跃任务
                    SchedulerStatusChanged?.Invoke("所有任务已完成");
                    _isProcessing = false;
                    return;
                }
            }
        }
        finally
        {
            _isProcessing = false;
            IsProcessingChanged?.Invoke(false);
            _processingTask = null;
        }
    }

    /// <summary>
    /// 处理单个下载任务（并发执行），完成后释放信号量
    /// </summary>
    private async Task ProcessSingleTaskAsync(DownloadTaskRecord task, CancellationToken ct)
    {
        try
        {
            var result = await _executor.ExecuteAsync(
                task,
                (info) =>
                {
                    _tracker.OnProgressChanged(task, info);
                    TaskProgressChanged?.Invoke(task);
                },
                (videoBytes, audioBytes) =>
                {
                    _tracker.OnBytesChanged(task, videoBytes, audioBytes);
                },
                ct);

            if (!string.IsNullOrWhiteSpace(result.ExtrasResultSummary))
            {
                task.ExtrasResultSummary = result.ExtrasResultSummary;
                await _repository.UpdateExtrasResultAsync(task.TaskId, result.ExtrasResultSummary);
            }

            // 标记完成
            task.Status = ToStorage(DownloadTaskStatus.Completed);
            task.Progress = 100;
            task.VideoProgress = 100;
            task.AudioProgress = 100;
            task.MergeProgress = 100;
            task.SpeedText = "";
            task.LastUpdatedAt = DateTime.Now;
            await _repository.UpdateStageProgressAsync(
                task.TaskId, 100, ToStorage(DownloadTaskStatus.Completed), 100, 100, 100, "");
            _tracker.BroadcastProgress(task);
            _tracker.BroadcastStatusChanged(task);
            TaskStatusChanged?.Invoke(task);
        }
        catch (OperationCanceledException)
        {
            // 宿主退出与用户停止具有不同语义：宿主退出后的任务必须明确显示为已中断，
            // 下次启动只能由用户手动恢复；普通停止暂时维持现有 Ready 语义，G2 再细化。
            var cancelledStatus = _isShuttingDown
                ? DownloadTaskStatus.Interrupted
                : DownloadTaskStatus.Ready;
            task.Status = ToStorage(cancelledStatus);
            await _repository.UpdateProgressAsync(task.TaskId, task.Progress, task.Status);
            _tracker.BroadcastProgress(task);
        }
        catch (Exception ex)
        {
            var safeError = SensitiveDataSanitizer.Sanitize(ex.Message);
            task.Status = ToStorage(DownloadTaskStatus.Failed);
            task.ErrorMessage = safeError;
            task.LastUpdatedAt = DateTime.Now;
            Log.Error($"任务 {task.TaskId} 下载失败: {safeError}", ex);
            await _repository.UpdateProgressAsync(
                task.TaskId,
                task.Progress,
                ToStorage(DownloadTaskStatus.Failed),
                safeError);
            _tracker.BroadcastProgress(task);
            _tracker.BroadcastStatusChanged(task);
            TaskStatusChanged?.Invoke(task);
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private void ThrowIfShuttingDown()
    {
        if (_isShuttingDown)
        {
            throw new InvalidOperationException("协调器正在关闭，不能接受新的执行命令。");
        }
    }

    #endregion
}
