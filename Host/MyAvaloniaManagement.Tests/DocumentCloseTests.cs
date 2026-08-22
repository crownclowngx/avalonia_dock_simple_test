using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证当前关闭协调器的取消、修订保存、重入许可和异常兜底。</summary>
public sealed class DocumentCloseTests
{
    [Fact]
    public async Task 非持久化或干净Document无需确认且参数防御有效()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var welcome = Assert.IsType<ManagedDocumentDockable>(GetDock(context).VisibleDockables![0]);
        Assert.True(coordinator.TryBeginDockClose(welcome, static () => { }));

        var document = await CreateDirtyDocumentAsync(context, dirty: false);
        Assert.True(coordinator.TryBeginDockClose(document, static () => { }));
        Assert.Throws<ArgumentNullException>(() =>
            coordinator.TryBeginDockClose(null!, static () => { }));
        Assert.Throws<ArgumentNullException>(() =>
            coordinator.TryBeginDockClose(document, null!));
    }

    [Fact]
    public async Task Dock关闭取消保持打开_放弃授予一次性重入许可()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var document = await CreateDirtyDocumentAsync(context);
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var retryCount = 0;

        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Cancel);
        Assert.False(coordinator.TryBeginDockClose(document, () => retryCount++));
        Assert.Equal(0, retryCount);

        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);
        Assert.False(coordinator.TryBeginDockClose(document, () => retryCount++));
        Assert.Equal(1, retryCount);
        Assert.True(coordinator.TryBeginDockClose(document, () => retryCount++));
        Assert.Equal(1, retryCount);
    }

    [Fact]
    public async Task Dock重复请求只保留一个确认任务()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var document = await CreateDirtyDocumentAsync(context);
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var pending = new TaskCompletionSource<DocumentCloseChoice>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Interactions.PendingCloseChoice = pending;
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.False(coordinator.TryBeginDockClose(document, retried.SetResult));
        Assert.False(coordinator.TryBeginDockClose(document, retried.SetResult));
        Assert.Single(context.Interactions.CloseRequests);

        pending.SetResult(DocumentCloseChoice.Discard);
        await retried.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(context.Interactions.CloseRequests);
    }

    [Fact]
    public async Task Dock保存取消不重试_提交后警告仍重试且提示失败不改变事实()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var document = await CreateDirtyDocumentAsync(context);
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var retryCount = 0;

        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Save);
        Assert.False(coordinator.TryBeginDockClose(document, () => retryCount++));
        Assert.Equal(0, retryCount);

        context.Storage.SavePath = Path.Combine(context.TempDirectory, "dock-warning.mamdoc");
        context.Provider.GetRequiredService<DocumentTestProbe>().AcceptChangesException =
            new InvalidOperationException("accept-secret");
        context.Interactions.ShowErrorException = new InvalidOperationException("dialog-secret");
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Save);
        Assert.False(coordinator.TryBeginDockClose(document, () => retryCount++));
        Assert.Equal(1, retryCount);
        Assert.NotEmpty(context.Storage.Writes);
    }

    [Fact]
    public async Task Dock确认或重入回调异常被兜底且不会遗留许可()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var document = await CreateDirtyDocumentAsync(context);
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();

        context.Interactions.ConfirmCloseException = new InvalidOperationException("confirm-secret");
        Assert.False(coordinator.TryBeginDockClose(document, static () => { }));
        Assert.Contains(context.Interactions.Errors, message => message.Contains("保持打开", StringComparison.Ordinal));

        context.Interactions.ConfirmCloseException = null;
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);
        Assert.False(coordinator.TryBeginDockClose(
            document,
            static () => throw new InvalidOperationException("retry-secret")));
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Cancel);
        Assert.False(coordinator.TryBeginDockClose(document, static () => { }));
        Assert.Equal(3, context.Interactions.CloseRequests.Count);
    }

    [Fact]
    public async Task 窗口关闭覆盖干净_放弃_保存取消和重复请求()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        Assert.True(await coordinator.ConfirmWindowCloseAsync([]));

        var document = await CreateDirtyDocumentAsync(context);
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);
        Assert.True(await coordinator.ConfirmWindowCloseAsync([document]));

        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Save);
        Assert.False(await coordinator.ConfirmWindowCloseAsync([document]));

        var pending = new TaskCompletionSource<DocumentCloseChoice>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Interactions.PendingCloseChoice = pending;
        var first = coordinator.ConfirmWindowCloseAsync([document]);
        Assert.False(await coordinator.ConfirmWindowCloseAsync([document]));
        pending.SetResult(DocumentCloseChoice.Cancel);
        Assert.False(await first);
    }

    [Fact]
    public async Task 窗口确认异常保持打开且固定提示脱敏()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var document = await CreateDirtyDocumentAsync(context);
        context.Interactions.ConfirmCloseException = new InvalidOperationException("window-secret");

        var closed = await context.Provider.GetRequiredService<DocumentCloseCoordinator>()
            .ConfirmWindowCloseAsync([document]);

        Assert.False(closed);
        Assert.Contains(context.Interactions.Errors, message => message.Contains("保持打开", StringComparison.Ordinal));
        Assert.DoesNotContain(
            context.Interactions.Errors,
            message => message.Contains("window-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 窗口保存期间出现新修订_保持打开且再次保存后允许关闭()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var document = await CreateDirtyDocumentAsync(context);
        var model = Assert.IsType<TestSavableDocument>(document.Model);
        context.Storage.SavePath = Path.Combine(context.TempDirectory, "window-revision.mamdoc");
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Save);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Storage.PauseNextWrite(writeStarted, releaseWrite);

        var firstClose = context.Provider.GetRequiredService<DocumentCloseCoordinator>()
            .ConfirmWindowCloseAsync([document]);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.Content = "保存期间的新内容";
        model.IsModified = true;
        releaseWrite.SetResult();

        Assert.False(await firstClose.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(model.IsDirty);
        Assert.Contains(context.Interactions.Errors, message =>
            message.Contains("再次保存", StringComparison.Ordinal));

        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Save);
        Assert.True(await context.Provider.GetRequiredService<DocumentCloseCoordinator>()
            .ConfirmWindowCloseAsync([document]));
        Assert.False(model.IsDirty);
    }

    [Fact]
    public async Task Dock保存期间出现新修订_不授予重入许可并保持Document打开()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var document = await CreateDirtyDocumentAsync(context);
        var model = Assert.IsType<TestSavableDocument>(document.Model);
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        context.Storage.SavePath = Path.Combine(context.TempDirectory, "dock-revision.mamdoc");
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Save);
        context.Interactions.ErrorShown = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Storage.PauseNextWrite(writeStarted, releaseWrite);
        var retryCount = 0;

        Assert.False(coordinator.TryBeginDockClose(document, () => retryCount++));
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.Content = "Dock 保存期间的新内容";
        model.IsModified = true;
        releaseWrite.SetResult();
        var message = await context.Interactions.ErrorShown.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("再次保存", message, StringComparison.Ordinal);
        Assert.Equal(0, retryCount);
        Assert.True(model.IsDirty);
        Assert.False(coordinator.TryBeginDockClose(document, () => retryCount++));
        Assert.Equal(2, context.Interactions.CloseRequests.Count);
    }

    private static async Task<ManagedDocumentDockable> CreateDirtyDocumentAsync(
        TestHostContext context,
        bool dirty = true)
    {
        var result = await context.Provider.GetRequiredService<DocumentPersistenceCoordinator>()
            .CreateDocumentAsync(TestDocumentIds.TypeId);
        Assert.True(result.ShouldUpdateError);
        var document = Assert.Single(
            GetDock(context).VisibleDockables!.OfType<ManagedDocumentDockable>(),
            item => item.Registration.Descriptor.DocumentTypeId == TestDocumentIds.TypeId);
        Assert.IsType<TestSavableDocument>(document.Model).IsModified = dirty;
        return document;
    }

    private static DocumentDock GetDock(TestHostContext context) =>
        Assert.IsType<DocumentDock>(context.Workspace.DockFactory.GetDockable<IDocumentDock>(
            MyAvaloniaManagement.Business.Layout.DockLayoutIds.Documents));
}
