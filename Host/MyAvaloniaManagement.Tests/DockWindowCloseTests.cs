using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;

namespace MyAvaloniaManagement.Tests;

/// <summary>用可控队列证明原生回调适配的时序；真实 HostWindow 行为另由 Headless 集成验证。</summary>
public sealed class DockWindowCloseTests
{
    [Fact]
    public async Task 首次关闭同步取消且仅在排队重试后消费同一范围许可()
    {
        using var context = DocumentTestContext.Create();
        var (window, document) = await CreateWindowAsync(context);
        var documents = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var queue = new Queue<Action>();
        using var closing = new DockWindowCloseCoordinator(documents, queue.Enqueue);
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);
        var retried = 0;
        Assert.False(closing.TryBeginClose(window, () =>
        {
            Assert.True(closing.TryBeginClose(window, () => throw new InvalidOperationException()));
            Assert.True(documents.TryBeginDockClose(document, () => throw new InvalidOperationException()));
            retried++;
        }));
        Assert.Equal(0, retried);
        Assert.False(document.ClosingToken.IsCancellationRequested);
        Assert.Single(queue);
        queue.Dequeue()();
        Assert.Equal(1, retried);
        Assert.False(Assert.Single(context.Interactions.CloseRequests).IsExit);
        closing.Complete(window);
        Assert.False(documents.IsClosing(document));
    }

    [Fact]
    public async Task 确认后组内容变化不会把新页面一起关闭()
    {
        using var context = DocumentTestContext.Create();
        var (window, document) = await CreateWindowAsync(context);
        var documents = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var queue = new Queue<Action>();
        using var closing = new DockWindowCloseCoordinator(documents, queue.Enqueue);
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);
        Assert.False(closing.TryBeginClose(window, () => throw new InvalidOperationException("不能重试")));
        var dock = Assert.IsType<DocumentDock>(window.Layout!.VisibleDockables![0]);
        dock.VisibleDockables!.Add(new Tool { Id = "new-tool" });
        queue.Dequeue()();
        Assert.False(documents.IsClosing(document));
        Assert.False(document.ClosingToken.IsCancellationRequested);
    }

    [Fact]
    public async Task 窗口已移除时迟到确认只清理许可不重新创建窗口()
    {
        using var context = DocumentTestContext.Create();
        var (window, document) = await CreateWindowAsync(context);
        var documents = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var queue = new Queue<Action>();
        using var closing = new DockWindowCloseCoordinator(documents, queue.Enqueue);
        var choice = new TaskCompletionSource<DocumentCloseChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Interactions.PendingCloseChoice = choice;
        Assert.False(closing.TryBeginClose(window, () => throw new InvalidOperationException("不能重试")));
        closing.Complete(window);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        documents.StateChanged += (_, _) => { if (!documents.IsClosing(document)) released.TrySetResult(); };
        choice.SetResult(DocumentCloseChoice.Discard);
        await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(queue);
        Assert.False(document.ClosingToken.IsCancellationRequested);
    }

    [Fact]
    public async Task 重试失败或适配器释放都会撤销未消费许可()
    {
        using var context = DocumentTestContext.Create();
        var (window, document) = await CreateWindowAsync(context);
        var documents = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var queue = new Queue<Action>();
        using var closing = new DockWindowCloseCoordinator(documents, queue.Enqueue);
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);
        Assert.False(closing.TryBeginClose(window, () => throw new InvalidOperationException("框架失败")));
        queue.Dequeue()();
        Assert.False(documents.IsClosing(document));
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);
        Assert.False(closing.TryBeginClose(window, () => throw new InvalidOperationException("释放后不能调用")));
        closing.Dispose();
        queue.Dequeue()();
        Assert.False(documents.IsClosing(document));
        Assert.Equal(2, context.Interactions.CloseRequests.Count);
    }

    private static async Task<(IDockWindow Window, ManagedDocumentDockable Document)> CreateWindowAsync(TestHostContext context)
    {
        _ = context.CreateMainWindowViewModel();
        _ = await context.Provider.GetRequiredService<DocumentPersistenceCoordinator>().CreateDocumentAsync(TestDocumentIds.TypeId);
        var document = context.Workspace.GetDocuments().Last();
        Assert.IsType<TestSavableDocument>(document.Model).IsModified = true;
        // 这里只构造范围查询夹具，不伪装 FloatDockable 或原生窗口已被验证。
        return (new DockWindow
        {
            Layout = new RootDock
            {
                VisibleDockables = context.Workspace.DockFactory.CreateList<IDockable>(new DocumentDock
                {
                    VisibleDockables = context.Workspace.DockFactory.CreateList<IDockable>(document),
                }),
            },
        }, document);
    }
}
