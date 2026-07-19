using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Download.Extras;
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

    /// <summary>将存储字符串解析为枚举的快捷方法</summary>
    private static DownloadTaskStatus ParseStatus(string s) => DownloadTaskStatusMapper.FromStorageString(s);

    /// <summary>将枚举转换为存储字符串的快捷方法</summary>
    private static string ToStorage(DownloadTaskStatus s) => DownloadTaskStatusMapper.ToStorageString(s);

    #region 懒单例

    private static readonly Lazy<BiliDownloadCoordinator> _instance = new(CreateInstance);
    public static BiliDownloadCoordinator Instance => _instance.Value;

    private static BiliDownloadCoordinator CreateInstance()
    {
        var repository = new DownloadTaskStore();
        var messengerService = new MessengerService();
        var tracker = new DownloadProgressTracker(repository, messengerService);
        return new BiliDownloadCoordinator(
            repository,
            new BiliCredentialProvider(),
            messengerService,
            tracker);
    }

    #endregion

    private readonly IDownloadTaskRepository _repository;
    private readonly IBiliCredentialProvider _credentialProvider;
    private readonly IMessengerService _messengerService;
    private readonly IDownloadProgressTracker _tracker;
    private readonly BiliApiService _apiService = new();
    private readonly BiliDownloadService _downloadService = new();
    private readonly ExtrasHandlerRegistry _extrasRegistry = ExtrasHandlerRegistry.CreateDefault();

    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;
    private bool _isProcessing;
    private bool _initialized;

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

    public BiliDownloadCoordinator(
        IDownloadTaskRepository repository,
        IBiliCredentialProvider credentialProvider,
        IMessengerService messengerService,
        IDownloadProgressTracker tracker)
    {
        _repository = repository;
        _credentialProvider = credentialProvider;
        _messengerService = messengerService;
        _tracker = tracker;

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
                if (DownloadTaskStatusMapper.IsRunning(ParseStatus(t.Status)))
                {
                    t.Status = ToStorage(DownloadTaskStatus.Interrupted);
                    await _repository.UpdateProgressAsync(t.TaskId, t.Progress, ToStorage(DownloadTaskStatus.Interrupted));
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

            // 释放下载服务资源（HttpClient + MultiConnectionDownloader）
            _downloadService.Dispose();

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
                        var tempBase = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "BiliDownloader", "temp");
                        task.TempDirectory = Path.Combine(tempBase, task.TaskId);
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
                Task? completedTask = null;
                lock (_activeTasksLock)
                {
                    if (_activeTasks.Count > 0)
                        completedTask = Task.WhenAny(_activeTasks);
                }

                if (completedTask != null)
                {
                    await completedTask;
                    await completedTask; // 解包异常
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
            await _downloadService.DownloadItemAsync(
                task,
                _apiService,
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

            // 执行附加资源管线（失败不影响主任务状态）
            if (task.ExtrasConfig != 0)
            {
                await ExecuteExtrasPipelineAsync(task, ct);
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
            task.Status = ToStorage(DownloadTaskStatus.Ready);
            await _repository.UpdateProgressAsync(task.TaskId, task.Progress, ToStorage(DownloadTaskStatus.Ready));
            _tracker.BroadcastProgress(task);
        }
        catch (Exception ex)
        {
            task.Status = ToStorage(DownloadTaskStatus.Failed);
            task.ErrorMessage = ex.Message;
            task.LastUpdatedAt = DateTime.Now;
            Log.Error($"任务 {task.TaskId} 下载失败: {ex.Message}", ex);
            await _repository.UpdateProgressAsync(task.TaskId, task.Progress, ToStorage(DownloadTaskStatus.Failed), ex.Message);
            _tracker.BroadcastProgress(task);
            _tracker.BroadcastStatusChanged(task);
            TaskStatusChanged?.Invoke(task);
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    /// <summary>
    /// 执行附加资源下载管线（弹幕/字幕/封面）。
    /// 失败不影响主任务状态，仅记录到 ExtrasResultSummary。
    /// </summary>
    private async Task ExecuteExtrasPipelineAsync(DownloadTaskRecord task, CancellationToken ct)
    {
        try
        {
            var extrasType = (ExtrasType)task.ExtrasConfig;
            var handlers = _extrasRegistry.Resolve(extrasType);
            if (handlers.Count == 0) return;

            var results = new List<string>();
            var actualOutputDir = string.IsNullOrEmpty(task.SubFolder)
                ? task.OutputDirectory
                : Path.Combine(task.OutputDirectory, task.SubFolder);
            var baseFileName = BiliDownloadService.SanitizeFileName(task.ItemTitle);

            var context = new ExtrasContext
            {
                TaskId = task.TaskId,
                Aid = task.Aid,
                Bvid = task.Bvid,
                Cid = task.Cid,
                EpId = task.EpId,
                SeasonId = task.SeasonId,
                MediaType = task.MediaType,
                OutputDirectory = task.OutputDirectory,
                SubFolder = task.SubFolder,
                BaseFileName = baseFileName,
#pragma warning disable CS0618
                Cookie = _credentialProvider.IsLoggedIn ? _credentialProvider.GetCookieHeader() : task.Cookie,
#pragma warning restore CS0618
                CoverUrl = task.CoverUrl,
                ApiService = _apiService,
                ProgressReporter = new Progress<string>(msg =>
                    SchedulerStatusChanged?.Invoke($"[{task.ItemTitle}] {msg}")),
            };

            foreach (var handler in handlers)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var result = await handler.ExecuteAsync(context, ct);
                    var status = result.Success ? "OK" : result.ErrorMessage;
                    results.Add($"{handler.Type}: {status}");
                    if (result.Success)
                        Log.Info($"Extras [{handler.DisplayName}] 成功: {string.Join(", ", result.OutputFiles)}");
                    else
                        Log.Warn($"Extras [{handler.DisplayName}] 失败: {result.ErrorMessage}");
                }
                catch (OperationCanceledException)
                {
                    results.Add($"{handler.Type}: CANCELLED");
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warn($"Extras [{handler.DisplayName}] 异常: {ex.Message}");
                    results.Add($"{handler.Type}: FAIL - {ex.Message}");
                }
            }

            task.ExtrasResultSummary = string.Join("; ", results);
            await _repository.UpdateExtrasResultAsync(task.TaskId, task.ExtrasResultSummary);
        }
        catch (Exception ex)
        {
            Log.Warn($"Extras 管线异常: {ex.Message}");
        }
    }

    #endregion
}
