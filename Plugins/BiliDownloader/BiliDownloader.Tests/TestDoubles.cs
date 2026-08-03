using System.Diagnostics;
using System.Net;
using BiliDownloader;
using BiliDownloader.Models;
using BiliDownloader.Services;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using Microsoft.Data.Sqlite;
using BiliDownloader.Services.Persistence;
using CommunityToolkit.Mvvm.Messaging;
using MyAvaloniaManagementCommon.Message;

namespace BiliDownloader.Tests;

internal sealed class TestDataPaths : IBiliDataPaths, IDisposable
{
    public TestDataPaths()
    {
        RootDirectory = Path.Combine(
            Path.GetTempPath(),
            "BiliDownloader.Tests",
            Guid.NewGuid().ToString("N"),
            "BiliDownloader");
        DataDirectory = RootDirectory;
        LogDirectory = Path.Combine(RootDirectory, "logs");
        TempDirectory = Path.Combine(RootDirectory, "temp");
        DownloadTaskDatabasePath = Path.Combine(RootDirectory, "bili_download_tasks.db");
        CredentialDatabasePath = Path.Combine(RootDirectory, "credentials.db");
        CredentialKeyPath = Path.Combine(RootDirectory, "credential.key");
        StorageEpochMarkerPath = Path.Combine(RootDirectory, "storage_epoch_v2");
        ResetDirectories = [RootDirectory];
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string LogDirectory { get; }
    public string TempDirectory { get; }
    public string DownloadTaskDatabasePath { get; }
    public string CredentialDatabasePath { get; }
    public string CredentialKeyPath { get; }
    public string StorageEpochMarkerPath { get; }
    public IReadOnlyList<string> ResetDirectories { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }
}

internal sealed class NoOpLocalStateInitializer : IBiliLocalStateInitializer
{
    public int InitializeCount { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitializeCount++;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryBiliCredentialStore : IBiliCredentialStore
{
    public BiliCredentialSession? Session { get; private set; }
    public int SaveCount { get; private set; }
    public int DeleteCount { get; private set; }

    public InMemoryBiliCredentialStore(BiliCredentialSession? session = null)
    {
        Session = Clone(session);
    }

    public Task InitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task SaveSessionAsync(
        BiliCredentialSession session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Session = Clone(session);
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<BiliCredentialSession?> LoadSessionAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Clone(Session));
    }

    public Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Session = null;
        DeleteCount++;
        return Task.CompletedTask;
    }

    private static BiliCredentialSession? Clone(BiliCredentialSession? session)
        => session is null
            ? null
            : session with { Cookies = session.Cookies.ToList() };
}

internal sealed class StubBiliSessionApi : IBiliSessionApi
{
    public LoginValidationResult ValidationResult { get; set; } =
        new(LoginValidationStatus.Unavailable);
    public int ValidationCount { get; private set; }

    public Task<LoginValidationResult> CheckLoginAsync(
        string cookieHeader,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidationCount++;
        return Task.FromResult(ValidationResult);
    }

    public Task<bool> ExitLoginAsync(
        string cookieHeader,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}

/// <summary>
/// 线程安全的内存任务仓储。测试通过它观察持久化调用顺序，且不会访问真实 SQLite 或 AppData。
/// </summary>
internal sealed class InMemoryDownloadTaskRepository : IDownloadTaskRepository
{
    private readonly object _gate = new();
    private readonly List<DownloadTaskRecord> _tasks = [];

    public int InitializeCount { get; private set; }
    public int MaxConcurrentCalls { get; private set; }
    public Exception? InitializeException { get; set; }
    public Exception? InsertException { get; set; }

    public List<string> CallLog { get; } = [];

    public IReadOnlyList<DownloadTaskRecord> Tasks
    {
        get
        {
            lock (_gate)
            {
                return _tasks.ToArray();
            }
        }
    }

    public void Seed(params DownloadTaskRecord[] tasks)
    {
        lock (_gate)
        {
            _tasks.AddRange(tasks);
        }
    }

    public Task InitAsync()
    {
        if (InitializeException is not null)
        {
            return Task.FromException(InitializeException);
        }

        lock (_gate)
        {
            InitializeCount++;
            CallLog.Add("repository:init");
        }

        return Task.CompletedTask;
    }

    public Task InsertBatchAsync(List<DownloadTaskRecord> records)
    {
        if (InsertException is not null)
        {
            return Task.FromException(InsertException);
        }

        lock (_gate)
        {
            CallLog.Add("repository:insert");
            foreach (var record in records)
            {
                _tasks.RemoveAll(x => x.TaskId == record.TaskId);
                _tasks.Add(record);
            }
        }

        return Task.CompletedTask;
    }

    public Task<List<DownloadTaskRecord>> GetAllAsync()
    {
        lock (_gate)
        {
            return Task.FromResult(_tasks.ToList());
        }
    }

    public Task<List<DownloadTaskRecord>> GetByDocumentIdAsync(string documentId)
    {
        lock (_gate)
        {
            return Task.FromResult(_tasks.Where(x => x.DocumentId == documentId).ToList());
        }
    }

    public Task<List<DownloadTaskRecord>> GetIncompleteAsync()
    {
        lock (_gate)
        {
            return Task.FromResult(_tasks.Where(x => x.Status != "done").ToList());
        }
    }

    public Task UpdateProgressAsync(string taskId, double progress, string status, string? errorMessage = null)
    {
        lock (_gate)
        {
            var task = Find(taskId);
            task.Progress = progress;
            task.Status = status;
            task.ErrorMessage = errorMessage;
            task.LastUpdatedAt = DateTime.Now;
            CallLog.Add($"repository:status:{status}");
        }

        return Task.CompletedTask;
    }

    public Task UpdateStageProgressAsync(
        string taskId,
        double progress,
        string status,
        double videoProgress,
        double audioProgress,
        double mergeProgress,
        string speedText)
    {
        lock (_gate)
        {
            var task = Find(taskId);
            task.Progress = progress;
            task.Status = status;
            task.VideoProgress = videoProgress;
            task.AudioProgress = audioProgress;
            task.MergeProgress = mergeProgress;
            task.SpeedText = speedText;
            task.LastUpdatedAt = DateTime.Now;
            CallLog.Add($"repository:stage:{status}");
        }

        return Task.CompletedTask;
    }

    public Task UpdateBytesAsync(string taskId, long videoBytes, long audioBytes)
    {
        lock (_gate)
        {
            var task = Find(taskId);
            task.VideoBytesDownloaded = videoBytes;
            task.AudioBytesDownloaded = audioBytes;
            task.LastUpdatedAt = DateTime.Now;
            CallLog.Add("repository:bytes");
        }

        return Task.CompletedTask;
    }

    public Task UpdateIntegrityAsync(
        string taskId,
        long expectedVideoBytes,
        long expectedAudioBytes,
        bool videoIntegrityPassed,
        bool audioIntegrityPassed,
        DateTime lastUpdatedAt)
    {
        lock (_gate)
        {
            var task = Find(taskId);
            task.ExpectedVideoBytes = expectedVideoBytes;
            task.ExpectedAudioBytes = expectedAudioBytes;
            task.VideoIntegrityPassed = videoIntegrityPassed;
            task.AudioIntegrityPassed = audioIntegrityPassed;
            task.LastUpdatedAt = lastUpdatedAt;
        }

        return Task.CompletedTask;
    }

    public Task MarkCompletedAsync(
        string taskId,
        string outputFilePath,
        string? extrasResultSummary,
        DateTime lastUpdatedAt)
    {
        lock (_gate)
        {
            var task = Find(taskId);
            task.Progress = 100;
            task.Status = "done";
            task.VideoProgress = 100;
            task.AudioProgress = 100;
            task.MergeProgress = 100;
            task.SpeedText = "";
            task.OutputFilePath = outputFilePath;
            task.ExtrasResultSummary = extrasResultSummary;
            task.ErrorMessage = null;
            task.ErrorType = null;
            task.IsRetryable = false;
            task.LastUpdatedAt = lastUpdatedAt;
            CallLog.Add("repository:stage:done");
        }

        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(
        string taskId,
        double progress,
        string? errorMessage,
        string? errorType,
        bool isRetryable,
        DateTime lastUpdatedAt)
    {
        lock (_gate)
        {
            var task = Find(taskId);
            task.Progress = progress;
            task.Status = "failed";
            task.ErrorMessage = errorMessage;
            task.ErrorType = errorType;
            task.IsRetryable = isRetryable;
            task.LastUpdatedAt = lastUpdatedAt;
            CallLog.Add("repository:status:failed");
        }

        return Task.CompletedTask;
    }

    public Task UpdateTempDirectoryAsync(string taskId, string tempDirectory)
    {
        lock (_gate)
        {
            var task = Find(taskId);
            task.TempDirectory = tempDirectory;
            task.LastUpdatedAt = DateTime.Now;
        }

        return Task.CompletedTask;
    }

    public Task DeleteByIdAsync(string taskId)
    {
        lock (_gate)
        {
            _tasks.RemoveAll(x => x.TaskId == taskId);
        }

        return Task.CompletedTask;
    }

    public Task DeleteByIdsAsync(IEnumerable<string> taskIds)
    {
        var ids = taskIds.ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            _tasks.RemoveAll(x => ids.Contains(x.TaskId));
        }

        return Task.CompletedTask;
    }

    public Task DeleteDoneAsync()
    {
        lock (_gate)
        {
            _tasks.RemoveAll(x => x.Status == "done");
        }

        return Task.CompletedTask;
    }

    public Task UpdateExtrasResultAsync(string taskId, string? extrasResultSummary)
    {
        lock (_gate)
        {
            var task = Find(taskId);
            task.ExtrasResultSummary = extrasResultSummary;
            task.LastUpdatedAt = DateTime.Now;
        }

        return Task.CompletedTask;
    }

    public Task PrepareVerifiedResumeAsync(
        string taskId,
        string outputFilePath,
        string outputPathKey,
        FileConflictPolicy conflictPolicy,
        long estimatedRequiredBytes)
    {
        lock (_gate)
        {
            var task = Find(taskId);
            task.OutputFilePath = outputFilePath;
            task.OutputPathKey = outputPathKey;
            task.ConflictPolicy = conflictPolicy;
            task.EstimatedRequiredBytes = estimatedRequiredBytes;
            task.Status = "pending";
            task.ErrorMessage = null;
            task.ErrorType = null;
        }
        return Task.CompletedTask;
    }

    private DownloadTaskRecord Find(string taskId)
        => _tasks.Single(x => x.TaskId == taskId);
}

/// <summary>
/// 可编程的假下载执行器。默认立即成功，也可以由测试注入阻塞、失败或取消行为。
/// </summary>
internal sealed class FakeDownloadTaskExecutor : IDownloadTaskExecutor
{
    private int _activeCount;

    public int ExecuteCount { get; private set; }
    public int MaxActiveCount { get; private set; }
    public List<DownloadTaskRecord> ExecutedTasks { get; } = [];

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Func<DownloadTaskRecord, CancellationToken, Task<DownloadExecutionResult>> Handler { get; set; }
        = (_, _) => Task.FromResult(new DownloadExecutionResult(null, null));

    public Action? OnExecute { get; set; }
    public Action<Action<DownloadProgressInfo>, Action<long, long>>? OnCallbacks { get; set; }

    public Task<DownloadExecutionResult> ExecuteAsync(
        DownloadTaskRecord task,
        Action<DownloadProgressInfo> onProgress,
        Action<long, long> onBytesChanged,
        CancellationToken cancellationToken)
    {
        ExecuteCount++;
        ExecutedTasks.Add(task);
        var active = Interlocked.Increment(ref _activeCount);
        MaxActiveCount = Math.Max(MaxActiveCount, active);
        OnExecute?.Invoke();
        OnCallbacks?.Invoke(onProgress, onBytesChanged);
        Started.TrySetResult();
        return ExecuteCoreAsync(task, cancellationToken);
    }

    private async Task<DownloadExecutionResult> ExecuteCoreAsync(
        DownloadTaskRecord task,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Handler(task, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCount);
        }
    }
}

internal sealed class FakeCredentialProvider : IBiliCredentialProvider
{
    /// <summary>可配置的登录态，默认 true 以兼容现有测试</summary>
    public bool IsLoggedIn { get; set; } = true;

    public string GetCookieHeader() => IsLoggedIn ? "SESSDATA=fake" : string.Empty;
}

internal sealed class NoOpDownloadProgressTracker : IDownloadProgressTracker
{
    public void OnProgressChanged(DownloadTaskRecord task, DownloadProgressInfo info)
    {
        task.Progress = info.OverallProgress;
    }

    public void OnBytesChanged(DownloadTaskRecord task, long videoBytes, long audioBytes)
    {
        task.VideoBytesDownloaded = videoBytes;
        task.AudioBytesDownloaded = audioBytes;
    }

    public void BroadcastStatusChanged(DownloadTaskRecord task)
    {
    }

    public void BroadcastProgress(DownloadTaskRecord task)
    {
    }

    public Task FlushAsync(string taskId) => Task.CompletedTask;

    public Task ShutdownAsync() => Task.CompletedTask;
}

/// <summary>
/// 记录 Flush/Broadcast 调用顺序的测试替身。
/// 用于验证"先 flush → 再写终态 → 再广播"的时序。
/// </summary>
internal sealed class RecordingProgressTracker : IDownloadProgressTracker
{
    private readonly object _gate = new();

    public List<string> CallLog { get; } = [];

    public void OnProgressChanged(DownloadTaskRecord task, DownloadProgressInfo info)
    {
        task.Progress = info.OverallProgress;
        lock (_gate) { CallLog.Add($"tracker:progress:{task.TaskId}"); }
    }

    public void OnBytesChanged(DownloadTaskRecord task, long videoBytes, long audioBytes)
    {
        task.VideoBytesDownloaded = videoBytes;
        task.AudioBytesDownloaded = audioBytes;
        lock (_gate) { CallLog.Add($"tracker:bytes:{task.TaskId}"); }
    }

    public void BroadcastStatusChanged(DownloadTaskRecord task)
    {
        lock (_gate) { CallLog.Add($"tracker:broadcast_status:{task.TaskId}"); }
    }

    public void BroadcastProgress(DownloadTaskRecord task)
    {
        lock (_gate) { CallLog.Add($"tracker:broadcast_progress:{task.TaskId}"); }
    }

    public Task FlushAsync(string taskId)
    {
        lock (_gate) { CallLog.Add($"tracker:flush:{taskId}"); }
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        lock (_gate) { CallLog.Add("tracker:shutdown"); }
        return Task.CompletedTask;
    }
}

/// <summary>
/// 每个测试实例独享的消息器，避免 WeakReferenceMessenger.Default 在并行测试间串扰。
/// </summary>
internal sealed class IsolatedMessengerService : IMessengerService
{
    private readonly IMessenger _messenger = new StrongReferenceMessenger();

    public IMessenger Messenger => _messenger;

    public void Send<TMessage>(TMessage message) where TMessage : class
        => _messenger.Send(message);

    public void Register<TReceiver, TMessage>(
        TReceiver receiver,
        MyAvaloniaManagementCommon.Message.MessageHandler<TReceiver, TMessage> handler)
        where TReceiver : class
        where TMessage : class
        => _messenger.Register<TReceiver, TMessage>(receiver, (target, message) => handler(target, message));

    public void Unregister<TMessage>(object receiver) where TMessage : class
        => _messenger.Unregister<TMessage>(receiver);

    public void UnregisterAll(object receiver)
        => _messenger.UnregisterAll(receiver);
}

internal sealed class FakeFfmpegService : IFfmpegService
{
    public bool? ReadyOverride { get; set; }
    public bool CreateOutputFile { get; set; }
    public string? CustomPath { get; set; }
    public string? ResolvedPath => ResolveFfmpegPath();
    public bool IsReady => ReadyOverride ?? ResolvedPath is not null;
    public List<(string Video, string Audio, string Output)> MergeCalls { get; } = [];

    public string? ResolveFfmpegPath()
        => !string.IsNullOrWhiteSpace(CustomPath) && File.Exists(CustomPath)
            ? CustomPath
            : null;

    public Task<bool> ValidatePathAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path));
    }

    public Task MergeAsync(
        string videoPath,
        string audioPath,
        string outputPath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        MergeCalls.Add((videoPath, audioPath, outputPath));
        if (CreateOutputFile)
        {
            File.WriteAllBytes(outputPath, [0x01]);
        }
        return Task.CompletedTask;
    }
}

internal sealed class FakeDownloadRuntime : IDownloadRuntime
{
    private DateTimeOffset _utcNow = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    public int DelayCount { get; private set; }
    public DateTimeOffset UtcNow
    {
        get
        {
            _utcNow = _utcNow.AddSeconds(1);
            return _utcNow;
        }
    }

    public Task DelayForRetryAsync(int failedAttempt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DelayCount++;
        return Task.CompletedTask;
    }
}

internal sealed record HttpRequestSnapshot(
    HttpMethod Method,
    Uri? Uri,
    IReadOnlyDictionary<string, string> Headers);

internal sealed class StubBiliHttpClientFactory : IBiliHttpClientFactory
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public StubBiliHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    public List<HttpRequestSnapshot> Requests { get; } = [];

    public HttpClient CreateMediaClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", HttpConstants.UserAgent);
        client.DefaultRequestHeaders.Add("Referer", HttpConstants.Referer);
        client.DefaultRequestHeaders.Add("Origin", HttpConstants.Origin);
        return client;
    }

    public HttpClient CreateCoverClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("User-Agent", HttpConstants.UserAgent);
        client.DefaultRequestHeaders.Add("Referer", HttpConstants.Referer);
        return client;
    }

    private HttpClient CreateClient()
        => new(new DelegateHttpHandler(request =>
        {
            Requests.Add(new HttpRequestSnapshot(
                request.Method,
                request.RequestUri,
                request.Headers.ToDictionary(
                    pair => pair.Key,
                    pair => string.Join(", ", pair.Value),
                    StringComparer.OrdinalIgnoreCase)));
            return _handler(request);
        }));

    private sealed class DelegateHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public DelegateHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_handler(request));
        }
    }
}

internal sealed class FakeFfmpegProcessFactory : IFfmpegProcessFactory
{
    public FakeFfmpegProcess Process { get; } = new();
    public ProcessStartInfo? StartInfo { get; private set; }

    public IFfmpegProcess Start(ProcessStartInfo startInfo)
    {
        StartInfo = startInfo;
        return Process;
    }
}

internal sealed class FakeFfmpegProcess : IFfmpegProcess
{
    public bool HasExited { get; set; }
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = "";
    public string StandardError { get; set; } = "";
    public bool BlockUntilCancelled { get; set; }
    public bool KillCalled { get; private set; }

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        if (BlockUntilCancelled)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        HasExited = true;
    }

    public Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StandardOutput);
    }

    public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StandardError);
    }

    public void Kill(bool entireProcessTree)
    {
        KillCalled = true;
        HasExited = true;
    }

    public void Dispose()
    {
    }
}

internal sealed class RecordingMessengerService : IMessengerService
{
    private readonly IMessenger _messenger = new StrongReferenceMessenger();

    public List<object> SentMessages { get; } = [];
    public bool ThrowOnSend { get; set; }

    public IMessenger Messenger => _messenger;

    public void Send<TMessage>(TMessage message) where TMessage : class
    {
        if (ThrowOnSend)
        {
            throw new InvalidOperationException("模拟消息发送失败");
        }

        SentMessages.Add(message);
        _messenger.Send(message);
    }

    public void Register<TReceiver, TMessage>(
        TReceiver receiver,
        MyAvaloniaManagementCommon.Message.MessageHandler<TReceiver, TMessage> handler)
        where TReceiver : class
        where TMessage : class
        => _messenger.Register<TReceiver, TMessage>(
            receiver,
            (target, message) => handler(target, message));

    public void Unregister<TMessage>(object receiver) where TMessage : class
        => _messenger.Unregister<TMessage>(receiver);

    public void UnregisterAll(object receiver)
        => _messenger.UnregisterAll(receiver);
}

internal sealed class InMemorySettingsRepository : ISettingsRepository
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public int InitializeCount { get; private set; }
    public List<(string Key, string Value)> Writes { get; } = [];
    public Exception? InitializeException { get; set; }

    public void Seed(string key, string value) => _values[key] = value;

    public Task InitAsync()
    {
        InitializeCount++;
        return InitializeException is null
            ? Task.CompletedTask
            : Task.FromException(InitializeException);
    }

    public Task<string?> GetSettingAsync(string key)
        => Task.FromResult(_values.GetValueOrDefault(key));

    public Task SetSettingAsync(string key, string value)
    {
        _values[key] = value;
        Writes.Add((key, value));
        return Task.CompletedTask;
    }
}

/// <summary>
/// G4: 确认服务测试替身。
/// 可配置返回值（模拟用户确认/取消），并记录调用次数和最后一条消息，
/// 供测试断言确认机制是否被正确触发。
/// </summary>
internal sealed class FakeConfirmationService : IConfirmationService
{
    /// <summary>配置返回结果（true=确认，false=取消）</summary>
    public bool Result { get; set; } = true;

    /// <summary>ConfirmAsync 被调用的次数</summary>
    public int CallCount { get; private set; }

    /// <summary>最后一次确认消息正文（供断言内容正确性）</summary>
    public string? LastMessage { get; private set; }

    /// <summary>最后一次确认标题</summary>
    public string? LastTitle { get; private set; }

    public Task<bool> ConfirmAsync(string title, string message)
    {
        CallCount++;
        LastTitle = title;
        LastMessage = message;
        return Task.FromResult(Result);
    }
}

internal static class AsyncTest
{
    public static async Task EventuallyAsync(
        Func<bool> condition,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("等待测试条件超时。");
            }

            await Task.Delay(10);
        }
    }
}
