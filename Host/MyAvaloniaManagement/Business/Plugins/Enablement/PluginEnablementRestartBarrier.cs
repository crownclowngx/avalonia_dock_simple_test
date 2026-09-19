using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Plugins.Enablement;

/// <summary>退出准备只需要冻结设置并排空已接受操作，不获得读写设置的额外权限。</summary>
internal interface IPluginEnablementRestartBarrier
{
    Task<IDisposable> PauseForRestartAsync(CancellationToken cancellationToken);
}

/// <summary>设置服务独占的操作准入门。冻结与登记在同一锁内完成，窗口销毁不会改变已接受操作的寿命。</summary>
internal sealed class PluginEnablementOperationGate : IPluginEnablementRestartBarrier
{
    private readonly object _gate = new();
    private int _active;
    private bool _paused, _failed;
    private TaskCompletionSource _drained = Completed();

    internal bool TryEnter()
    {
        lock (_gate)
        {
            if (_paused) return false;
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    internal void Exit(bool success)
    {
        lock (_gate)
        {
            if (_paused && !success) _failed = true;
            if (--_active == 0) _drained.TrySetResult();
        }
    }

    /// <summary>返回许可持有冻结，取消时必须释放；超时不取消已接受的文件提交。</summary>
    public async Task<IDisposable> PauseForRestartAsync(CancellationToken cancellationToken)
    {
        Task drain;
        lock (_gate)
        {
            if (_paused) throw new InvalidOperationException("设置已经在准备退出。");
            _paused = true; _failed = false; drain = _drained.Task;
        }
        try
        {
            await drain.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
                if (_failed) throw new InvalidOperationException("正在保存的插件设置未能提交，请确认设置后重试。");
            return new PauseLease(this);
        }
        catch { Resume(); throw; }
    }

    private void Resume() { lock (_gate) _paused = false; }
    private static TaskCompletionSource Completed() { var value = new TaskCompletionSource(); value.SetResult(); return value; }
    private sealed class PauseLease(PluginEnablementOperationGate owner) : IDisposable
    {
        private PluginEnablementOperationGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Resume();
    }
}
