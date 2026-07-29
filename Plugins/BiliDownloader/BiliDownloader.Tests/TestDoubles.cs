using BiliDownloader.Models;
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
            CallLog.Add("repository:bytes");
        }

        return Task.CompletedTask;
    }

    public Task UpdateTempDirectoryAsync(string taskId, string tempDirectory)
    {
        lock (_gate)
        {
            Find(taskId).TempDirectory = tempDirectory;
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
            Find(taskId).ExtrasResultSummary = extrasResultSummary;
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
    public string GetCookieHeader() => string.Empty;

    public bool IsLoggedIn => false;
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
