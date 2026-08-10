using System.Collections.Concurrent;
using System.Diagnostics;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 下载带宽许可边界。调用方必须在读取网络流之前申请本次计划读取的字节数；
/// 实现不持有网络流或文件流，因此限速策略与 HTTP/文件生命周期保持解耦。
/// </summary>
public interface IBandwidthLimiter
{
    ValueTask AcquireAsync(int bytes, string taskId, CancellationToken cancellationToken);
}

public sealed class UnlimitedBandwidthLimiter : IBandwidthLimiter
{
    public ValueTask AcquireAsync(int bytes, string taskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

/// <summary>统一的限速值规则。持久化使用 bytes/s，UI 只负责 KiB/s 换算。</summary>
public static class BandwidthLimitPolicy
{
    public const long MinimumNonZeroBytesPerSecond = 64L * 1024;
    public const long DefaultEditorBytesPerSecond = 1024L * 1024;
    public const int ReadQuantumBytes = 8 * 1024;

    public static long Validate(long bytesPerSecond, string? parameterName = null)
    {
        if (bytesPerSecond < 0)
            throw new ArgumentOutOfRangeException(parameterName ?? nameof(bytesPerSecond), "限速不能为负数。");
        if (bytesPerSecond > 0 && bytesPerSecond < MinimumNonZeroBytesPerSecond)
            throw new ArgumentOutOfRangeException(parameterName ?? nameof(bytesPerSecond),
                $"非零限速不能低于 {MinimumNonZeroBytesPerSecond / 1024} KiB/s。");
        return bytesPerSecond;
    }

    public static long FromKibibytesPerSecond(long kibibytesPerSecond)
    {
        if (kibibytesPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(kibibytesPerSecond), "限速不能为负数。");
        if (kibibytesPerSecond is > 0 and < 64)
            throw new ArgumentOutOfRangeException(nameof(kibibytesPerSecond), "非零限速不能低于 64 KiB/s。");
        try
        {
            return Validate(checked(kibibytesPerSecond * 1024), nameof(kibibytesPerSecond));
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(kibibytesPerSecond), "限速值过大。");
        }
    }

    public static long ToKibibytesPerSecond(long bytesPerSecond)
        => Validate(bytesPerSecond) / 1024;
}

/// <summary>令牌桶使用的单调时间边界，使系统时间调整不会制造额度。</summary>
public interface IBandwidthClock
{
    long GetTimestamp();
    TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp);
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemBandwidthClock : IBandwidthClock
{
    public long GetTimestamp() => Stopwatch.GetTimestamp();
    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
        => Stopwatch.GetElapsedTime(startingTimestamp, endingTimestamp);
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}

public sealed record BandwidthLimiterStatistics(
    long GrantedBytes,
    long GrantedRequests,
    long CancelledRequests,
    TimeSpan TotalWait,
    TimeSpan MaximumWait);

internal interface IAdjustableBandwidthLimiter : IBandwidthLimiter, IDisposable
{
    long LimitBytesPerSecond { get; }
    void UpdateLimit(long bytesPerSecond);
    BandwidthLimiterStatistics GetStatistics();
}

/// <summary>
/// 可动态调整的公平令牌桶。队列以 taskId 分组并轮询；同一任务即使有多个分块连接，
/// 每轮也只会获得一个读取量子，避免连接数较多的任务淹没其他任务。
/// </summary>
internal sealed class FairTokenBucketBandwidthLimiter : IAdjustableBandwidthLimiter
{
    private sealed class PendingRequest
    {
        public PendingRequest(int bytes, string taskId, long createdAt)
        {
            Bytes = bytes;
            TaskId = taskId;
            CreatedAt = createdAt;
            Completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public int Bytes { get; }
        public string TaskId { get; }
        public long CreatedAt { get; }
        public TaskCompletionSource Completion { get; }
        public CancellationTokenRegistration CancellationRegistration { get; set; }
        public bool IsCancelled { get; set; }
    }

    private readonly object _sync = new();
    private readonly IBandwidthClock _clock;
    private readonly Dictionary<string, Queue<PendingRequest>> _queues = new(StringComparer.Ordinal);
    private readonly Queue<string> _roundRobin = new();
    private readonly HashSet<string> _scheduledTasks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _wakeSignal = new(0, int.MaxValue);
    private readonly CancellationTokenSource _disposeCts = new();
    private long _limitBytesPerSecond;
    private long _lastTimestamp;
    private double _tokens;
    private bool _pumpRunning;
    private bool _disposed;
    private long _grantedBytes;
    private long _grantedRequests;
    private long _cancelledRequests;
    private long _totalWaitTicks;
    private long _maximumWaitTicks;

    public FairTokenBucketBandwidthLimiter(long bytesPerSecond, IBandwidthClock clock)
    {
        _clock = clock;
        _limitBytesPerSecond = BandwidthLimitPolicy.Validate(bytesPerSecond);
        _lastTimestamp = clock.GetTimestamp();
        _tokens = bytesPerSecond == 0
            ? 0
            : Math.Min(BandwidthLimitPolicy.ReadQuantumBytes, GetCapacity(bytesPerSecond));
    }

    public long LimitBytesPerSecond => Interlocked.Read(ref _limitBytesPerSecond);

    public ValueTask AcquireAsync(int bytes, string taskId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (bytes <= 0) return ValueTask.CompletedTask;
        if (bytes > BandwidthLimitPolicy.ReadQuantumBytes)
            throw new ArgumentOutOfRangeException(nameof(bytes),
                $"单次限速许可不能超过 {BandwidthLimitPolicy.ReadQuantumBytes} 字节。");
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("限速许可必须具有任务 ID。", nameof(taskId));
        cancellationToken.ThrowIfCancellationRequested();

        PendingRequest? request = null;
        var startPump = false;
        lock (_sync)
        {
            ThrowIfDisposed();
            RefillTokensLocked();
            if (_limitBytesPerSecond == 0)
            {
                RecordGrantLocked(bytes, TimeSpan.Zero);
                return ValueTask.CompletedTask;
            }

            request = new PendingRequest(bytes, taskId, _clock.GetTimestamp());
            // 注册必须在请求对 pump 可见之前完成。否则初始 token 可能让 pump 立即完成请求，
            // 随后才写入的 CancellationTokenRegistration 将永远没有释放机会。
            request.CancellationRegistration = cancellationToken.Register(static state =>
            {
                var (limiter, pending) = ((FairTokenBucketBandwidthLimiter, PendingRequest))state!;
                limiter.CancelRequest(pending);
            }, (this, request));
            if (!_queues.TryGetValue(taskId, out var queue))
            {
                queue = new Queue<PendingRequest>();
                _queues.Add(taskId, queue);
            }
            queue.Enqueue(request);
            if (_scheduledTasks.Add(taskId)) _roundRobin.Enqueue(taskId);
            if (!_pumpRunning)
            {
                _pumpRunning = true;
                startPump = true;
            }
        }

        if (startPump) _ = PumpAsync();
        SignalPump();
        return new ValueTask(request.Completion.Task);
    }

    public void UpdateLimit(long bytesPerSecond)
    {
        BandwidthLimitPolicy.Validate(bytesPerSecond);
        lock (_sync)
        {
            ThrowIfDisposed();
            RefillTokensLocked();
            var previous = _limitBytesPerSecond;
            _limitBytesPerSecond = bytesPerSecond;
            _tokens = bytesPerSecond == 0
                ? 0
                : previous == 0
                    ? Math.Min(BandwidthLimitPolicy.ReadQuantumBytes, GetCapacity(bytesPerSecond))
                    : Math.Min(_tokens, GetCapacity(bytesPerSecond));
            _lastTimestamp = _clock.GetTimestamp();
            if (_roundRobin.Count > 0 && !_pumpRunning)
            {
                _pumpRunning = true;
                _ = PumpAsync();
            }
        }
        SignalPump();
    }

    public BandwidthLimiterStatistics GetStatistics() => new(
        Interlocked.Read(ref _grantedBytes),
        Interlocked.Read(ref _grantedRequests),
        Interlocked.Read(ref _cancelledRequests),
        TimeSpan.FromTicks(Interlocked.Read(ref _totalWaitTicks)),
        TimeSpan.FromTicks(Interlocked.Read(ref _maximumWaitTicks)));

    private async Task PumpAsync()
    {
        try
        {
            while (!_disposeCts.IsCancellationRequested)
            {
                List<PendingRequest>? completed = null;
                TimeSpan? delay = null;
                lock (_sync)
                {
                    RefillTokensLocked();
                    RemoveCancelledHeadsLocked();
                    if (_roundRobin.Count == 0)
                    {
                        _pumpRunning = false;
                        return;
                    }

                    if (_limitBytesPerSecond == 0)
                    {
                        completed = DrainAllLocked();
                    }
                    else
                    {
                        var taskId = _roundRobin.Peek();
                        var request = _queues[taskId].Peek();
                        if (_tokens >= request.Bytes)
                        {
                            _tokens -= request.Bytes;
                            completed = [DequeueCurrentLocked(taskId)];
                        }
                        else
                        {
                            var missing = request.Bytes - _tokens;
                            delay = TimeSpan.FromSeconds(missing / _limitBytesPerSecond);
                            if (delay < TimeSpan.FromMilliseconds(1)) delay = TimeSpan.FromMilliseconds(1);
                        }
                    }
                }

                if (completed is not null)
                {
                    foreach (var request in completed) CompleteRequest(request);
                    continue;
                }

                // 每轮都取消未胜出的等待。否则频繁热更新会遗留一批仍在计时或等待信号的任务，
                // 它们会在之后错误消费唤醒量，既增加内存压力，也可能把真正需要立即重算额度的请求拖慢。
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
                var delayTask = _clock.DelayAsync(delay!.Value, waitCts.Token);
                var signalTask = _wakeSignal.WaitAsync(waitCts.Token);
                await Task.WhenAny(delayTask, signalTask).ConfigureAwait(false);
                await waitCts.CancelAsync().ConfigureAwait(false);
                await ObserveCancellationAsync(delayTask).ConfigureAwait(false);
                await ObserveCancellationAsync(signalTask).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            // Dispose 会在 finally 中统一唤醒剩余等待者。
        }
        finally
        {
            List<PendingRequest> abandoned;
            lock (_sync)
            {
                abandoned = DrainAllLocked();
                _pumpRunning = false;
            }
            foreach (var request in abandoned)
            {
                request.CancellationRegistration.Dispose();
                request.Completion.TrySetCanceled(_disposeCts.Token);
            }
        }
    }

    private void CancelRequest(PendingRequest request)
    {
        lock (_sync)
        {
            if (request.IsCancelled || request.Completion.Task.IsCompleted) return;
            request.IsCancelled = true;
            Interlocked.Increment(ref _cancelledRequests);
        }
        request.Completion.TrySetCanceled();
        SignalPump();
    }

    private void CompleteRequest(PendingRequest request)
    {
        request.CancellationRegistration.Dispose();
        if (request.IsCancelled) return;
        var waited = _clock.GetElapsedTime(request.CreatedAt, _clock.GetTimestamp());
        RecordGrant(request.Bytes, waited);
        request.Completion.TrySetResult();
    }

    private void RefillTokensLocked()
    {
        var now = _clock.GetTimestamp();
        var elapsed = _clock.GetElapsedTime(_lastTimestamp, now);
        _lastTimestamp = now;
        if (_limitBytesPerSecond == 0 || elapsed <= TimeSpan.Zero) return;
        // 即便测试时钟或平台实现异常，也不允许一次跳变积累无限额度。
        var seconds = Math.Min(1, elapsed.TotalSeconds);
        _tokens = Math.Min(GetCapacity(_limitBytesPerSecond),
            _tokens + seconds * _limitBytesPerSecond);
    }

    private PendingRequest DequeueCurrentLocked(string taskId)
    {
        _roundRobin.Dequeue();
        _scheduledTasks.Remove(taskId);
        var queue = _queues[taskId];
        var request = queue.Dequeue();
        if (queue.Count == 0) _queues.Remove(taskId);
        else
        {
            _roundRobin.Enqueue(taskId);
            _scheduledTasks.Add(taskId);
        }
        return request;
    }

    private void RemoveCancelledHeadsLocked()
    {
        var passes = _roundRobin.Count;
        while (passes-- > 0 && _roundRobin.Count > 0)
        {
            var taskId = _roundRobin.Dequeue();
            _scheduledTasks.Remove(taskId);
            var queue = _queues[taskId];
            while (queue.Count > 0 && queue.Peek().IsCancelled)
            {
                var cancelled = queue.Dequeue();
                cancelled.CancellationRegistration.Dispose();
            }
            if (queue.Count == 0) _queues.Remove(taskId);
            else
            {
                _roundRobin.Enqueue(taskId);
                _scheduledTasks.Add(taskId);
            }
        }
    }

    private List<PendingRequest> DrainAllLocked()
    {
        var result = new List<PendingRequest>();
        while (_roundRobin.Count > 0)
        {
            var taskId = _roundRobin.Dequeue();
            _scheduledTasks.Remove(taskId);
            if (!_queues.Remove(taskId, out var queue)) continue;
            while (queue.Count > 0)
            {
                var request = queue.Dequeue();
                if (!request.IsCancelled) result.Add(request);
                else request.CancellationRegistration.Dispose();
            }
        }
        return result;
    }

    private static double GetCapacity(long limitBytesPerSecond)
        => Math.Max(BandwidthLimitPolicy.ReadQuantumBytes, limitBytesPerSecond / 4d);

    private void RecordGrant(int bytes, TimeSpan wait)
    {
        Interlocked.Add(ref _grantedBytes, bytes);
        Interlocked.Increment(ref _grantedRequests);
        Interlocked.Add(ref _totalWaitTicks, wait.Ticks);
        UpdateMaximum(ref _maximumWaitTicks, wait.Ticks);
    }

    private void RecordGrantLocked(int bytes, TimeSpan wait) => RecordGrant(bytes, wait);

    private static void UpdateMaximum(ref long location, long candidate)
    {
        var current = Interlocked.Read(ref location);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref location, candidate, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private void SignalPump()
    {
        try { _wakeSignal.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    private static async Task ObserveCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消的是本轮未胜出的内部等待；调用方取消由 PendingRequest 自己传播。
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _disposeCts.Cancel();
        SignalPump();
        // Pump 可能仍在 finally 中访问这两个同步原语。此处只发出取消，不抢先 Dispose，
        // 避免释放与后台清理竞态；它们随后由 GC 回收，且每个 limiter 仅各持有一个实例。
    }
}

public interface IGlobalBandwidthLimitController
{
    long LimitBytesPerSecond { get; }
    void UpdateLimit(long bytesPerSecond, string reason);
}

/// <summary>组合限速器所需的全局读取许可端口；控制端口与传输端口保持接口隔离。</summary>
public interface IGlobalBandwidthLimiter : IBandwidthLimiter
{
}

public sealed class GlobalBandwidthLimiter : IGlobalBandwidthLimiter, IGlobalBandwidthLimitController, IDisposable
{
    private readonly FairTokenBucketBandwidthLimiter _inner;
    private readonly IPluginLogger _log;
    private int _disposed;

    public GlobalBandwidthLimiter(IBandwidthClock clock, IPluginLogger? logger = null)
    {
        _inner = new FairTokenBucketBandwidthLimiter(0, clock);
        _log = logger ?? PluginLog.For<GlobalBandwidthLimiter>();
    }

    public long LimitBytesPerSecond => _inner.LimitBytesPerSecond;

    public ValueTask AcquireAsync(int bytes, string taskId, CancellationToken cancellationToken)
        => _inner.AcquireAsync(bytes, taskId, cancellationToken);

    public void UpdateLimit(long bytesPerSecond, string reason)
    {
        var previous = LimitBytesPerSecond;
        _inner.UpdateLimit(bytesPerSecond);
        _log.Info($"全局媒体限速已更新；原因={reason}，旧值={previous} B/s，新值={bytesPerSecond} B/s。"
            + " 该变更只控制后续网络读取，不重启任务或修改断点。");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var stats = _inner.GetStatistics();
        _inner.Dispose();
        _log.Info($"全局主媒体限速器已释放；最终配置={LimitBytesPerSecond} B/s，"
            + $"许可字节={stats.GrantedBytes}，许可次数={stats.GrantedRequests}，"
            + $"取消等待={stats.CancelledRequests}，累计等待={stats.TotalWait.TotalMilliseconds:F0}ms，"
            + $"最大等待={stats.MaximumWait.TotalMilliseconds:F0}ms。"
            + " 该聚合日志用于判断吞吐和关闭行为，不记录逐次读取或任何媒体 URL。");
    }
}

public interface ITaskBandwidthLimitManager : IBandwidthLimiter
{
    IDisposable Activate(string taskId, long bytesPerSecond);
    bool TryUpdateLimit(string taskId, long bytesPerSecond, string reason);
}

public sealed class TaskBandwidthLimitManager : ITaskBandwidthLimitManager, IDisposable
{
    private sealed record Entry(FairTokenBucketBandwidthLimiter Limiter, long StartedAt);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly IBandwidthClock _clock;
    private readonly IPluginLogger _log;
    private readonly object _lifecycleSync = new();
    private bool _disposed;

    public TaskBandwidthLimitManager(IBandwidthClock clock, IPluginLogger? logger = null)
    {
        _clock = clock;
        _log = logger ?? PluginLog.For<TaskBandwidthLimitManager>();
    }

    public IDisposable Activate(string taskId, long bytesPerSecond)
    {
        BandwidthLimitPolicy.Validate(bytesPerSecond);
        Entry entry;
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            entry = new Entry(new FairTokenBucketBandwidthLimiter(bytesPerSecond, _clock), _clock.GetTimestamp());
            if (!_entries.TryAdd(taskId, entry))
            {
                entry.Limiter.Dispose();
                throw new InvalidOperationException($"任务 {taskId} 已存在活动限速上下文。");
            }
        }
        _log.Info($"任务媒体限速器已激活；任务={taskId}，配置={bytesPerSecond} B/s。"
            + " 视频与音频的所有分块连接将共享该额度。");
        return new Activation(this, taskId, entry);
    }

    public ValueTask AcquireAsync(int bytes, string taskId, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(taskId, out var entry))
            return entry.Limiter.AcquireAsync(bytes, taskId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public bool TryUpdateLimit(string taskId, long bytesPerSecond, string reason)
    {
        BandwidthLimitPolicy.Validate(bytesPerSecond);
        long previous;
        lock (_lifecycleSync)
        {
            if (!_entries.TryGetValue(taskId, out var entry)) return false;
            previous = entry.Limiter.LimitBytesPerSecond;
            entry.Limiter.UpdateLimit(bytesPerSecond);
        }
        _log.Info($"活动任务媒体限速已热更新；任务={taskId}，原因={reason}，"
            + $"旧值={previous} B/s，新值={bytesPerSecond} B/s。断点和任务状态保持不变。");
        return true;
    }

    private void Deactivate(string taskId, Entry expected)
    {
        BandwidthLimiterStatistics stats;
        lock (_lifecycleSync)
        {
            if (!_entries.TryRemove(new KeyValuePair<string, Entry>(taskId, expected))) return;
            stats = expected.Limiter.GetStatistics();
            expected.Limiter.Dispose();
        }
        var lifetime = _clock.GetElapsedTime(expected.StartedAt, _clock.GetTimestamp());
        _log.Info($"任务媒体限速器已释放；任务={taskId}，生存期={lifetime.TotalSeconds:F2}s，"
            + $"许可字节={stats.GrantedBytes}，许可次数={stats.GrantedRequests}，"
            + $"取消等待={stats.CancelledRequests}，累计等待={stats.TotalWait.TotalMilliseconds:F0}ms，"
            + $"最大等待={stats.MaximumWait.TotalMilliseconds:F0}ms。");
    }

    private sealed class Activation(TaskBandwidthLimitManager owner, string taskId, Entry entry) : IDisposable
    {
        private TaskBandwidthLimitManager? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Deactivate(taskId, entry);
    }

    public void Dispose()
    {
        KeyValuePair<string, Entry>[] entries;
        lock (_lifecycleSync)
        {
            if (_disposed) return;
            _disposed = true;
            entries = _entries.ToArray();
        }
        foreach (var pair in entries) Deactivate(pair.Key, pair.Value);
    }
}

/// <summary>把任务级和全局额度组合为下载器唯一依赖的端口。</summary>
public sealed class CompositeBandwidthLimiter(
    ITaskBandwidthLimitManager taskLimiter,
    IGlobalBandwidthLimiter globalLimiter) : IBandwidthLimiter
{
    public async ValueTask AcquireAsync(int bytes, string taskId, CancellationToken cancellationToken)
    {
        // 先等待更严格的任务桶，避免已经受单任务限制的请求提前占用共享全局令牌。
        await taskLimiter.AcquireAsync(bytes, taskId, cancellationToken).ConfigureAwait(false);
        await globalLimiter.AcquireAsync(bytes, taskId, cancellationToken).ConfigureAwait(false);
    }
}
