using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Documents;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证当前关闭协调器的取消、修订保存、重入许可和异常兜底。</summary>
public sealed class DocumentCloseTests
{
    [Fact]
    public async Task V16单页已确认许可可接续精确的一页范围且取消会重新开放命令()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var page = await CreateDirtyDocumentAsync(context);
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);
        Assert.False(coordinator.TryBeginDockClose(page, () => { }));
        using (var approval = await coordinator.PrepareRangeCloseAsync([page]))
        {
            Assert.NotNull(approval);
            Assert.Single(context.Interactions.CloseRequests);
            Assert.True(coordinator.IsClosing(page));
            Assert.Null(await coordinator.PrepareRangeCloseAsync([page]));
        }
        Assert.False(coordinator.IsClosing(page));
        var leases = context.Provider.GetRequiredService<WorkbenchDocumentCommandLeaseStore>();
        Assert.True(leases.TryAcquire(page, out var lease));
        lease!.Dispose();
    }

    [Fact]
    public async Task V16单页许可不能扩大为多页范围或应用退出()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var first = await CreateDirtyDocumentAsync(context);
        var second = context.Workspace.GetDocuments().First(page => !ReferenceEquals(page, first));
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);
        Assert.False(coordinator.TryBeginDockClose(first, () => { }));
        Assert.Null(await coordinator.PrepareRangeCloseAsync([first, second]));
        Assert.Null(await coordinator.PrepareRangeCloseAsync([first], isApplicationExit: true));
        Assert.Single(context.Interactions.CloseRequests);
        Assert.True(coordinator.TryBeginDockClose(first, () => throw new InvalidOperationException()));
        coordinator.ReopenAfterDockRejection(first);
        Assert.False(coordinator.IsClosing(first));
    }

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

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public async Task 范围关闭取消或文件选择取消保持整组与生命周期(int choice)
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var first = await CreateDirtyDocumentAsync(context);
        _ = await context.Provider.GetRequiredService<DocumentPersistenceCoordinator>().CreateDocumentAsync(TestDocumentIds.TypeId);
        var second = context.Workspace.GetDocuments().Last();
        Assert.IsType<TestSavableDocument>(second.Model).IsModified = true;
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        context.Interactions.CloseChoices.Enqueue((DocumentCloseChoice)choice);

        using var approval = await coordinator.PrepareRangeCloseAsync([first, second]);

        Assert.Null(approval);
        var request = Assert.Single(context.Interactions.CloseRequests);
        Assert.False(request.IsExit);
        Assert.Equal(2, request.Names.Count);
        Assert.Contains(first, context.Workspace.GetDocuments());
        Assert.Contains(second, context.Workspace.GetDocuments());
        Assert.False(first.ClosingToken.IsCancellationRequested);
        Assert.False(second.ClosingToken.IsCancellationRequested);
        Assert.False(coordinator.IsClosing(first));
        Assert.False(coordinator.IsClosing(second));
    }

    [Fact]
    public async Task 范围关闭先等待干净文档命令且许可撤销可重新获得命令()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var document = await CreateDirtyDocumentAsync(context, dirty: false);
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var commands = context.Provider.GetRequiredService<WorkbenchDocumentCommandLeaseStore>();
        Assert.True(commands.TryAcquire(document, out var command));
        var closing = coordinator.PrepareRangeCloseAsync([document]);
        Assert.False(closing.IsCompleted);
        Assert.True(command!.ClosingToken.IsCancellationRequested);
        Assert.False(document.ClosingToken.IsCancellationRequested);
        Assert.False(commands.TryAcquire(document, out _));
        command.Dispose();
        using var approval = await closing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(approval);
        Assert.True(coordinator.TryBeginDockClose(document, () => throw new InvalidOperationException()));
        Assert.Empty(context.Interactions.CloseRequests);
        approval.Dispose();
        approval.Dispose();
        Assert.False(coordinator.IsClosing(document));
        Assert.True(commands.TryAcquire(document, out var reopened));
        reopened!.Dispose();
    }

    [Fact]
    public async Task 范围关闭等待期间出现新修改则撤销所有许可()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var document = await CreateDirtyDocumentAsync(context, dirty: false);
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var commands = context.Provider.GetRequiredService<WorkbenchDocumentCommandLeaseStore>();
        Assert.True(commands.TryAcquire(document, out var command));
        var closing = coordinator.PrepareRangeCloseAsync([document]);
        Assert.IsType<TestSavableDocument>(document.Model).IsModified = true;
        command!.Dispose();
        Assert.Null(await closing.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(coordinator.IsClosing(document));
        Assert.Contains(context.Interactions.Errors, error => error.Contains("再次保存", StringComparison.Ordinal));
        Assert.False(document.ClosingToken.IsCancellationRequested);
    }

    [Fact]
    public async Task 范围关闭与主窗口和单页关闭互斥且取消后可以重试()
    {
        using var context = DocumentTestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var document = await CreateDirtyDocumentAsync(context);
        var coordinator = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var pending = new TaskCompletionSource<DocumentCloseChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Interactions.PendingCloseChoice = pending;
        var closing = coordinator.PrepareRangeCloseAsync([document]);
        Assert.Null(await coordinator.PrepareRangeCloseAsync([document]));
        Assert.False(await coordinator.ConfirmWindowCloseAsync([document]));
        Assert.False(coordinator.TryBeginDockClose(document, () => throw new InvalidOperationException()));
        pending.SetResult(DocumentCloseChoice.Cancel);
        Assert.Null(await closing.WaitAsync(TimeSpan.FromSeconds(5)));
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);
        using var next = await coordinator.PrepareRangeCloseAsync([document]);
        Assert.NotNull(next);
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
