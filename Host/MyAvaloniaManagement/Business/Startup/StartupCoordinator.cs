using System;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Composition;

namespace MyAvaloniaManagement.Business.Startup;

internal enum StartupState { Created, Loading, Prepared, Ready, Cancelling, Cancelled, Failed }

/// <summary>
/// 拥有一次启动任务和唯一终态，不负责 UI 或资源释放。成功交付的 Runtime 始终留给 Program 收尾，
/// 即使取消恰好发生在工厂返回后也不丢失所有权；工厂交付前的失败由 Runtime 自身回滚。
/// </summary>
internal sealed class StartupCoordinator(
    Func<IStartupProgressSink, CancellationToken, Task<HostRuntime>> createRuntime) : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _completion;
    private StartupState _state;
    internal StartupProgressBuffer Progress { get; } = new();
    internal HostRuntime? Runtime { get; private set; }
    internal Exception? Failure { get; private set; }
    internal StartupState State { get { lock (_gate) return _state; } }

    /// <summary>重复启动共享同一个任务；外部等待不会阻塞 UI 消息循环。</summary>
    internal Task StartAsync()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_completion is not null) return _completion;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = completion.Task;
            if (_state != StartupState.Cancelling) _state = StartupState.Loading;
        }
        _ = RunAsync(completion);
        return completion.Task;
    }

    private async Task RunAsync(TaskCompletionSource completion)
    {
        try
        {
            _cancellation.Token.ThrowIfCancellationRequested();
            Runtime = await createRuntime(Progress, _cancellation.Token).ConfigureAwait(false);
            lock (_gate)
            {
                // 展示层可能在工厂运行期间失败；迟到成功不能覆盖已提交的故障。
                if (_state != StartupState.Failed)
                    _state = _state == StartupState.Cancelling ? StartupState.Cancelled : StartupState.Prepared;
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        { lock (_gate) { if (_state != StartupState.Failed) _state = StartupState.Cancelled; } }
        catch (Exception exception) { Fail(exception); }
        finally { completion.TrySetResult(); }
    }

    /// <summary>关闭意图先提交，再发协作取消；Ready 后的关闭由主工作台原协议接管。</summary>
    internal bool RequestCancellation()
    {
        lock (_gate)
        {
            if (_state is StartupState.Ready or StartupState.Failed or StartupState.Cancelled or StartupState.Cancelling) return false;
            _state = StartupState.Cancelling;
        }
        _cancellation.Cancel();
        return true;
    }

    /// <summary>仅在主窗口已完成展示后提交；已接受的取消优先于迟到的成功。</summary>
    internal bool TryComplete()
    {
        lock (_gate)
        {
            if (_state != StartupState.Prepared) return false;
            _state = StartupState.Ready;
            Progress.Report(new(StartupStage.Ready, Outcome: StartupOutcome.Succeeded));
            Progress.Close();
            return true;
        }
    }

    /// <summary>组合与窗口装配共用的失败入口，保留原始异常供宿主诊断边界解释。</summary>
    internal void Fail(Exception exception)
    {
        lock (_gate) { Failure ??= exception; _state = StartupState.Failed; }
        Progress.Close();
    }

    public void Dispose()
    {
        Progress.Close();
        _cancellation.Dispose();
    }
}
