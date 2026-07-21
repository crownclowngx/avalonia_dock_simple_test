using BiliDownloader.Models;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Persistence;
using CommunityToolkit.Mvvm.Messaging;
using MyAvaloniaManagementCommon.Message;

namespace BiliDownloader.Tests;

/// <summary>
/// 线程安全的内存任务仓储。测试通过它观察持久化调用顺序，且不会访问真实 SQLite 或 AppData。
/// </summary>
internal sealed class InMemoryDownloadTaskRepository : IDownloadTaskRepository
{
    private readonly object _gate = new();
    private readonly List<DownloadTaskRecord> _tasks = [];

    public int InitializeCount { get; private set; }

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
        lock (_gate)
        {
            InitializeCount++;
            CallLog.Add("repository:init");
        }

        return Task.CompletedTask;
    }

    public Task InsertBatchAsync(List<DownloadTaskRecord> records)
    {
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
    public int ExecuteCount { get; private set; }

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Func<DownloadTaskRecord, CancellationToken, Task<DownloadExecutionResult>> Handler { get; set; }
        = (_, _) => Task.FromResult(new DownloadExecutionResult(null, null));

    public Action? OnExecute { get; set; }

    public Task<DownloadExecutionResult> ExecuteAsync(
        DownloadTaskRecord task,
        Action<DownloadProgressInfo> onProgress,
        Action<long, long> onBytesChanged,
        CancellationToken cancellationToken)
    {
        ExecuteCount++;
        OnExecute?.Invoke();
        Started.TrySetResult();
        return Handler(task, cancellationToken);
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
