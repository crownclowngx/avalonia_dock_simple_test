using System.Threading.Channels;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>只服务生命周期测试的手动时钟；通过定时器登记屏障推进时间，不用真实 30/10 秒睡眠。</summary>
internal sealed class ManualLifecycleTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly Channel<TimeSpan> _created = Channel.CreateUnbounded<TimeSpan>();
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (_gate) return _ticks; }
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    internal async Task WaitForTimerAsync(TimeSpan due)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await _created.Reader.ReadAsync(deadline.Token) != due) { }
    }

    internal void Advance(TimeSpan elapsed)
    {
        ManualTimer[] ready;
        lock (_gate)
        {
            _ticks += elapsed.Ticks;
            ready = _timers.Where(timer => timer.Due <= _ticks).ToArray();
            foreach (var timer in ready) _timers.Remove(timer);
        }
        foreach (var timer in ready) timer.Fire();
    }

    private sealed class ManualTimer(ManualLifecycleTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        internal long Due { get; private set; }
        private bool _disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (_disposed) return false;
                owner._timers.Remove(this);
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    Due = owner._ticks + dueTime.Ticks;
                    owner._timers.Add(this);
                    owner._created.Writer.TryWrite(dueTime);
                }
                return true;
            }
        }
        internal void Fire() { if (!_disposed) callback(state); }
        public void Dispose() { lock (owner._gate) { _disposed = true; owner._timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
