using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using BiliDownloader.Services.Naming;
using BiliDownloader.Services.Download.Extras;
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
    private readonly IMediaMergeRetryExecutor? _mergeRetryExecutor;
    private readonly IBiliDataPaths _paths;
    private readonly IBiliCredentialProvider _credentialProvider;
    private readonly IDownloadRecoveryService _recoveryService;
    private readonly ISubmissionPreflightService? _preflightService;

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
        IBiliCredentialProvider? credentialProvider = null,
        IDownloadRecoveryService? recoveryService = null,
        ISubmissionPreflightService? preflightService = null,
        IMediaMergeRetryExecutor? mergeRetryExecutor = null)
    {
        _repository = repository;
        _messengerService = messengerService;
        _tracker = tracker;
        _executor = executor;
        _paths = paths;
        _credentialProvider = credentialProvider ?? new NullCredentialProvider();
        _recoveryService = recoveryService ?? new DownloadRecoveryService(repository);
        _preflightService = preflightService;
        _mergeRetryExecutor = mergeRetryExecutor ?? executor as IMediaMergeRetryExecutor;

        // 注册监听：接收 Document 发来的下载任务提交消息
        _messengerService.Register<BiliDownloadCoordinator, SubmitDownloadTaskMessage>(
            this, (coordinator, msg) =>
            {
                _ = coordinator.HandleSubmitMessageAsync(msg);
            });

        // 登录状态消息不能转换任务状态。等待登录任务属于已经暂停的用户意图，
        // 即使凭据重新可用，也必须由用户点击“恢复”后才允许重新进入调度队列。
        // Coordinator 因此只监听明确的任务提交命令，不把环境事件当成执行授权。
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
            var submission = msg.ToSubmission();
            if (_preflightService is null)
            {
                await SubmitTasksAsync(submission);
                return;
            }
            var report = await _preflightService.InspectAsync(submission);
            if (report.IsBlocked || report.RequiresConfirmation)
            {
                Log.Warn("旧消息提交需要用户确认或存在阻止项，已安全拒绝；请使用 G6 可等待提交服务。");
                return;
            }
            await CommitPreparedAsync(new PreparedSubmission(report, false), _preflightService);
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

        // G3: 校验临时文件事实，确保断点字节数与磁盘一致。
        // 设计思考：以磁盘事实为准，而非信任数据库中的旧值。
        // 异常退出可能导致数据库记录的字节数大于实际文件大小（写入后未 fsync），
        // 如果信任旧值，续传时会从错误位置开始，导致文件损坏。
        foreach (var task in allTasks)
        {
            var status = ParseStatus(task.Status);
            if (status is DownloadTaskStatus.Interrupted or DownloadTaskStatus.Paused)
            {
                await _recoveryService.ReconcileAsync(task);
            }
        }

        SchedulerStatusChanged?.Invoke("协调器已初始化");
    }

    /// <summary>
    /// 接收新任务：批量插入 SQLite 并启动处理队列
    /// </summary>
    public async Task SubmitTasksAsync(SubmitDownloadTaskMessage msg, string defaultOutputDirectory)
        => await SubmitTasksAsync(msg.ToSubmission());

    public async Task SubmitTasksAsync(DownloadSubmission submission)
    {
        await InitializeAsync();
        await _commandLock.WaitAsync();
        try
        {
            ThrowIfShuttingDown();

            var records = new List<DownloadTaskRecord>();
            var profile = submission.Profile;
            var subFolder = profile.UseGroupFolder
                ? FileNameSanitizer.Sanitize(submission.SeriesTitle)
                : string.Empty;

            foreach (var item in submission.Items)
            {
                var record = new DownloadTaskRecord
                {
                    TaskId = item.ItemId,
                    DocumentId = submission.DocumentId,
                    SourceDocumentTitle = submission.DocumentTitle,
                    SeriesTitle = submission.SeriesTitle,
                    ItemTitle = item.Title,
                    Aid = item.Aid,
                    Bvid = item.Bvid,
                    Cid = item.Cid,
                    MediaUnitKey = CreateMediaUnitStorageKey(item),
                    RenditionFingerprint = CreateRenditionStorageKey(item, profile),
                    QualityId = profile.VideoQualityId,
                    AudioQualityId = profile.AudioQualityId,
                    SubmissionSnapshotVersion = 2,
                    DurationSeconds = item.Duration,
                    UseGroupFolder = profile.UseGroupFolder,
                    AddIndexToTitle = profile.AddIndexToTitle,
                    NamingTemplate = profile.NamingTemplate,
                    PresetId = profile.PresetId,
                    SelectedVideoCodec = profile.VideoCodecPreference,
                    SelectedOutputContainer = profile.OutputContainer,
                    SelectedOutputMediaMode = profile.OutputMediaMode,
                    SelectedVideoDynamicRangePreference = profile.VideoDynamicRangePreference,
                    SelectedAudioFeaturePreference = profile.AudioFeaturePreference,
                    RequestedMediaFeatures = GetExplicitRequestedFeatures(profile),
                    RedownloadedFromTaskId = submission.RedownloadedFromTaskId ?? string.Empty,
                    OutputDirectory = profile.OutputDirectory,
                    SubFolder = subFolder,
                    Status = ToStorage(DownloadTaskStatus.Ready),
                    CreatedAt = DateTime.Now,
                    LastUpdatedAt = DateTime.Now,
                    MediaType = item.MediaType.ToString().ToLowerInvariant(),
                    EpId = item.EpId,
                    SeasonId = item.SeasonId,
                    ExtrasConfig = (profile.DownloadDanmaku ? (int)ExtrasType.Danmaku : 0)
                        | (profile.DownloadSubtitle ? (int)ExtrasType.Subtitle : 0)
                        | (profile.DownloadCover ? (int)ExtrasType.Cover : 0),
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
    /// 提交 G6 预检批次。Coordinator 在唯一命令锁内重新读取文件和任务事实；
    /// 若指纹变化则返回 Stale，绝不沿用用户确认前的旧冲突数量。
    /// 路径保留和任务插入由 SQLite 同一事务完成，因此并发 Document 只能有一个成功占用目标。
    /// </summary>
    public async Task<SubmissionCommitResult> CommitPreparedAsync(
        PreparedSubmission prepared,
        ISubmissionPreflightService preflight,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync();
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfShuttingDown();
            var current = await preflight.InspectAsync(prepared.Report.Submission, cancellationToken);
            if (!string.Equals(current.Fingerprint, prepared.Report.Fingerprint, StringComparison.Ordinal))
                return new(SubmissionCommitStatus.Stale, 0, current.SkipCount, "输出目录事实已变化，请重新确认预检结果。");
            if (current.GlobalIssues.Concat(current.Items.SelectMany(item => item.Issues))
                .Any(issue => issue.Code == "stale_comparison"))
                return new(SubmissionCommitStatus.StaleComparison, 0, current.SkipCount,
                    "增量检查后的任务事实已变化，已拒绝旧结果并要求刷新分类。");
            if (current.IsBlocked)
                return new(SubmissionCommitStatus.Blocked, 0, current.SkipCount, BuildPreflightMessage(current));
            if (current.RequiresConfirmation && !prepared.UserConfirmed)
                return new(SubmissionCommitStatus.Blocked, 0, current.SkipCount, "当前预检结果需要用户明确确认。");

            var profile = current.Submission.Profile;
            var subFolder = profile.UseGroupFolder
                ? FileNameSanitizer.Sanitize(current.Submission.SeriesTitle)
                : string.Empty;
            var records = new List<DownloadTaskRecord>();
            var taskReferences = new List<CommittedTaskReference>();
            var resumed = 0;
            var resumedTaskIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var planned in current.Items.Where(item => item.ShouldSubmit))
            {
                if (planned.IsResume && !string.IsNullOrWhiteSpace(planned.ResumeTaskId))
                {
                    if (!resumedTaskIds.Add(planned.ResumeTaskId))
                        return new(SubmissionCommitStatus.Blocked, 0, current.SkipCount,
                            "同一续传任务在批次中出现多次，请重新选择内容。");
                    await _repository.PrepareVerifiedResumeAsync(
                        planned.ResumeTaskId,
                        planned.OutputFilePath,
                        planned.OutputPathKey,
                        profile.ConflictPolicy,
                        planned.EstimatedRequiredBytes);
                    taskReferences.Add(new CommittedTaskReference(planned.Item.ItemId, planned.ResumeTaskId));
                    resumed++;
                    continue;
                }
                var item = planned.Item;
                var taskId = Guid.NewGuid().ToString("N");
                records.Add(new DownloadTaskRecord
                {
                    TaskId = taskId,
                    DocumentId = current.Submission.DocumentId,
                    SourceDocumentTitle = current.Submission.DocumentTitle,
                    SeriesTitle = current.Submission.SeriesTitle,
                    ItemTitle = item.Title,
                    Aid = item.Aid,
                    Bvid = item.Bvid,
                    Cid = item.Cid,
                    MediaUnitKey = CreateMediaUnitStorageKey(item),
                    RenditionFingerprint = CreateRenditionStorageKey(item, profile),
                    QualityId = profile.VideoQualityId,
                    AudioQualityId = profile.AudioQualityId,
                    SubmissionSnapshotVersion = 2,
                    DurationSeconds = item.Duration,
                    UseGroupFolder = profile.UseGroupFolder,
                    AddIndexToTitle = profile.AddIndexToTitle,
                    NamingTemplate = profile.NamingTemplate,
                    PresetId = profile.PresetId,
                    SelectedVideoCodec = profile.VideoCodecPreference,
                    SelectedOutputContainer = profile.OutputContainer,
                    SelectedOutputMediaMode = profile.OutputMediaMode,
                    SelectedVideoDynamicRangePreference = profile.VideoDynamicRangePreference,
                    SelectedAudioFeaturePreference = profile.AudioFeaturePreference,
                    RequestedMediaFeatures = GetExplicitRequestedFeatures(profile),
                    ExpectedMediaFeatures = planned.OutputPlan?.ExpectedMediaFeatures,
                    ActualVideoCodec = ToStorageCodec(planned.OutputPlan?.ActualVideoCodec ?? VideoCodec.Unknown),
                    RedownloadedFromTaskId = current.Submission.RedownloadedFromTaskId ?? string.Empty,
                    OutputDirectory = profile.OutputDirectory,
                    SubFolder = subFolder,
                    OutputFilePath = planned.OutputFilePath,
                    OutputPathKey = planned.OutputPathKey,
                    ConflictPolicy = profile.ConflictPolicy,
                    EstimatedRequiredBytes = planned.EstimatedRequiredBytes,
                    OverwriteConfirmed = profile.ConflictPolicy == FileConflictPolicy.Overwrite && prepared.UserConfirmed,
                    Status = ToStorage(DownloadTaskStatus.Ready),
                    CreatedAt = DateTime.Now,
                    LastUpdatedAt = DateTime.Now,
                    MediaType = item.MediaType.ToString().ToLowerInvariant(),
                    EpId = item.EpId,
                    SeasonId = item.SeasonId,
                    ExtrasConfig = (profile.DownloadDanmaku ? (int)ExtrasType.Danmaku : 0)
                        | (profile.DownloadSubtitle ? (int)ExtrasType.Subtitle : 0)
                        | (profile.DownloadCover ? (int)ExtrasType.Cover : 0),
                    CoverUrl = item.CoverUrl,
                });
                taskReferences.Add(new CommittedTaskReference(item.ItemId, taskId));
            }
            if (records.Count > 0) await _repository.InsertBatchAsync(records);
            var committed = records.Count + resumed;
            if (committed > 0)
            {
                TaskListChanged?.Invoke();
                SchedulerStatusChanged?.Invoke($"已接收 {committed} 个通过预检的任务");
                StartProcessingInternal();
            }
            return new(SubmissionCommitStatus.Committed, committed, current.SkipCount,
                $"已提交 {committed} 项，跳过 {current.SkipCount} 项。", taskReferences);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return new(SubmissionCommitStatus.Stale, 0, prepared.Report.SkipCount,
                "任务或输出路径已被其他提交占用，请重新预检。");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private static string BuildPreflightMessage(SubmissionPreflightReport report)
        => string.Join("；", report.GlobalIssues
            .Concat(report.Items.SelectMany(item => item.Issues))
            .Where(issue => issue.Severity == PreflightIssueSeverity.Blocking)
            .Select(issue => issue.Message)
            .Distinct());

    private static string CreateMediaUnitStorageKey(DownloadSubmissionItem item) =>
        item.Aid > 0 && item.Cid > 0
            ? new MediaUnitKey(item.Aid, item.Cid).ToStorageKey()
            : string.Empty;

    private static string CreateRenditionStorageKey(
        DownloadSubmissionItem item,
        DownloadProfileSnapshot profile) =>
        item.Aid > 0 && item.Cid > 0 && profile.VideoQualityId > 0
            ? RenditionFingerprint.Create(
                new MediaUnitKey(item.Aid, item.Cid), profile.ToRenditionSpecification()).Value
            : string.Empty;

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
        => await DeleteTaskAsync(task, DeleteTaskOptions.RecordOnly, CancellationToken.None);

    public async Task DeleteTaskAsync(
        DownloadTaskRecord task,
        DeleteTaskOptions options,
        CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken);
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
            _tracker.Remove(task.TaskId);

            // 通知对应 Document 移除
            try
            {
                _messengerService.Send(new DownloadTaskDeletedMessage(task.DocumentId, task.TaskId));
            }
            catch { /* 忽略广播失败 */ }

            if (options.DeleteTemporaryFiles)
                CleanupTempFiles(task.TempDirectory);

            // 清理成品文件
            try
            {
                if (options.DeleteOutputFile
                    && !string.IsNullOrWhiteSpace(task.OutputFilePath)
                    && File.Exists(task.OutputFilePath))
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
            // G7：普通重试必须保留经过下载器验证的断点。只有 RestartTaskAsync 才代表用户明确
            // 选择“从零开始”并清理字节事实，避免网络/CDN 短暂失败导致重复下载大文件。
            task.ErrorMessage = null;
            task.ErrorType = null;
            task.IsRetryable = false;

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

            // G3: 关闭前 flush 所有待写入进度，确保最后进度不丢失
            await _tracker.ShutdownAsync();

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
            // G2: 阶段边界暂停检查（如果已暂停则阻塞直到恢复或取消）
            context.WaitIfPaused();

            var result = await _executor.ExecuteAsync(
                task,
                new DownloadExecutionCallbacks(
                (info) =>
                {
                    _tracker.OnProgressChanged(task, info);
                    TaskProgressChanged?.Invoke(task);
                },
                (videoBytes, audioBytes) =>
                {
                    _tracker.OnBytesChanged(task, videoBytes, audioBytes);
                },
                async checkpoint =>
                {
                    task.ExpectedVideoBytes = checkpoint.ExpectedVideoBytes;
                    task.ExpectedAudioBytes = checkpoint.ExpectedAudioBytes;
                    task.VideoIntegrityPassed = checkpoint.VideoIntegrityPassed;
                    task.AudioIntegrityPassed = checkpoint.AudioIntegrityPassed;
                    task.LastUpdatedAt = DateTime.Now;
                    await _repository.UpdateIntegrityAsync(
                        task.TaskId,
                        checkpoint.ExpectedVideoBytes,
                        checkpoint.ExpectedAudioBytes,
                        checkpoint.VideoIntegrityPassed,
                        checkpoint.AudioIntegrityPassed,
                        task.LastUpdatedAt);
                },
                async outputPlan =>
                {
                    task.ActualVideoCodec = ToStorageCodec(outputPlan.ActualVideoCodec);
                    task.ExpectedMediaFeatures = outputPlan.ExpectedMediaFeatures;
                    task.LastUpdatedAt = DateTime.Now;
                    await _repository.UpdateActualVideoCodecAsync(
                        task.TaskId, task.ActualVideoCodec, task.LastUpdatedAt);
                    await _repository.UpdateExpectedMediaFeaturesAsync(
                        task.TaskId, outputPlan.ExpectedMediaFeatures, task.LastUpdatedAt);
                }),
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

            // G3: 确保节流中的最后进度已落盘，再写终态
            await _tracker.FlushAsync(task.TaskId);

            // 数据库提交完成后再发布内存终态，避免 UI 先观察到不可恢复的状态。
            var outputFilePath = result.OutputFilePath ?? "";
            var completedAt = DateTime.Now;
            if (result.ActualMediaFeatures.HasValue)
                task.ActualMediaFeatures = result.ActualMediaFeatures;
            if (task.ActualMediaFeatures.HasValue)
                await _repository.UpdateActualMediaFeaturesAsync(
                    task.TaskId, task.ActualMediaFeatures.Value, completedAt);
            await _repository.MarkCompletedAsync(
                task.TaskId,
                outputFilePath,
                task.ExtrasResultSummary,
                completedAt);
            task.Status = ToStorage(DownloadTaskStatus.Completed);
            task.Progress = 100;
            ApplyCompletedStageProgress(task);
            task.SpeedText = "";
            task.OutputFilePath = outputFilePath;
            task.LastUpdatedAt = completedAt;
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

            // G3: 确保节流中的最后进度已落盘，再写终态
            await _tracker.FlushAsync(task.TaskId);

            var cancelledStorageStatus = ToStorage(cancelledStatus);
            await _repository.UpdateProgressAsync(task.TaskId, task.Progress, cancelledStorageStatus);
            task.Status = cancelledStorageStatus;
            _tracker.BroadcastProgress(task);

            if (cancelledStatus is DownloadTaskStatus.Paused or DownloadTaskStatus.Canceled)
            {
                _tracker.BroadcastStatusChanged(task);
                TaskStatusChanged?.Invoke(task);
            }
        }
        catch (Exception ex)
        {
            // G3: 确保节流中的最后进度已落盘，再写终态
            await _tracker.FlushAsync(task.TaskId);

            var safeError = SensitiveDataSanitizer.Sanitize(ex.Message);
            // G3: 错误分类 → 填充 ErrorType 和 IsRetryable，供 UI 展示和重试判断
            var failure = DownloadErrorClassifier.ClassifyFailure(ex);
            var failedAt = DateTime.Now;

            if (failure.Kind == DownloadFailureKind.Authentication)
            {
                task.Status = ToStorage(DownloadTaskStatus.WaitingForLogin);
                task.ErrorMessage = safeError;
                task.ErrorType = failure.StorageValue;
                task.IsRetryable = false;
                task.LastUpdatedAt = failedAt;
                await _repository.UpdateProgressAsync(task.TaskId, task.Progress, task.Status, safeError);
                _tracker.BroadcastStatusChanged(task);
                TaskStatusChanged?.Invoke(task);
                return;
            }

            if (ex is InsufficientDiskSpaceException or OutputConflictException)
            {
                await _repository.MarkFailedAsync(task.TaskId, task.Progress, safeError,
                    failure.StorageValue, false, failedAt);
                task.Status = ToStorage(DownloadTaskStatus.Paused);
                await _repository.UpdateProgressAsync(task.TaskId, task.Progress, task.Status, safeError);
                task.ErrorMessage = safeError;
                task.ErrorType = failure.StorageValue;
                task.IsRetryable = false;
                _tracker.BroadcastStatusChanged(task);
                TaskStatusChanged?.Invoke(task);
                return;
            }

            Log.Error($"任务 {task.TaskId} 下载失败: {safeError}", ex);
            await _repository.MarkFailedAsync(
                task.TaskId,
                task.Progress,
                safeError,
                failure.StorageValue,
                failure.IsRetryable,
                failedAt);
            task.Status = ToStorage(DownloadTaskStatus.Failed);
            task.ErrorMessage = safeError;
            task.ErrorType = failure.StorageValue;
            task.IsRetryable = failure.IsRetryable;
            task.LastUpdatedAt = failedAt;
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

    /// <summary>
    /// 仅重试已经下载并校验完成的媒体合并。该命令在开始前验证持久化检查点、临时文件长度和
    /// 输出路径所有权；任一事实不可信时拒绝执行，而不是静默退化为重新下载。
    /// </summary>
    public async Task RetryMergeAsync(string taskId)
    {
        if (_mergeRetryExecutor is null)
            throw new InvalidOperationException("当前下载执行器不支持仅重试合并。");

        DownloadTaskRecord task;
        TaskRuntimeContext context;
        await _commandLock.WaitAsync();
        try
        {
            task = (await _repository.GetAllAsync()).SingleOrDefault(item => item.TaskId == taskId)
                ?? throw new InvalidOperationException("待重试合并的任务不存在。");
            if (ParseStatus(task.Status) != DownloadTaskStatus.Failed
                || task.ErrorType is not ("ffmpeg" or "merge"))
                throw new InvalidOperationException("只有 ffmpeg 或合并失败任务可以仅重试合并。");
            if ((task.SelectedOutputMediaMode ?? OutputMediaMode.AudioVideo) == OutputMediaMode.AudioOnly)
                throw new InvalidOperationException("仅音频任务没有合并阶段，请使用普通重试或重新开始。");

            ValidateMergeCheckpoint(task);
            if (!await _repository.OwnsOutputPathReservationAsync(task.TaskId, task.OutputPathKey))
                throw new InvalidOperationException("任务已失去输出路径保留，请重新选择输出位置。");

            lock (_schedulerLock)
            {
                if (_activeTasks.ContainsKey(taskId))
                    throw new InvalidOperationException("任务正在执行，不能重复启动合并。");
                context = TaskRuntimeContext.CreateLinked(
                    taskId, _processingCts?.Token ?? CancellationToken.None);
                _activeContexts[taskId] = context;
            }

            task.Status = ToStorage(DownloadTaskStatus.Merging);
            task.ErrorMessage = null;
            await _repository.UpdateStageProgressAsync(
                task.TaskId, task.Progress, task.Status,
                100,
                (task.SelectedOutputMediaMode ?? OutputMediaMode.AudioVideo) == OutputMediaMode.AudioVideo ? 100 : 0,
                Math.Max(1, task.MergeProgress), "");
            _tracker.BroadcastStatusChanged(task);
            TaskStatusChanged?.Invoke(task);
        }
        finally
        {
            _commandLock.Release();
        }

        var operation = ExecuteMergeRetryCoreAsync(task, context);
        lock (_schedulerLock) _activeTasks[taskId] = operation;
        try
        {
            await operation;
        }
        finally
        {
            lock (_schedulerLock)
            {
                _activeTasks.Remove(taskId);
                _activeContexts.Remove(taskId);
            }
            context.Dispose();
        }
    }

    /// <summary>
    /// 将失败或暂停任务迁移到用户选择的新目录并重新排队。新位置始终使用自动编号策略，
    /// 因为旧路径的覆盖确认只授权旧文件，绝不能跨目录复用。
    /// </summary>
    public async Task RelocateTaskOutputAsync(string taskId, string newDirectory)
    {
        if (string.IsNullOrWhiteSpace(newDirectory)) return;
        await _commandLock.WaitAsync();
        try
        {
            var task = (await _repository.GetAllAsync()).SingleOrDefault(item => item.TaskId == taskId)
                ?? throw new InvalidOperationException("待迁移任务不存在。");
            lock (_schedulerLock)
            {
                if (_activeTasks.ContainsKey(taskId))
                    throw new InvalidOperationException("任务正在执行，不能更换输出目录。");
            }

            string directory;
            try
            {
                directory = Path.GetFullPath(newDirectory);
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, $".bili-write-probe-{Guid.NewGuid():N}");
                File.WriteAllBytes(probe, []);
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new OutputDirectoryException("所选输出目录无法创建或写入。", ex);
            }

            var baseName = !string.IsNullOrWhiteSpace(task.OutputFilePath)
                ? Path.GetFileNameWithoutExtension(task.OutputFilePath)
                : FileNameSanitizer.Sanitize(task.ItemTitle);
            var extension = !string.IsNullOrWhiteSpace(task.OutputFilePath)
                ? Path.GetExtension(task.OutputFilePath)
                : (task.SelectedOutputMediaMode == OutputMediaMode.AudioOnly
                    ? ".m4a"
                    : task.SelectedOutputContainer == OutputContainer.Mkv ? ".mkv" : ".mp4");
            var existing = (await _repository.GetAllAsync())
                .Where(item => item.TaskId != taskId && !string.IsNullOrWhiteSpace(item.OutputFilePath))
                .Select(item => NormalizePathKey(item.OutputFilePath))
                .ToHashSet(GetPathComparer());

            string outputPath = "";
            string outputKey = "";
            for (var suffix = 0; suffix <= 9999; suffix++)
            {
                var fileName = suffix == 0 ? baseName + extension : $"{baseName} ({suffix}){extension}";
                var candidate = Path.Combine(directory, fileName);
                var key = NormalizePathKey(candidate);
                if (!File.Exists(candidate) && !existing.Contains(key))
                {
                    outputPath = candidate;
                    outputKey = key;
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new OutputDirectoryException("新目录中没有可用的自动编号文件名。");

            await _repository.RelocateOutputAsync(taskId, directory, outputPath, outputKey);
            task.OutputDirectory = directory;
            task.SubFolder = "";
            task.OutputFilePath = outputPath;
            task.OutputPathKey = outputKey;
            task.ConflictPolicy = FileConflictPolicy.AutoNumber;
            task.OverwriteConfirmed = false;
            task.Status = ToStorage(DownloadTaskStatus.Ready);
            task.ErrorMessage = null;
            task.ErrorType = null;
            task.IsRetryable = false;
            _tracker.BroadcastStatusChanged(task);
            TaskStatusChanged?.Invoke(task);
            SchedulerStatusChanged?.Invoke($"已更换输出目录: {task.ItemTitle}");
            StartProcessingInternal();
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private static string NormalizePathKey(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private async Task ExecuteMergeRetryCoreAsync(DownloadTaskRecord task, TaskRuntimeContext context)
    {
        try
        {
            var result = await _mergeRetryExecutor!.ExecuteMergeOnlyAsync(
                task,
                info =>
                {
                    _tracker.OnProgressChanged(task, info);
                    TaskProgressChanged?.Invoke(task);
                },
                context.Token);
            if (!string.IsNullOrWhiteSpace(result.ExtrasResultSummary))
            {
                task.ExtrasResultSummary = result.ExtrasResultSummary;
                await _repository.UpdateExtrasResultAsync(task.TaskId, result.ExtrasResultSummary);
            }
            await _tracker.FlushAsync(task.TaskId);
            await CompleteTaskAsync(task, result);
            SchedulerStatusChanged?.Invoke($"已完成合并重试: {task.ItemTitle}");
        }
        catch (OperationCanceledException)
        {
            await _tracker.FlushAsync(task.TaskId);
            task.Status = ToStorage(_isShuttingDown
                ? DownloadTaskStatus.Interrupted
                : context.IsPaused ? DownloadTaskStatus.Paused : DownloadTaskStatus.Canceled);
            await _repository.UpdateProgressAsync(task.TaskId, task.Progress, task.Status);
            _tracker.BroadcastStatusChanged(task);
            TaskStatusChanged?.Invoke(task);
        }
        catch (Exception ex)
        {
            await _tracker.FlushAsync(task.TaskId);
            var failure = DownloadErrorClassifier.ClassifyFailure(ex);
            var technical = SensitiveDataSanitizer.Sanitize(ex.Message);
            Log.Error($"任务 {task.TaskId} 合并重试失败: {technical}", ex);
            task.Status = ToStorage(DownloadTaskStatus.Failed);
            task.ErrorMessage = technical;
            task.ErrorType = failure.StorageValue;
            task.IsRetryable = failure.IsRetryable;
            task.LastUpdatedAt = DateTime.Now;
            await _repository.MarkFailedAsync(
                task.TaskId, task.Progress, task.ErrorMessage,
                task.ErrorType, task.IsRetryable, task.LastUpdatedAt);
            _tracker.BroadcastStatusChanged(task);
            TaskStatusChanged?.Invoke(task);
        }
    }

    private static void ValidateMergeCheckpoint(DownloadTaskRecord task)
    {
        var mode = task.SelectedOutputMediaMode ?? OutputMediaMode.AudioVideo;
        if (mode == OutputMediaMode.AudioOnly)
            throw new InvalidOperationException("仅音频任务没有可重试的合并阶段。");
        var requiresAudio = mode == OutputMediaMode.AudioVideo;
        if (!task.VideoIntegrityPassed || task.ExpectedVideoBytes <= 0
            || (requiresAudio && (!task.AudioIntegrityPassed || task.ExpectedAudioBytes <= 0)))
            throw new InvalidOperationException("缺少可信媒体检查点，请执行完整重试或重新开始。");
        if (string.IsNullOrWhiteSpace(task.TempDirectory))
            throw new InvalidOperationException("任务临时目录缺失，请执行完整重试或重新开始。");
        var video = Path.Combine(task.TempDirectory, "video.tmp");
        var audio = Path.Combine(task.TempDirectory, "audio.tmp");
        if (!File.Exists(video) || new FileInfo(video).Length != task.ExpectedVideoBytes
            || (requiresAudio && (!File.Exists(audio) || new FileInfo(audio).Length != task.ExpectedAudioBytes)))
            throw new InvalidOperationException("临时媒体长度与检查点不一致，请执行完整重试或重新开始。");
    }

    private async Task CompleteTaskAsync(DownloadTaskRecord task, DownloadExecutionResult result)
    {
        var outputFilePath = result.OutputFilePath ?? "";
        var completedAt = DateTime.Now;
        if (result.ActualMediaFeatures.HasValue)
            task.ActualMediaFeatures = result.ActualMediaFeatures;
        if (task.ActualMediaFeatures.HasValue)
            await _repository.UpdateActualMediaFeaturesAsync(
                task.TaskId, task.ActualMediaFeatures.Value, completedAt);
        await _repository.MarkCompletedAsync(
            task.TaskId, outputFilePath, task.ExtrasResultSummary, completedAt);
        task.Status = ToStorage(DownloadTaskStatus.Completed);
        task.Progress = 100;
        ApplyCompletedStageProgress(task);
        task.SpeedText = "";
        task.OutputFilePath = outputFilePath;
        task.ErrorMessage = null;
        task.ErrorType = null;
        task.IsRetryable = false;
        task.LastUpdatedAt = completedAt;
        _tracker.BroadcastProgress(task);
        _tracker.BroadcastStatusChanged(task);
        TaskStatusChanged?.Invoke(task);
    }

    private static void ApplyCompletedStageProgress(DownloadTaskRecord task)
    {
        switch (task.SelectedOutputMediaMode ?? OutputMediaMode.AudioVideo)
        {
            case OutputMediaMode.VideoOnly:
                task.VideoProgress = 100;
                task.AudioProgress = 0;
                task.MergeProgress = 100;
                break;
            case OutputMediaMode.AudioOnly:
                task.VideoProgress = 0;
                task.AudioProgress = 100;
                task.MergeProgress = 0;
                break;
            default:
                task.VideoProgress = 100;
                task.AudioProgress = 100;
                task.MergeProgress = 100;
                break;
        }
    }

    private static string ToStorageCodec(VideoCodec codec) => codec switch
    {
        VideoCodec.Avc => "avc",
        VideoCodec.Hevc => "hevc",
        VideoCodec.Av1 => "av1",
        _ => string.Empty,
    };

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
    /// 恢复已暂停或等待登录的任务。等待登录任务会在此处重新检查当前凭据，
    /// 只有这次显式用户命令和有效登录态同时成立时，任务才会重新进入 Ready。
    /// 状态必须先落库并通知 UI，最后才释放暂停门控或唤醒调度器，避免执行先于事实源。
    /// </summary>
    public async Task ResumeTaskAsync(string taskId)
    {
        await _commandLock.WaitAsync();
        try
        {
            var task = (await _repository.GetAllAsync()).FirstOrDefault(t => t.TaskId == taskId);
            if (task is null)
            {
                return;
            }

            var status = ParseStatus(task.Status);
            if (status == DownloadTaskStatus.WaitingForLogin && !_credentialProvider.IsLoggedIn)
            {
                SchedulerStatusChanged?.Invoke($"任务仍需登录，未启动: {task.ItemTitle}");
                return;
            }
            if (status is not (DownloadTaskStatus.Paused or DownloadTaskStatus.WaitingForLogin))
            {
                return;
            }

            task.Status = ToStorage(DownloadTaskStatus.Ready);
            await _repository.UpdateProgressAsync(taskId, task.Progress, task.Status);
            _tracker.BroadcastStatusChanged(task);
            TaskStatusChanged?.Invoke(task);

            // 活动暂停任务可能仍停在执行器的暂停门控中。事实源和 UI 已经观察到 Ready 后
            // 才释放门控，确保恢复瞬间崩溃时 SQLite 仍能解释任务为何继续执行。
            TaskRuntimeContext? ctx;
            lock (_schedulerLock) { _activeContexts.TryGetValue(taskId, out ctx); }
            if (ctx?.IsPaused == true)
            {
                ctx.Resume();
            }
            StartProcessingInternal();
            SchedulerStatusChanged?.Invoke($"已恢复任务: {task.ItemTitle}");
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

    /// <summary>
    /// 显式恢复所有暂停或等待登录任务。方法名为兼容既有调用保留；实际筛选语义与
    /// 单项恢复一致，每一项仍会独立复核当前凭据，不能因批量命令绕过授权门禁。
    /// </summary>
    public async Task ResumeAllPausedAsync()
    {
        var allTasks = await _repository.GetAllAsync();
        var resumable = allTasks.Where(t => ParseStatus(t.Status)
            is DownloadTaskStatus.Paused or DownloadTaskStatus.WaitingForLogin).ToList();
        foreach (var t in resumable)
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

    /// <summary>
    /// 只把显式偏好记为“用户要求”。Auto 的实际高规格由运行时证据决定，不能事后改写成用户曾明确要求；
    /// 同时按输出模式清除不会被消费的维度，保证审计事实与 rendition 规范化规则一致。
    /// </summary>
    private static MediaFeatureFlags GetExplicitRequestedFeatures(DownloadProfileSnapshot profile)
    {
        var features = MediaFeatureFlags.None;
        if (profile.OutputMediaMode != OutputMediaMode.AudioOnly)
        {
            features |= profile.VideoDynamicRangePreference switch
            {
                VideoDynamicRangePreference.Hdr => MediaFeatureFlags.Hdr,
                VideoDynamicRangePreference.DolbyVision => MediaFeatureFlags.DolbyVision,
                _ => MediaFeatureFlags.None,
            };
        }
        if (profile.OutputMediaMode != OutputMediaMode.VideoOnly)
        {
            features |= profile.AudioFeaturePreference switch
            {
                AudioFeaturePreference.HiRes => MediaFeatureFlags.HiResAudio,
                AudioFeaturePreference.DolbyAtmos => MediaFeatureFlags.DolbyAtmos,
                _ => MediaFeatureFlags.None,
            };
        }
        return features;
    }

    #endregion
}
