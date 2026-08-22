using MyAvaloniaManagement.PluginSdk;
using MyPlugTest.Services;
using MyPlugTest.ViewModels;
using Xunit;

namespace MyPlugTest.Tests;

/// <summary>直接验证 MyPlugTest Welcome Document 对保存修订的所有权。</summary>
public sealed class RevisionedDocumentSaveTests
{
    [Fact]
    public async Task 捕获后继续编辑_旧修订不清脏且当前修订确认幂等()
    {
        using var lifetime = new TestDocumentLifetime();
        using var model = CreateModel(lifetime);
        await model.InitializeAsync(new DocumentActivationContext("修订测试"), default);
        var dirtyChanges = 0;
        model.IsDirtyChanged += (_, _) => dirtyChanges++;

        model.Url = "https://first.test";
        var first = await model.CaptureSaveSnapshotAsync(default);
        model.ResponseContent = "捕获后的新正文";
        var current = await model.CaptureSaveSnapshotAsync(default);

        Assert.True(current.Revision.Value > first.Revision.Value);
        model.AcceptChanges(first.Revision);
        Assert.True(model.IsDirty);
        Assert.Equal(1, dirtyChanges);

        model.AcceptChanges(current.Revision);
        model.AcceptChanges(current.Revision);
        Assert.False(model.IsDirty);
        Assert.Equal(2, dirtyChanges);
    }

    [Fact]
    public async Task 快照内容可恢复且初始化后的目标Document保持干净()
    {
        using var sourceLifetime = new TestDocumentLifetime();
        using var source = CreateModel(sourceLifetime);
        await source.InitializeAsync(new DocumentActivationContext("来源"), default);
        source.Url = "https://restore.test";
        source.ResponseContent = "恢复正文";
        source.UrlHistory.AddUrl("https://history.test");
        var snapshot = await source.CaptureSaveSnapshotAsync(default);

        using var targetLifetime = new TestDocumentLifetime();
        using var target = CreateModel(targetLifetime);
        await target.InitializeAsync(
            new DocumentActivationContext("恢复目标", restoredContent: snapshot.Content),
            default);

        Assert.Equal("https://restore.test", target.Url);
        Assert.Equal("恢复正文", target.ResponseContent);
        Assert.Equal("https://history.test", Assert.Single(target.UrlHistory.HistoryItems).Url);
        Assert.False(target.IsDirty);
    }

    [Fact]
    public async Task 捕获同时观察调用方取消与Document关闭令牌()
    {
        using var lifetime = new TestDocumentLifetime();
        using var model = CreateModel(lifetime);
        await model.InitializeAsync(new DocumentActivationContext("取消测试"), default);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await model.CaptureSaveSnapshotAsync(canceled.Token));

        lifetime.Close();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await model.CaptureSaveSnapshotAsync(default));
    }

    private static TestWelcomeViewModel CreateModel(IDocumentLifetime lifetime) =>
        new(
            new TestEventBus(),
            new UrlHistoryViewModel(),
            new StubUrlContentService(),
            lifetime);

    private sealed class StubUrlContentService : IUrlContentService
    {
        public Task<string> GetStringAsync(
            string url,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);
    }

    private sealed class TestEventBus : IHostEventBus
    {
        public void Publish<TEvent>(TEvent @event) where TEvent : class
        {
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class =>
            new Subscription();

        private sealed class Subscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class TestDocumentLifetime : IDocumentLifetime, IDisposable
    {
        private readonly CancellationTokenSource _source = new();

        public CancellationToken ClosingToken => _source.Token;
        public bool IsClosing => _source.IsCancellationRequested;

        internal void Close() => _source.Cancel();

        public void Dispose() => _source.Dispose();
    }
}
