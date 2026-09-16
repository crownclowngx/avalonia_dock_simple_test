using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 合并静默期内的快照并串行写入。输入已经脱离 UI 对象；旧写入完成后才允许新修订提交，
/// flush 取消延迟而不取消进行中的原子事务。释放时等待文件操作，不把锁提前交给另一实例。
/// </summary>
internal sealed class DockLayoutSaveQueue(Func<DockLayoutSnapshotV3, bool> write,
    Action<Exception?> completed, TimeProvider? timeProvider = null) : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _writer = new(1);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private CancellationTokenSource? _delay;
    private DockLayoutSnapshotV3? _latest;
    private long _revision;
    private long _savedRevision;
    private Task _pending = Task.CompletedTask;
    private bool _disposed;

    internal void Submit(DockLayoutSnapshotV3 snapshot)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _latest = snapshot;
            var revision = ++_revision;
            _delay?.Cancel();
            _delay?.Dispose();
            _delay = new CancellationTokenSource();
            _pending = RunAsync(snapshot, revision, _delay.Token, delayed: true);
        }
    }

    internal Task FlushAsync()
    {
        lock (_sync)
        {
            if (_disposed || _latest is null) return _pending;
            _delay?.Cancel();
            return _pending = RunAsync(_latest, _revision, CancellationToken.None, delayed: false);
        }
    }

    private async Task RunAsync(DockLayoutSnapshotV3 snapshot, long revision, CancellationToken token, bool delayed)
    {
        try
        {
            if (delayed) await Task.Delay(TimeSpan.FromMilliseconds(750), _clock, token).ConfigureAwait(false);
            await _writer.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // 进入串行区后重新检查版本，迟到的旧请求不能覆盖已提交的新快照。
                if (revision <= _savedRevision) return;
                if (!await Task.Run(() => write(snapshot)).ConfigureAwait(false))
                    throw new InvalidOperationException("布局当前只读，未保存。");
                _savedRevision = revision;
                Notify(null);
            }
            finally { _writer.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { Notify(exception); }
    }

    private void Notify(Exception? exception)
    {
        try { completed(exception); }
        catch { /* 状态通知失败不能把已提交文件重新归类为事务失败。 */ }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _delay?.Cancel();
        }
        // 所有 await 均不捕获 UI 上下文，DI 同步释放可以安全等待文件事务。
        _pending.GetAwaiter().GetResult();
        _writer.Wait();
        _writer.Release();
        _delay?.Dispose();
    }
}
