using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MyAvaloniaManagementCommon.Events;

namespace MyAvaloniaManagement.Business.Events;

/// <summary>
/// 当前宿主运行时独享的同步事件总线。
/// </summary>
/// <remarks>
/// 本类只负责保存订阅和执行同步派发，不承担 UI 调度、异步任务、重试或诊断策略。
/// 订阅使用强引用，生命周期由返回的令牌显式表达；这比依赖垃圾回收时机的弱引用更容易验证，
/// 也让 Document Scope 和插件生命周期能够确定地结束各自订阅。
/// </remarks>
internal sealed class HostEventBus : IHostEventBus, IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<Type, List<Subscription>> _subscriptions = [];
    private bool _disposed;

    public void Publish<TEvent>(TEvent @event) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(@event);

        Subscription[] snapshot;
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            snapshot = _subscriptions.TryGetValue(typeof(TEvent), out var subscriptions)
                ? subscriptions.ToArray()
                : [];
        }

        // 用户代码绝不能在总线锁内执行。处理器可以安全地再次发布、订阅或释放自身令牌；
        // 同时，快照固定了本次发布的顺序，发布期间新增的订阅只会从下一次事件开始生效。
        foreach (var subscription in snapshot)
        {
            subscription.Invoke(@event);
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new Subscription(
            this,
            typeof(TEvent),
            message => handler((TEvent)message));
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_subscriptions.TryGetValue(typeof(TEvent), out var subscriptions))
            {
                subscriptions = [];
                _subscriptions.Add(typeof(TEvent), subscriptions);
            }

            subscriptions.Add(subscription);
        }

        return subscription;
    }

    public void Dispose()
    {
        Subscription[] subscriptions;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            subscriptions = _subscriptions.Values.SelectMany(items => items).ToArray();
            _subscriptions.Clear();
        }

        // 清空字典已经切断总线对处理器的强引用。逐个标记令牌可让调用方随后重复 Dispose
        // 安全返回；这里不执行任何用户代码，也不需要维持订阅顺序。
        foreach (var subscription in subscriptions)
        {
            subscription.MarkReleasedByOwner();
        }
    }

    private void Remove(Subscription subscription)
    {
        lock (_syncRoot)
        {
            if (_disposed ||
                !_subscriptions.TryGetValue(subscription.EventType, out var subscriptions))
            {
                return;
            }

            subscriptions.Remove(subscription);
            if (subscriptions.Count == 0)
            {
                _subscriptions.Remove(subscription.EventType);
            }
        }
    }

    private sealed class Subscription(
        HostEventBus owner,
        Type eventType,
        Action<object> handler) : IDisposable
    {
        private int _released;

        internal Type EventType { get; } = eventType;

        internal void Invoke(object message) => handler(message);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Remove(this);
            }
        }

        internal void MarkReleasedByOwner() =>
            Interlocked.Exchange(ref _released, 1);
    }
}
