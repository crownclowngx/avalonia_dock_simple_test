using System;
using Avalonia.Threading;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

/// <summary>明确区分既有同步 UI 通知与始终排队的 Palette 合并行为。</summary>
internal enum UiRefreshMode
{
    ImmediateOnUiThread,
    AlwaysPost,
}

/// <summary>合并尚未发布的 UI 刷新请求，并在释放后使迟到请求失效。</summary>
/// <remarks>
/// 设计思路：只提取调度状态，订阅、事件委托、业务状态和异常映射仍由消费者拥有。
/// 与消费者共用原同步锁，使“清除 pending、检查释放、取得观察者快照”保持同一个原子范围；
/// 若另外引入调度锁，这几个动作之间会出现原来没有的竞态窗口。
/// RequestRefresh 只在锁外调用消费者。消费者在自己的锁内调用 TryBeginRefresh，然后
/// 在锁外派发已取得的观察者快照；这里不捕获回调异常，也不增加节流、重试或全局状态。
/// </remarks>
internal sealed class UiRefreshScheduler : IDisposable
{
    private readonly object _gate;
    private readonly Dispatcher _dispatcher;
    private readonly UiRefreshMode _mode;
    private readonly Action _publish;
    private bool _pending;
    private bool _disposed;

    /// <summary>绑定消费者已有的同步边界、UI Dispatcher、通知时机及发布入口。</summary>
    internal UiRefreshScheduler(object gate, Dispatcher dispatcher, UiRefreshMode mode, Action publish)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _mode = mode;
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
    }

    /// <summary>可从 UI 或后台线程请求刷新；已有待发布请求时合并，释放后忽略。</summary>
    internal void RequestRefresh()
    {
        lock (_gate)
        {
            if (_disposed || _pending) return;
            _pending = true;
        }
        if (_mode == UiRefreshMode.ImmediateOnUiThread && _dispatcher.CheckAccess())
            _publish();
        else
            _dispatcher.Post(_publish, DispatcherPriority.Normal);
    }

    /// <summary>由发布入口在共享锁内调用，清除排队标记并检查迟到回调是否仍可发布。</summary>
    /// <remarks>
    /// 锁为可重入 Monitor，允许调用方在同一锁内紧接着捕获事件快照。先清除标记使
    /// 观察者回调期间产生的新请求能按原策略刷新；已经开始的观察者快照不受后续释放撤销。
    /// </remarks>
    internal bool TryBeginRefresh()
    {
        lock (_gate)
        {
            _pending = false;
            return !_disposed;
        }
    }

    /// <summary>幂等关闭请求入口，不阻塞等待 Dispatcher，也不代替消费者解除业务订阅。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending = false;
        }
    }
}
