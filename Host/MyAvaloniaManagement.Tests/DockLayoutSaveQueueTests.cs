using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Tests;

/// <summary>控制时间与写入阻塞验证外部可见提交顺序，不以真实等待时间判断防抖正确性。</summary>
public sealed class DockLayoutSaveQueueTests
{
    [Fact]
    public async Task 连续调整只在最后静默期提交一次且Flush无需等待()
    {
        var clock = new Clock();
        var writes = new List<double>();
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var queue = new DockLayoutSaveQueue(snapshot => { writes.Add(snapshot.MainWindow.Bounds.X); written.TrySetResult(); return true; }, _ => { }, clock);
        for (var index = 0; index < 100; index++) queue.Submit(Snapshot(index));
        clock.Advance(749);
        Assert.Empty(writes);
        clock.Advance(1);
        await written.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await queue.FlushAsync();
        Assert.Equal([99d], writes);
        queue.Submit(Snapshot(120));
        await queue.FlushAsync();
        Assert.Equal([99d, 120d], writes);
    }

    [Fact]
    public async Task 旧写入未结束时新修订等待且最终文件由新修订决定()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<double>();
        using var queue = new DockLayoutSaveQueue(snapshot =>
        {
            if (snapshot.MainWindow.Bounds.X == 1) { entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); }
            writes.Add(snapshot.MainWindow.Bounds.X);
            return true;
        }, _ => { });
        queue.Submit(Snapshot(1));
        var first = queue.FlushAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        queue.Submit(Snapshot(2));
        var second = queue.FlushAsync();
        Assert.False(second.IsCompleted);
        release.Set();
        await Task.WhenAll(first, second);
        Assert.Equal([1d, 2d], writes);
        await queue.FlushAsync();
        Assert.Equal(2, writes.Count);
    }

    [Fact]
    public async Task 失败不标记已提交且重试成功清除错误()
    {
        var fail = true;
        var results = new List<bool>();
        var calls = 0;
        using var queue = new DockLayoutSaveQueue(_ => { calls++; if (fail) throw new IOException("private-path"); return true; }, error => results.Add(error is null));
        queue.Submit(Snapshot(1));
        await queue.FlushAsync();
        Assert.Equal([false], results);
        fail = false;
        await queue.FlushAsync();
        Assert.Equal([false, true], results);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task 释放等待在途写入且取消延迟任务和后续提交()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var queue = new DockLayoutSaveQueue(_ => { entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); calls++; return true; }, _ => { });
        queue.Submit(Snapshot(1));
        var flush = queue.FlushAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposed = Task.Run(queue.Dispose);
        Assert.False(disposed.IsCompleted);
        release.Set();
        await Task.WhenAll(flush, disposed);
        queue.Submit(Snapshot(2));
        await queue.FlushAsync();
        Assert.Equal(1, calls);
    }

    private static DockLayoutSnapshotV3 Snapshot(double x) => new(3,
        new("main", DockWindowBounds.Default with { X = x }, DockLayoutNode.Documents()), [], []);

    private sealed class Clock : TimeProvider
    {
        private readonly List<DelayTimer> _timers = [];
        private long _milliseconds;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new DelayTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        internal void Advance(int milliseconds)
        {
            _milliseconds += milliseconds;
            foreach (var timer in _timers.ToArray()) timer.Fire(_milliseconds);
        }
        private sealed class DelayTimer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            private double _due = double.PositiveInfinity;
            public bool Change(TimeSpan dueTime, TimeSpan period) { _due = clock._milliseconds + dueTime.TotalMilliseconds; return true; }
            internal void Fire(long now) { if (_due > now) return; _due = double.PositiveInfinity; callback(state); }
            public void Dispose() => _due = double.PositiveInfinity;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
