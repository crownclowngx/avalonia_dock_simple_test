using System.Collections.Concurrent;
using System.Threading.Channels;
using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.Services.Download;

internal enum ProgressWriteKind
{
    StageProgress,
    Bytes,
}

/// <summary>Compatibility request used by older tests and callers.</summary>
internal sealed record ProgressWriteRequest(
    string TaskId,
    long Version,
    ProgressWriteKind Kind,
    double Progress = 0,
    string Status = "",
    double VideoProgress = 0,
    double AudioProgress = 0,
    double MergeProgress = 0,
    string SpeedText = "",
    long VideoBytes = 0,
    long AudioBytes = 0);

/// <summary>
/// Single-reader persistence worker. Producers only replace the latest per-task
/// value, so queue growth is bounded by active task count while control commands
/// (flush and stop) can never be dropped.
/// </summary>
internal sealed class ProgressWriteChannel : IAsyncDisposable
{
    private static readonly IPluginLogger Log = PluginLog.For<ProgressWriteChannel>();
    private readonly IDownloadTaskRepository _repository;
    private readonly Channel<WorkerCommand> _commands = Channel.CreateUnbounded<WorkerCommand>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<string, TaskState> _states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _queuedSignals = new(StringComparer.Ordinal);
    private readonly object _shutdownLock = new();
    private readonly Task _consumerTask;
    private Task? _shutdownTask;

    public ProgressWriteChannel(IDownloadTaskRepository repository)
    {
        _repository = repository;
        _consumerTask = ConsumeLoopAsync();
    }

    public void Enqueue(TaskRuntimeSnapshot snapshot)
    {
        ThrowIfStopped();
        var state = _states.GetOrAdd(snapshot.TaskId, static _ => new TaskState());
        lock (state.Gate)
        {
            state.Snapshot = snapshot;
            state.Version++;
        }
        Signal(snapshot.TaskId);
    }

    public void Enqueue(ProgressWriteRequest request)
    {
        ThrowIfStopped();
        var state = _states.GetOrAdd(request.TaskId, static _ => new TaskState());
        lock (state.Gate)
        {
            if (request.Kind == ProgressWriteKind.StageProgress)
            {
                if (state.StageRequest is null || request.Version >= state.StageRequest.Version)
                    state.StageRequest = request;
            }
            else if (state.BytesRequest is null || request.Version >= state.BytesRequest.Version)
            {
                state.BytesRequest = request;
            }
            state.Version++;
        }
        Signal(request.TaskId);
    }

    public async Task FlushAsync(string taskId)
    {
        Task? shutdown;
        lock (_shutdownLock) shutdown = _shutdownTask;
        if (shutdown is not null)
        {
            await shutdown;
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new FlushCommand(taskId, completion));
        await completion.Task;
    }

    public void Remove(string taskId)
    {
        _states.TryRemove(taskId, out _);
        _queuedSignals.TryRemove(taskId, out _);
    }

    public Task ShutdownAsync()
    {
        lock (_shutdownLock)
            return _shutdownTask ??= ShutdownCoreAsync();
    }

    private async Task ShutdownCoreAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new StopCommand(completion));
        await completion.Task;
        _commands.Writer.TryComplete();
        await _consumerTask;
    }

    private void Signal(string taskId)
    {
        if (_queuedSignals.TryAdd(taskId, 0))
            _commands.Writer.TryWrite(new PersistCommand(taskId));
    }

    private async Task ConsumeLoopAsync()
    {
        await foreach (var command in _commands.Reader.ReadAllAsync())
        {
            switch (command)
            {
                case PersistCommand persist:
                    await ProcessSignalAsync(persist.TaskId);
                    break;
                case FlushCommand flush:
                    await CompleteFlushAsync(flush);
                    break;
                case StopCommand stop:
                    await CompleteStopAsync(stop);
                    return;
            }
        }
    }

    private async Task ProcessSignalAsync(string taskId)
    {
        var succeeded = false;
        try
        {
            await PersistUntilCurrentAsync(taskId, persistOneVersionOnly: true);
            succeeded = true;
        }
        catch (Exception ex)
        {
            // Keep the state dirty. A later progress report or an explicit flush retries it.
            Log.Warn($"进度后台写入失败 (Task={taskId}): {ex.Message}");
        }
        finally
        {
            _queuedSignals.TryRemove(taskId, out _);
        }

        if (succeeded && IsDirty(taskId)) Signal(taskId);
    }

    private async Task CompleteFlushAsync(FlushCommand command)
    {
        try
        {
            await PersistUntilCurrentAsync(command.TaskId, persistOneVersionOnly: false);
            command.Completion.TrySetResult();
        }
        catch (Exception ex)
        {
            command.Completion.TrySetException(ex);
        }
    }

    private async Task CompleteStopAsync(StopCommand command)
    {
        try
        {
            foreach (var taskId in _states.Keys)
                await PersistUntilCurrentAsync(taskId, persistOneVersionOnly: false);
            command.Completion.TrySetResult();
        }
        catch (Exception ex)
        {
            command.Completion.TrySetException(ex);
        }
    }

    private async Task PersistUntilCurrentAsync(string taskId, bool persistOneVersionOnly)
    {
        if (!_states.TryGetValue(taskId, out var state)) return;

        do
        {
            TaskRuntimeSnapshot? snapshot;
            ProgressWriteRequest? stage;
            ProgressWriteRequest? bytes;
            long version;
            lock (state.Gate)
            {
                if (state.PersistedVersion >= state.Version) return;
                snapshot = state.Snapshot;
                stage = state.StageRequest;
                bytes = state.BytesRequest;
                version = state.Version;
            }

            await ExecuteWithRetryAsync(async () =>
            {
                if (snapshot is not null)
                {
                    await _repository.UpdateRuntimeSnapshotAsync(snapshot);
                    return;
                }

                if (stage is not null)
                {
                    await _repository.UpdateStageProgressAsync(
                        stage.TaskId, stage.Progress, stage.Status,
                        stage.VideoProgress, stage.AudioProgress,
                        stage.MergeProgress, stage.SpeedText);
                }
                if (bytes is not null)
                    await _repository.UpdateBytesAsync(bytes.TaskId, bytes.VideoBytes, bytes.AudioBytes);
            });

            lock (state.Gate)
                state.PersistedVersion = Math.Max(state.PersistedVersion, version);
        }
        while (!persistOneVersionOnly && IsDirty(taskId));
    }

    private static async Task ExecuteWithRetryAsync(Func<Task> action)
    {
        var delays = new[] { 50, 150, 450 };
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (SqliteException ex) when ((ex.SqliteErrorCode is 5 or 6) && attempt < delays.Length)
            {
                await Task.Delay(delays[attempt]);
            }
        }
    }

    private bool IsDirty(string taskId)
    {
        if (!_states.TryGetValue(taskId, out var state)) return false;
        lock (state.Gate) return state.PersistedVersion < state.Version;
    }

    private void ThrowIfStopped()
    {
        lock (_shutdownLock)
            if (_shutdownTask is not null)
                throw new InvalidOperationException("进度持久化工作器已经关闭。");
    }

    public async ValueTask DisposeAsync() => await ShutdownAsync();

    private abstract record WorkerCommand;
    private sealed record PersistCommand(string TaskId) : WorkerCommand;
    private sealed record FlushCommand(string TaskId, TaskCompletionSource Completion) : WorkerCommand;
    private sealed record StopCommand(TaskCompletionSource Completion) : WorkerCommand;

    private sealed class TaskState
    {
        public object Gate { get; } = new();
        public long Version { get; set; }
        public long PersistedVersion { get; set; }
        public TaskRuntimeSnapshot? Snapshot { get; set; }
        public ProgressWriteRequest? StageRequest { get; set; }
        public ProgressWriteRequest? BytesRequest { get; set; }
    }
}
