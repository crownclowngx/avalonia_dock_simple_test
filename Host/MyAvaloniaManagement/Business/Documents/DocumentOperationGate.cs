using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 为新建、打开、菜单保存和关闭前保存提供同一串行边界，并证明退出时在途操作已经结束。
/// </summary>
/// <remarks>
/// 同一进程只有一个主工作区。使用一个窄门比在多个协调器中分别加锁更重要：否则标签
/// 关闭保存可能与菜单保存同时覆盖文件，批量打开也可能在保存期间观察到半更新的路径状态。
/// </remarks>
internal sealed class DocumentOperationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateGate = new();
    private bool _accepting = true;
    private int _pending;
    private TaskCompletionSource? _drained;

    /// <summary>关闭宽限与等待结果分开；超时不表示操作终止，调用方必须保留在用资源。</summary>
    internal TimeSpan ShutdownGrace { get; }

    public DocumentOperationGate() : this(TimeSpan.FromSeconds(5)) { }

    internal DocumentOperationGate(TimeSpan shutdownGrace) => ShutdownGrace = shutdownGrace;

    internal async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_stateGate)
        {
            if (!_accepting) throw new InvalidOperationException("Host 正在关闭，不再接受文档操作。");
            if (_pending++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var entered = false;
        try
        {
            await _gate.WaitAsync();
            entered = true;
            // 关闭前排队但尚未开始的请求也计入排空，获得串行门后不得再启动插件代码。
            lock (_stateGate)
                if (!_accepting) throw new InvalidOperationException("Host 正在关闭，排队的文档操作已停止。");
            return await operation();
        }
        finally
        {
            if (entered) _gate.Release();
            TaskCompletionSource? drained = null;
            lock (_stateGate)
                if (--_pending == 0) drained = _drained;
            drained?.TrySetResult();
        }
    }

    /// <summary>关闭入口但不释放串行门，正在初始化的文档仍可完成并由原有调用链回收。</summary>
    internal void BeginShutdown() { lock (_stateGate) _accepting = false; }

    /// <summary>提供真实操作结束的证据；同时统计执行中及等待串行门的请求。</summary>
    internal async Task<bool> WaitForDrainAsync(TimeSpan timeout)
    {
        Task completion;
        lock (_stateGate) completion = _pending == 0 ? Task.CompletedTask : _drained!.Task;
        try { await completion.WaitAsync(timeout).ConfigureAwait(false); return true; }
        catch (TimeoutException) { return false; }
    }
}
