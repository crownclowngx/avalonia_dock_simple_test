using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using MyAvaloniaManagementCommon.Message;
using System.Threading.Channels;

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
    private readonly IBiliCredentialProvider _credentialProvider;

    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly object _lifecycleLock = new();
    private readonly object _schedulerLock = new();
    private readonly Channel<bool> _queueWakeups = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
    private Task? _initializationTask;
    private Task? _shutdownTask;
    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;
    private volatile bool _isProcessing;
    private volatile bool _isShuttingDown;

    /// <summary>调度器是否正在处理任务</summary>
    public bool IsProcessing => _isProcessing;
    private int _maxConcurrentDownloads = 1;
    private readonly Dictionary<string, Task> _activeTasks = new(StringComparer.Ordinal);

    /// <summary>活动任务的运行时上下文（per-task CTS + 暂停门控）</summary>
    private readonly Dictionary<string, TaskRuntimeContext> _activeContexts = new(StringComparer.Ordinal);

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
        IBiliDataPaths paths,
        IBiliCredentialProvider? credentialProvider = null)
    {
        _repository = repository;
        _messengerService = messengerService;
        _tracker = tracker;
        _executor = executor;
        _paths = paths;
        _credentialProvider = credentialProvider ?? new NullCredentialProvider();

        // 注册监听：接收 Document 发来的下载任务提交消息
        _messengerService.Register<BiliDownloadCoordinator, SubmitDownloadTaskMessage>(
            this, (coordinator, msg) =>
            {
                _ = coordinator.HandleSubmitMessageAsync(msg);
            });

        // G2: 监听登录状态变更，登录成功时自动恢复 WaitingForLogin 任务
        _messengerService.Register<BiliDownloadCoordinator, LoginStateChangedMessage>(
            this, (coordinator, msg) =>
            {
                _ = coordinator.HandleLoginStateChangedAsync(msg);
            });
    }

    /// <summary>未注入凭据提供者时的空实现（向后兼容旧构造调用）</summary>
    private sealed class NullCredentialProvider : IBiliCredentialProvider
    {
        public string GetCookieHeader() => string.Empty;
        public bool IsLoggedIn => true;
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
    /// 设置并发下载数（1-5）。
    /// G2: 降低并发数时，暂停最近启动的超额任务（保留断点），而非暴力取消。
    /// </summary>
    public void SetMaxConcurrentDownloads(int max)
    {
        var clamped = Math.Clamp(max, 1, 5);
        int previousMax;
        lock (_schedulerLock)
        {
            previousMax = _maxConcurrentDownloads;
            _maxConcurrentDownloads = clamped;
        }

        // G2: 下调时暂停超额任务
        if (clamped < previousMax)
        {
            _ = GracefulScaleDownAsync(clamped);
        }
        else
        {
            SignalQueueChanged();
        }
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
    /// 删除任务。
    /// G2: 活动任务使用 per-task CTS 取消，不影响其他并发任务。
    /// </summary>
    public async Task DeleteTaskAsync(DownloadTaskRecord task)
    {
        await _commandLock.WaitAsync();
        try
        {
            // G2: 活动任务通过 per-task CTS 取消，不再停止全部队列
            TaskRuntimeContext? ctx;
            Task? activeTask;
            lock (_schedulerLock)
            {
                _activeContexts.TryGetValue(task.TaskId, out ctx);
                _activeTasks.TryGetValue(task.TaskId, out activeTask);
            }

            if (ctx != null)
            {
                ctx.RequestCancellation();
                if (activeTask != null)
                {
                    try { await activeTask; } catch (OperationCanceledException) { }
                }
            }

            await _repository.DeleteByIdAsync(task.TaskId);

            // 通知对应 Document 移除
            try
            {
                _messengerService.Send(new DownloadTaskDeletedMessage(task.DocumentId, task.TaskId));
            }
            catch { /* 忽略广播失败 */ }

            // 清理临时文件目录
            CleanupTempFiles(task.TempDirectory);

            // 清理成品文件
            try
            {
                if (!string.IsNullOrWhiteSpace(task.OutputFilePath) && File.Exists(task.OutputFilePath))
                {
                    File.Delete(task.OutputFilePath);
                }
            }
            catch { /* 忽略清理失败 */ }

            TaskListChanged?.Invoke();
            SchedulerStatusChanged?.Invoke($"已删除任务: {task.ItemTitle}");
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
            lock (_schedulerLock)
            {
                activeTasks = _activeTasks.Values.ToArray();
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
        var started = false;
        TaskCompletionSource? startGate = null;
        lock (_schedulerLock)
        {
            if (_isShuttingDown)
            {
                return;
            }

            SignalQueueChanged();
            if (_isProcessing)
            {
                return;
            }

            _isProcessing = true;
            _processingCts?.Dispose();
            _processingCts = new CancellationTokenSource();
            startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _processingTask = RunProcessQueueAfterStartSignalAsync(
                startGate.Task,
                _processingCts.Token);
            started = true;
        }

        if (started)
        {
            IsProcessingChanged?.Invoke(true);
            startGate!.TrySetResult();
        }
    }

    private async Task RunProcessQueueAfterStartSignalAsync(
        Task startSignal,
        CancellationToken cancellationToken)
    {
        await startSignal;
        await ProcessQueueAsync(cancellationToken);
    }

    private async Task StopProcessingInternalAsync()
    {
        Task? processingTask;
        lock (_schedulerLock)
        {
            _processingCts?.Cancel();
            SignalQueueChanged();
            processingTask = _processingTask;
        }

        if (processingTask != null)
        {
            try { await processingTask; } catch (OperationCanceledException) { }
        }

        Task[] activeTasks;
        lock (_schedulerLock)
        {
            activeTasks = _activeTasks.Values.ToArray();
        }
        if (activeTasks.Length > 0)
        {
            try { await Task.WhenAll(activeTasks); } catch (OperationCanceledException) { }
        }

        bool notifyStopped;
        lock (_schedulerLock)
        {
            notifyStopped = _isProcessing;
            _processingCts?.Dispose();
            _processingCts = null;
            _processingTask = null;
            _activeTasks.Clear();
            // G2: 清理所有 per-task 上下文
            foreach (var ctx in _activeContexts.Values) ctx.Dispose();
            _activeContexts.Clear();
            _isProcessing = false;
        }
        if (notifyStopped)
        {
            IsProcessingChanged?.Invoke(false);
        }
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string[] completedIds;
                lock (_schedulerLock)
                {
                    completedIds = _activeTasks
                        .Where(pair => pair.Value.IsCompleted)
                        .Select(pair => pair.Key)
                        .ToArray();
                    foreach (var taskId in completedIds)
                    {
                        _activeTasks.Remove(taskId);
                        // G2: 同步清理已完成的 per-task 上下文
                        if (_activeContexts.TryGetValue(taskId, out var ctx))
                        {
                            _activeContexts.Remove(taskId);
                            ctx.Dispose();
                        }
                    }
                }

                // 查找所有待处理任务
                var allTasks = await _repository.GetAllAsync();
                List<DownloadTaskRecord> readyTasks;
                int availableSlots;
                lock (_schedulerLock)
                {
                    readyTasks = allTasks
                        .Where(task =>
                            ParseStatus(task.Status) == DownloadTaskStatus.Ready
                            && !_activeTasks.ContainsKey(task.TaskId))
                        .ToList();
                    availableSlots = Math.Max(0, _maxConcurrentDownloads - _activeTasks.Count);
                }

                // 处理循环拥有唯一调度权，只填充当前可用槽位，不阻塞在信号量上。
                foreach (var task in readyTasks.Take(availableSlots))
                {
                    // 确保临时目录
                    if (string.IsNullOrWhiteSpace(task.TempDirectory))
                    {
                        task.TempDirectory = Path.Combine(_paths.TempDirectory, task.TaskId);
                        await _repository.UpdateTempDirectoryAsync(task.TaskId, task.TempDirectory);
                    }

                    SchedulerStatusChanged?.Invoke($"正在下载: {task.ItemTitle}");
                    _tracker.BroadcastStatusChanged(task);

                    // G2: 为每个任务创建独立的运行时上下文（链接全局取消）
                    var context = TaskRuntimeContext.CreateLinked(task.TaskId, ct);
                    lock (_schedulerLock)
                    {
                        _activeContexts[task.TaskId] = context;
                    }

                    var downloadTask = ProcessSingleTaskAsync(task, context);
                    lock (_schedulerLock)
                    {
                        _activeTasks[task.TaskId] = downloadTask;
                    }
                }

                Task[] activeTasks;
                lock (_schedulerLock)
                {
                    activeTasks = _activeTasks.Values.ToArray();
                }

                if (activeTasks.Length > 0)
                {
                    var activeCompletion = Task.WhenAny(activeTasks);
                    var wakeup = _queueWakeups.Reader.WaitToReadAsync(ct).AsTask();
                    var winner = await Task.WhenAny(activeCompletion, wakeup);
                    if (winner == wakeup)
                    {
                        await wakeup;
                        DrainQueueWakeups();
                    }
                    else
                    {
                        await await activeCompletion;
                    }
                }
                else
                {
                    // 与 StartProcessingInternal 使用同一把锁，确保“准备退出”与新唤醒不会交错丢失。
                    var shouldContinue = false;
                    lock (_schedulerLock)
                    {
                        if (_queueWakeups.Reader.TryRead(out _))
                        {
                            shouldContinue = true;
                        }
                        else
                        {
                            _isProcessing = false;
                            _processingTask = null;
                        }
                    }

                    if (shouldContinue)
                    {
                        DrainQueueWakeups();
                        continue;
                    }

                    SchedulerStatusChanged?.Invoke("所有任务已完成");
                    IsProcessingChanged?.Invoke(false);
                    return;
                }
            }
        }
        finally
        {
            var changed = false;
            lock (_schedulerLock)
            {
                changed = _isProcessing;
                _isProcessing = false;
                _processingTask = null;
            }
            if (changed)
            {
                IsProcessingChanged?.Invoke(false);
            }
        }
    }

    /// <summary>
    /// 处理单个下载任务（并发执行）。
    /// G2: 使用 per-task 上下文替代全局 CancellationToken，支持暂停/取消/关闭三种语义区分。
    /// </summary>
    private async Task ProcessSingleTaskAsync(DownloadTaskRecord task, TaskRuntimeContext context)
    {
        try
        {
            // G2: 执行前检查登录态 → 无登录则进入 WaitingForLogin
            if (!_credentialProvider.IsLoggedIn)
            {
                task.Status = ToStorage(DownloadTaskStatus.WaitingForLogin);
                await _repository.UpdateProgressAsync(task.TaskId, task.Progress, task.Status);
                _tracker.BroadcastStatusChanged(task);
                TaskStatusChanged?.Invoke(task);
                return;
            }

            // G2: 阶段边界暂停检查（如果已暂停则阻塞直到恢复或取消）
            context.WaitIfPaused();

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
                context.Token);  // G2: 使用 per-task token 替代全局 ct

            if (!string.IsNullOrWhiteSpace(result.ExtrasResultSummary))
            {
                task.ExtrasResultSummary = result.ExtrasResultSummary;
                await _repository.UpdateExtrasResultAsync(task.TaskId, result.ExtrasResultSummary);
            }

            if (result.VideoTransfer is not null)
            {
                task.ExpectedVideoBytes = result.VideoTransfer.ExpectedBytes;
                task.VideoIntegrityPassed = result.VideoTransfer.IntegrityPassed;
            }
            if (result.AudioTransfer is not null)
            {
                task.ExpectedAudioBytes = result.AudioTransfer.ExpectedBytes;
                task.AudioIntegrityPassed = result.AudioTransfer.IntegrityPassed;
            }
            if (result.VideoTransfer is not null || result.AudioTransfer is not null)
            {
                task.LastUpdatedAt = DateTime.Now;
                await _repository.UpdateIntegrityAsync(
                    task.TaskId,
                    task.ExpectedVideoBytes,
                    task.ExpectedAudioBytes,
                    task.VideoIntegrityPassed,
                    task.AudioIntegrityPassed,
                    task.LastUpdatedAt);
            }

            // 标记完成
            task.Status = ToStorage(DownloadTaskStatus.Completed);
            task.Progress = 100;
            task.VideoProgress = 100;
            task.AudioProgress = 100;
            task.MergeProgress = 100;
            task.SpeedText = "";
            task.OutputFilePath = result.OutputFilePath ?? "";
            task.LastUpdatedAt = DateTime.Now;
            await _repository.MarkCompletedAsync(
                task.TaskId,
                task.OutputFilePath,
                task.ExtrasResultSummary,
                task.LastUpdatedAt);
            _tracker.BroadcastProgress(task);
            _tracker.BroadcastStatusChanged(task);
            TaskStatusChanged?.Invoke(task);
        }
        catch (OperationCanceledException)
        {
            // G2: 区分暂停/取消/关闭/全局停止四种语义
            DownloadTaskStatus cancelledStatus;
            if (context.IsPaused)
                cancelledStatus = DownloadTaskStatus.Paused;        // 暂停：保留断点
            else if (_isShuttingDown)
                cancelledStatus = DownloadTaskStatus.Interrupted;   // 关闭：标记中断
            else if (context.IsParentCancelled)
                cancelledStatus = DownloadTaskStatus.Ready;         // 全局停止：放回队列
            else
                cancelledStatus = DownloadTaskStatus.Canceled;      // 单任务取消

            task.Status = ToStorage(cancelledStatus);
            await _repository.UpdateProgressAsync(task.TaskId, task.Progress, task.Status);
            _tracker.BroadcastProgress(task);

            if (cancelledStatus is DownloadTaskStatus.Paused or DownloadTaskStatus.Canceled)
            {
                _tracker.BroadcastStatusChanged(task);
                TaskStatusChanged?.Invoke(task);
            }
        }
        catch (Exception ex)
        {
            var safeError = SensitiveDataSanitizer.Sanitize(ex.Message);
            task.Status = ToStorage(DownloadTaskStatus.Failed);
            task.ErrorMessage = safeError;
            task.LastUpdatedAt = DateTime.Now;
            Log.Error($"任务 {task.TaskId} 下载失败: {safeError}", ex);
            await _repository.MarkFailedAsync(
                task.TaskId,
                task.Progress,
                safeError,
                task.ErrorType,
                task.IsRetryable,
                task.LastUpdatedAt);
            _tracker.BroadcastProgress(task);
            _tracker.BroadcastStatusChanged(task);
            TaskStatusChanged?.Invoke(task);
        }
        finally
        {
            // G2: 清理 per-task 上下文
            CleanupTaskContext(task.TaskId);
        }
    }

    private void SignalQueueChanged() => _queueWakeups.Writer.TryWrite(true);

    private void DrainQueueWakeups()
    {
        while (_queueWakeups.Reader.TryRead(out _))
        {
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

    #region G2 单任务控制

    /// <summary>
    /// 暂停单个任务。在下一个阶段边界生效，不取消 CTS（保留断点）。
    /// </summary>
    public async Task PauseTaskAsync(string taskId)
    {
        await _commandLock.WaitAsync();
        try
        {
            TaskRuntimeContext? ctx;
            lock (_schedulerLock) { _activeContexts.TryGetValue(taskId, out ctx); }

            if (ctx == null) return; // 任务不在活动中

            ctx.RequestPause();

            // 立即持久化状态（实际执行在阶段边界才暂停）
            var task = (await _repository.GetAllAsync()).FirstOrDefault(t => t.TaskId == taskId);
            if (task != null)
            {
                task.Status = ToStorage(DownloadTaskStatus.Paused);
                await _repository.UpdateProgressAsync(taskId, task.Progress, task.Status);
                _tracker.BroadcastStatusChanged(task);
                TaskStatusChanged?.Invoke(task);
            }
            SchedulerStatusChanged?.Invoke($"已暂停任务: {task?.ItemTitle ?? taskId}");
        }
        finally { _commandLock.Release(); }
    }

    /// <summary>
    /// 恢复已暂停的任务。将状态改回 Ready 并唤醒队列重新调度。
    /// </summary>
    public async Task ResumeTaskAsync(string taskId)
    {
        await _commandLock.WaitAsync();
        try
        {
            TaskRuntimeContext? ctx;
            lock (_schedulerLock) { _activeContexts.TryGetValue(taskId, out ctx); }

            if (ctx != null && ctx.IsPaused)
            {
                ctx.Resume();
            }

            var task = (await _repository.GetAllAsync()).FirstOrDefault(t => t.TaskId == taskId);
            if (task != null && ParseStatus(task.Status) == DownloadTaskStatus.Paused)
            {
                task.Status = ToStorage(DownloadTaskStatus.Ready);
                await _repository.UpdateProgressAsync(taskId, task.Progress, task.Status);
                _tracker.BroadcastStatusChanged(task);
                TaskStatusChanged?.Invoke(task);
            }

            StartProcessingInternal();
            SchedulerStatusChanged?.Invoke($"已恢复任务: {task?.ItemTitle ?? taskId}");
        }
        finally { _commandLock.Release(); }
    }

    /// <summary>
    /// 取消单个任务（不可逆）。通过 per-task CTS 取消，不影响其他任务。
    /// 取消后删除临时文件但保留成品文件。
    /// </summary>
    public async Task CancelTaskAsync(string taskId)
    {
        await _commandLock.WaitAsync();
        try
        {
            TaskRuntimeContext? ctx;
            Task? activeTask;
            lock (_schedulerLock)
            {
                _activeContexts.TryGetValue(taskId, out ctx);
                _activeTasks.TryGetValue(taskId, out activeTask);
            }

            if (ctx != null)
            {
                ctx.RequestCancellation();
                if (activeTask != null)
                {
                    try { await activeTask; } catch (OperationCanceledException) { }
                }
            }

            var task = (await _repository.GetAllAsync()).FirstOrDefault(t => t.TaskId == taskId);
            if (task != null)
            {
                task.Status = ToStorage(DownloadTaskStatus.Canceled);
                await _repository.UpdateProgressAsync(taskId, task.Progress, task.Status);
                CleanupTempFiles(task.TempDirectory);
                _tracker.BroadcastStatusChanged(task);
                TaskStatusChanged?.Invoke(task);
            }
            SchedulerStatusChanged?.Invoke($"已取消任务: {task?.ItemTitle ?? taskId}");
        }
        finally { _commandLock.Release(); }
    }

    /// <summary>
    /// 重新开始任务：取消当前执行 → 清理旧断点 → 重置进度 → 从零执行。
    /// 与 RetryTaskAsync 不同：RestartTaskAsync 可用于任何状态，且会清理临时文件。
    /// </summary>
    public async Task RestartTaskAsync(string taskId)
    {
        await _commandLock.WaitAsync();
        try
        {
            // 先取消（如果在运行）
            TaskRuntimeContext? ctx;
            Task? activeTask;
            lock (_schedulerLock)
            {
                _activeContexts.TryGetValue(taskId, out ctx);
                _activeTasks.TryGetValue(taskId, out activeTask);
            }

            if (ctx != null)
            {
                ctx.RequestCancellation();
                if (activeTask != null)
                {
                    try { await activeTask; } catch (OperationCanceledException) { }
                }
            }

            var task = (await _repository.GetAllAsync()).FirstOrDefault(t => t.TaskId == taskId);
            if (task != null)
            {
                // 清理旧断点和临时文件
                CleanupTempFiles(task.TempDirectory);
                task.TempDirectory = string.Empty;

                // 重置进度
                task.Progress = 0;
                task.VideoProgress = 0;
                task.AudioProgress = 0;
                task.MergeProgress = 0;
                task.VideoBytesDownloaded = 0;
                task.AudioBytesDownloaded = 0;
                task.ErrorMessage = null;
                task.Status = ToStorage(DownloadTaskStatus.Ready);

                await _repository.UpdateBytesAsync(taskId, 0, 0);
                await _repository.UpdateProgressAsync(taskId, 0, task.Status);

                _tracker.BroadcastStatusChanged(task);
                TaskStatusChanged?.Invoke(task);
                StartProcessingInternal();
            }
            SchedulerStatusChanged?.Invoke($"已重新开始任务: {task?.ItemTitle ?? taskId}");
        }
        finally { _commandLock.Release(); }
    }

    /// <summary>暂停所有活动任务</summary>
    public async Task PauseAllActiveAsync()
    {
        string[] activeIds;
        lock (_schedulerLock) { activeIds = _activeContexts.Keys.ToArray(); }
        foreach (var id in activeIds)
            await PauseTaskAsync(id);
    }

    /// <summary>恢复所有暂停任务</summary>
    public async Task ResumeAllPausedAsync()
    {
        var allTasks = await _repository.GetAllAsync();
        var paused = allTasks.Where(t => ParseStatus(t.Status) == DownloadTaskStatus.Paused).ToList();
        foreach (var t in paused)
            await ResumeTaskAsync(t.TaskId);
    }

    /// <summary>取消所有活动任务</summary>
    public async Task CancelAllActiveAsync()
    {
        string[] activeIds;
        lock (_schedulerLock) { activeIds = _activeContexts.Keys.ToArray(); }
        foreach (var id in activeIds)
            await CancelTaskAsync(id);
    }

    /// <summary>重新开始所有失败/中断/取消任务</summary>
    public async Task RestartAllStalledAsync()
    {
        var allTasks = await _repository.GetAllAsync();
        var stalled = allTasks.Where(t =>
        {
            var s = ParseStatus(t.Status);
            return s is DownloadTaskStatus.Failed or DownloadTaskStatus.Interrupted
                or DownloadTaskStatus.Canceled;
        }).ToList();
        foreach (var t in stalled)
            await RestartTaskAsync(t.TaskId);
    }

    /// <summary>
    /// G2: 登录状态变更处理。登录成功时自动恢复所有 WaitingForLogin 任务。
    /// </summary>
    private async Task HandleLoginStateChangedAsync(LoginStateChangedMessage msg)
    {
        if (!msg.IsLoggedIn) return;

        try
        {
            var allTasks = await _repository.GetAllAsync();
            var waitingTasks = allTasks
                .Where(t => ParseStatus(t.Status) == DownloadTaskStatus.WaitingForLogin)
                .ToList();

            foreach (var task in waitingTasks)
            {
                task.Status = ToStorage(DownloadTaskStatus.Ready);
                await _repository.UpdateProgressAsync(task.TaskId, task.Progress, task.Status);
                _tracker.BroadcastStatusChanged(task);
                TaskStatusChanged?.Invoke(task);
            }

            if (waitingTasks.Count > 0)
            {
                SchedulerStatusChanged?.Invoke($"登录成功，{waitingTasks.Count} 个等待登录的任务已恢复");
                StartProcessingInternal();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"处理登录状态变更失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// G2: 并发数下调时优雅暂停超额任务（LIFO：新任务断点少，优先暂停）
    /// </summary>
    private async Task GracefulScaleDownAsync(int targetCount)
    {
        // 短暂等待自然完成
        await Task.Delay(200);

        string[] excessIds;
        lock (_schedulerLock)
        {
            var excess = _activeContexts.Count - targetCount;
            if (excess <= 0) return;
            // 取最后加入的（新任务断点少）
            excessIds = _activeContexts.Keys.TakeLast(excess).ToArray();
        }

        foreach (var id in excessIds)
            await PauseTaskAsync(id);
    }

    /// <summary>G2: 清理单个任务的运行时上下文</summary>
    private void CleanupTaskContext(string taskId)
    {
        TaskRuntimeContext? ctx;
        lock (_schedulerLock)
        {
            if (_activeContexts.TryGetValue(taskId, out ctx))
            {
                _activeContexts.Remove(taskId);
            }
        }
        ctx?.Dispose();
    }

    /// <summary>清理临时文件目录（忽略失败）</summary>
    private static void CleanupTempFiles(string? tempDir)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
        catch { /* 忽略清理失败 */ }
    }

    #endregion
}
