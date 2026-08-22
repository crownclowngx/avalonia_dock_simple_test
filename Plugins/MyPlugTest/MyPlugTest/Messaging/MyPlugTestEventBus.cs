using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MyPlugTest.Messaging;

/// <summary>MyPlugTest 插件 Provider 独占的同步事件总线。</summary>
/// <remarks>
/// 本类只保存订阅并完成同步派发。插件 Provider 以 Singleton 生命周期持有本实例，因此多个
/// MyPlugTest Document Scope 可以通信，而不同插件 Provider、不同 HostRuntime 之间不会共享消息。
/// 订阅采用强引用和显式令牌；这让 Document Scope 的关闭可以确定地结束订阅，而不依赖垃圾回收。
/// </remarks>
internal sealed class MyPlugTestEventBus : IMyPlugTestEventBus, IDisposable
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

        // 处理器属于插件业务代码，绝不能在总线锁内执行。快照同时保证本轮顺序稳定，并让
        // 处理器可以安全地发布、订阅或释放令牌；本轮新增订阅从下一次发布开始生效。
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

        // 字典清空后总线已不再强持有处理器。逐个标记令牌只同步释放状态，不执行用户代码；
        // 调用方稍后重复 Dispose 仍会安全返回，也不会重新访问已释放的 Provider。
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
        MyPlugTestEventBus owner,
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
