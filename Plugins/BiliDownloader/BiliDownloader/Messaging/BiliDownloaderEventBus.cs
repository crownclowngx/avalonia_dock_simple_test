using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BiliDownloader.Messaging;

/// <summary>BiliDownloader 插件 Provider 独占的同步事件总线。</summary>
/// <remarks>
/// 本实例以插件级 Singleton 存活，使 Tool、Lifecycle 和多个 Document Scope 共享同一插件业务事件流。
/// 不使用静态默认实例，也不由 Host 根容器提供，因此并行 Runtime 与其他插件天然隔离。
/// </remarks>
internal sealed class BiliDownloaderEventBus : IBiliDownloaderEventBus, IDisposable
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

        // 锁内只复制订阅事实，不执行任何登录、下载或 UI 处理器。快照固定本轮顺序，
        // 并允许处理器安全地重入发布、释放自身或增加下一轮才可见的新订阅。
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

        // 先清除强引用，再在锁外同步令牌状态；Dispose 不调用业务处理器，且重复调用保持幂等。
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
        BiliDownloaderEventBus owner,
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
