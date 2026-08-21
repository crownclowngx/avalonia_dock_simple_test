using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Events;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 固定 G9 事件总线的同步派发、异常、并发、运行时隔离和 Document Scope 所有权语义。
/// </summary>
public sealed class HostEventBusTests
{
    [Fact]
    public void HostEventBus按订阅顺序在发布线程同步派发且只匹配精确类型()
    {
        using var eventBus = new HostEventBus();
        var publisherThread = Environment.CurrentManagedThreadId;
        var calls = new List<string>();
        using var first = eventBus.Subscribe<BaseEvent>(_ =>
            calls.Add($"base:{Environment.CurrentManagedThreadId}"));
        using var second = eventBus.Subscribe<DerivedEvent>(_ =>
            calls.Add($"derived-1:{Environment.CurrentManagedThreadId}"));
        using var third = eventBus.Subscribe<DerivedEvent>(_ =>
            calls.Add($"derived-2:{Environment.CurrentManagedThreadId}"));

        eventBus.Publish(new DerivedEvent());

        Assert.Equal(
            [$"derived-1:{publisherThread}", $"derived-2:{publisherThread}"],
            calls);
    }

    [Fact]
    public void HostEventBus每条令牌独立且重复释放安全()
    {
        using var eventBus = new HostEventBus();
        var firstCount = 0;
        var secondCount = 0;
        var first = eventBus.Subscribe<TestEvent>(_ => firstCount++);
        using var second = eventBus.Subscribe<TestEvent>(_ => secondCount++);

        first.Dispose();
        first.Dispose();
        eventBus.Publish(new TestEvent());

        Assert.Equal(0, firstCount);
        Assert.Equal(1, secondCount);
    }

    [Fact]
    public void HostEventBus允许处理器释放自身并重入发布()
    {
        using var eventBus = new HostEventBus();
        var calls = new List<int>();
        IDisposable? subscription = null;
        subscription = eventBus.Subscribe<TestEvent>(message =>
        {
            calls.Add(message.Value);
            subscription!.Dispose();
            if (message.Value == 1)
            {
                eventBus.Publish(new TestEvent(2));
            }
        });

        eventBus.Publish(new TestEvent(1));
        eventBus.Publish(new TestEvent(3));

        Assert.Equal([1], calls);
    }

    [Fact]
    public void HostEventBus处理器异常原样传播并停止后续派发()
    {
        using var eventBus = new HostEventBus();
        var expected = new InvalidOperationException("预期处理失败");
        var laterCalled = false;
        using var first = eventBus.Subscribe<TestEvent>(_ => throw expected);
        using var second = eventBus.Subscribe<TestEvent>(_ => laterCalled = true);

        var actual = Assert.Throws<InvalidOperationException>(
            () => eventBus.Publish(new TestEvent()));

        Assert.Same(expected, actual);
        Assert.False(laterCalled);
    }

    [Fact]
    public void HostEventBus并发发布订阅和释放不损坏内部集合()
    {
        using var eventBus = new HostEventBus();
        var failures = new ConcurrentQueue<Exception>();

        Parallel.For(0, 256, index =>
        {
            try
            {
                using var subscription = eventBus.Subscribe<TestEvent>(_ => { });
                eventBus.Publish(new TestEvent(index));
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
        });

        Assert.Empty(failures);
    }

    [Fact]
    public void HostEventBus两个宿主服务根互不接收事件()
    {
        using var firstProvider = CreateHostServices();
        using var secondProvider = CreateHostServices();
        var firstBus = firstProvider.GetRequiredService<IHostEventBus>();
        var secondBus = secondProvider.GetRequiredService<IHostEventBus>();
        var firstCount = 0;
        var secondCount = 0;
        using var firstSubscription = firstBus.Subscribe<TestEvent>(_ => firstCount++);
        using var secondSubscription = secondBus.Subscribe<TestEvent>(_ => secondCount++);

        firstBus.Publish(new TestEvent());

        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
    }

    [Fact]
    public void HostEventBus释放后拒绝发布和订阅且旧令牌仍可重复释放()
    {
        var eventBus = new HostEventBus();
        var subscription = eventBus.Subscribe<TestEvent>(_ => { });

        eventBus.Dispose();
        eventBus.Dispose();

        Assert.Throws<ObjectDisposedException>(() => eventBus.Publish(new TestEvent()));
        Assert.Throws<ObjectDisposedException>(() => eventBus.Subscribe<TestEvent>(_ => { }));
        subscription.Dispose();
        subscription.Dispose();
    }

    [Fact]
    public void HostEventBus空事件和空处理器被明确拒绝()
    {
        using var eventBus = new HostEventBus();

        Assert.Throws<ArgumentNullException>(() => eventBus.Publish<TestEvent>(null!));
        Assert.Throws<ArgumentNullException>(() => eventBus.Subscribe<TestEvent>(null!));
    }

    [Fact]
    public void HostEventBusDocument关闭后释放自己的订阅且不影响其他Document()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEventBus, HostEventBus>();
        services.AddScoped<EventAwareDocument>();
        services.AddDocumentScopeManagement();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var eventBus = provider.GetRequiredService<IHostEventBus>();
        var firstLease = manager.CreatePluginDocument(typeof(EventAwareDocument));
        var secondLease = manager.CreatePluginDocument(typeof(EventAwareDocument));
        var first = Assert.IsType<EventAwareDocument>(firstLease.Model);
        var second = Assert.IsType<EventAwareDocument>(secondLease.Model);

        eventBus.Publish(new TestEvent());
        Assert.True(manager.Release(first));
        Assert.False(manager.Release(first));
        eventBus.Publish(new TestEvent());

        Assert.Equal(1, first.ReceivedCount);
        Assert.Equal(2, second.ReceivedCount);
        Assert.True(first.WasClosingWhenDisposed);

        manager.Dispose();
        eventBus.Publish(new TestEvent());
        Assert.Equal(2, second.ReceivedCount);
        Assert.True(second.WasClosingWhenDisposed);
    }

    [Fact]
    public void HostEventBusDocument创建失败时Scope释放已经建立的订阅()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEventBus, HostEventBus>();
        services.AddSingleton<EventProbe>();
        services.AddScoped<ScopedEventSubscription>();
        services.AddScoped<FailingEventDocument>();
        services.AddDocumentScopeManagement();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var eventBus = provider.GetRequiredService<IHostEventBus>();
        var probe = provider.GetRequiredService<EventProbe>();

        Assert.Throws<InvalidOperationException>(
            () => manager.CreatePluginDocument(typeof(FailingEventDocument)));
        eventBus.Publish(new TestEvent());

        Assert.Equal(0, probe.ReceivedCount);
        Assert.Equal(1, probe.DisposedCount);
    }

    private static ServiceProvider CreateHostServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEventBus, HostEventBus>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    private class BaseEvent;

    private sealed class DerivedEvent : BaseEvent;

    private sealed record TestEvent(int Value = 0);

    private sealed class EventAwareDocument : MyAvaloniaManagement.PluginSdk.IPluginDocument, IDisposable
    {
        private readonly MyAvaloniaManagement.PluginSdk.IDocumentLifetime _lifetime;
        private readonly IDisposable _subscription;
        private int _disposed;

        public EventAwareDocument(
            IHostEventBus eventBus,
            MyAvaloniaManagement.PluginSdk.IDocumentLifetime lifetime)
        {
            _lifetime = lifetime;
            // 订阅是构造函数最后一个可能失败的动作；成功后令牌由 Document 与 Scope 共同拥有。
            _subscription = eventBus.Subscribe<TestEvent>(_ =>
            {
                if (!_lifetime.IsClosing)
                {
                    ReceivedCount++;
                }
            });
        }

        public int ReceivedCount { get; private set; }

        public bool WasClosingWhenDisposed { get; private set; }
        public MyAvaloniaManagement.PluginSdk.DocumentPresentationState Presentation => new("事件测试");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            MyAvaloniaManagement.PluginSdk.DocumentActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            WasClosingWhenDisposed = _lifetime.IsClosing;
            _subscription.Dispose();
        }
    }

    private sealed class EventProbe
    {
        internal int ReceivedCount;
        internal int DisposedCount;
    }

    /// <summary>
    /// 模拟 Document 对象树中先于根 ViewModel 创建的 scoped 订阅者。若随后 Document 构造失败，
    /// DocumentScopeManager 必须释放整个 Scope，从而撤销这条已经成功建立的订阅。
    /// </summary>
    private sealed class ScopedEventSubscription : IDisposable
    {
        private readonly EventProbe _probe;
        private readonly IDisposable _subscription;
        private int _disposed;

        public ScopedEventSubscription(IHostEventBus eventBus, EventProbe probe)
        {
            _probe = probe;
            _subscription = eventBus.Subscribe<TestEvent>(
                _ => Interlocked.Increment(ref _probe.ReceivedCount));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _subscription.Dispose();
            Interlocked.Increment(ref _probe.DisposedCount);
        }
    }

    private sealed class FailingEventDocument : MyAvaloniaManagement.PluginSdk.IPluginDocument
    {
        public FailingEventDocument(ScopedEventSubscription subscription)
        {
            _ = subscription;
            throw new InvalidOperationException("模拟 Document 构造失败");
        }

        public MyAvaloniaManagement.PluginSdk.DocumentPresentationState Presentation => new("失败测试");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            MyAvaloniaManagement.PluginSdk.DocumentActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
