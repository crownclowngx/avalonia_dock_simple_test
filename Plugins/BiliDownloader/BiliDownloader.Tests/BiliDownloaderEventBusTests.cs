using System.Collections.Concurrent;
using BiliDownloader.Messaging;

namespace BiliDownloader.Tests;

/// <summary>验证 BiliDownloader 私有消息器的同步派发、重入、并发和释放契约。</summary>
public sealed class BiliDownloaderEventBusTests
{
    [Fact]
    public void 按订阅顺序在发布线程同步派发且只匹配精确类型()
    {
        using var eventBus = new BiliDownloaderEventBus();
        var publisherThread = Environment.CurrentManagedThreadId;
        var calls = new List<string>();
        using var baseSubscription = eventBus.Subscribe<BaseEvent>(_ => calls.Add("base"));
        using var first = eventBus.Subscribe<DerivedEvent>(
            _ => calls.Add($"derived-1:{Environment.CurrentManagedThreadId}"));
        using var second = eventBus.Subscribe<DerivedEvent>(
            _ => calls.Add($"derived-2:{Environment.CurrentManagedThreadId}"));

        eventBus.Publish(new DerivedEvent());

        Assert.Equal(
            [$"derived-1:{publisherThread}", $"derived-2:{publisherThread}"],
            calls);
    }

    [Fact]
    public void 订阅中新增订阅从下一次发布开始生效()
    {
        using var eventBus = new BiliDownloaderEventBus();
        var calls = new List<string>();
        IDisposable? added = null;
        using var first = eventBus.Subscribe<TestEvent>(_ =>
        {
            calls.Add("first");
            added ??= eventBus.Subscribe<TestEvent>(_ => calls.Add("added"));
        });

        eventBus.Publish(new TestEvent());
        eventBus.Publish(new TestEvent());

        Assert.Equal(["first", "first", "added"], calls);
        added!.Dispose();
    }

    [Fact]
    public void 令牌独立幂等且处理器可以自释放并重入发布()
    {
        using var eventBus = new BiliDownloaderEventBus();
        var calls = new List<int>();
        IDisposable? subscription = null;
        subscription = eventBus.Subscribe<TestEvent>(message =>
        {
            calls.Add(message.Value);
            subscription!.Dispose();
            subscription.Dispose();
            if (message.Value == 1)
            {
                eventBus.Publish(new TestEvent(2));
            }
        });
        using var unaffected = eventBus.Subscribe<TestEvent>(message => calls.Add(message.Value * 10));

        eventBus.Publish(new TestEvent(1));
        eventBus.Publish(new TestEvent(3));

        Assert.Equal([1, 20, 10, 30], calls);
    }

    [Fact]
    public void 处理器异常原样传播并停止后续派发()
    {
        using var eventBus = new BiliDownloaderEventBus();
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
    public void 并发发布订阅和释放不损坏内部集合()
    {
        using var eventBus = new BiliDownloaderEventBus();
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
    public void 不同插件Provider实例互不接收事件()
    {
        using var firstBus = new BiliDownloaderEventBus();
        using var secondBus = new BiliDownloaderEventBus();
        var firstCount = 0;
        var secondCount = 0;
        using var first = firstBus.Subscribe<TestEvent>(_ => firstCount++);
        using var second = secondBus.Subscribe<TestEvent>(_ => secondCount++);

        firstBus.Publish(new TestEvent());

        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
    }

    [Fact]
    public void 释放后拒绝发布订阅且旧令牌仍可重复释放()
    {
        var eventBus = new BiliDownloaderEventBus();
        var subscription = eventBus.Subscribe<TestEvent>(_ => { });

        eventBus.Dispose();
        eventBus.Dispose();

        Assert.Throws<ObjectDisposedException>(() => eventBus.Publish(new TestEvent()));
        Assert.Throws<ObjectDisposedException>(() => eventBus.Subscribe<TestEvent>(_ => { }));
        subscription.Dispose();
        subscription.Dispose();
    }

    [Fact]
    public void 空事件和空处理器被明确拒绝()
    {
        using var eventBus = new BiliDownloaderEventBus();

        Assert.Throws<ArgumentNullException>(() => eventBus.Publish<TestEvent>(null!));
        Assert.Throws<ArgumentNullException>(() => eventBus.Subscribe<TestEvent>(null!));
    }

    private class BaseEvent;
    private sealed class DerivedEvent : BaseEvent;
    private sealed record TestEvent(int Value = 0);
}
