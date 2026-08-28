using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 G3 Executor 严格路由当前实例并与单 Document 关闭安全协调。</summary>
public sealed class WorkbenchCommandDocumentTargetTests
{
    [Fact]
    public async Task 同类型多个Document只执行当前活动实例()
    {
        using var context = WorkbenchCommandG3TestContext.Create();
        var firstAdapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "first");
        var secondAdapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "second");
        var first = Assert.IsType<WorkbenchCommandG3Document>(firstAdapter.Model);
        var second = Assert.IsType<WorkbenchCommandG3Document>(secondAdapter.Model);
        var executor = context.Provider.GetRequiredService<WorkbenchCommandExecutor>();

        context.Workspace.DockFactory.SetActiveDockable(firstAdapter);
        var firstResult = await executor.ExecuteAsync(WorkbenchCommandG3TestContext.Command);
        context.Workspace.DockFactory.SetActiveDockable(secondAdapter);
        var secondResult = await executor.ExecuteAsync(WorkbenchCommandG3TestContext.Command);

        Assert.Equal(WorkbenchCommandExecutionStatus.Succeeded, firstResult.Status);
        Assert.Equal(WorkbenchCommandExecutionStatus.Succeeded, secondResult.Status);
        Assert.Equal(1, first.ExecutionCount);
        Assert.Equal(1, second.ExecutionCount);
        Assert.Equal(WorkbenchCommandG3TestContext.Command, first.LastExecutedCommand);
        Assert.Equal(WorkbenchCommandG3TestContext.Command, second.LastExecutedCommand);
    }

    [Fact]
    public async Task CanExecuteFalse和无匹配活动目标分别稳定拒绝()
    {
        using var context = WorkbenchCommandG3TestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var executor = context.Provider.GetRequiredService<WorkbenchCommandExecutor>();

        var unavailable = await executor.ExecuteAsync(WorkbenchCommandG3TestContext.Command);
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "disabled");
        context.Workspace.DockFactory.SetActiveDockable(adapter);
        Assert.IsType<WorkbenchCommandG3Document>(adapter.Model).AllowExecute = false;
        var disabled = await executor.ExecuteAsync(WorkbenchCommandG3TestContext.Command);

        Assert.Equal(WorkbenchCommandExecutionStatus.TargetUnavailable, unavailable.Status);
        Assert.Equal(WorkbenchCommandExecutionStatus.CommandDisabled, disabled.Status);
    }

    [Fact]
    public async Task 插件异常和非关联取消均失败且不泄漏异常正文()
    {
        var diagnostics = new RecordingWorkbenchCommandDiagnosticSink();
        using var context = WorkbenchCommandG3TestContext.Create(diagnostics);
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "failure");
        context.Workspace.DockFactory.SetActiveDockable(adapter);
        var target = Assert.IsType<WorkbenchCommandG3Document>(adapter.Model);
        var executor = context.Provider.GetRequiredService<WorkbenchCommandExecutor>();

        target.ExecuteException = new InvalidOperationException("secret C:\\private\\target.json");
        var failed = await executor.ExecuteAsync(WorkbenchCommandG3TestContext.Command);
        target.ExecuteException = new OperationCanceledException("unrelated-secret");
        var unrelatedCancellation = await executor.ExecuteAsync(
            WorkbenchCommandG3TestContext.Command);

        Assert.Equal(WorkbenchCommandExecutionStatus.Failed, failed.Status);
        Assert.Equal("插件命令执行失败；插件异常正文未写入诊断。", failed.UserMessage);
        Assert.DoesNotContain("secret", failed.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WorkbenchCommandExecutionStatus.Failed, unrelatedCancellation.Status);
        Assert.Equal(2, diagnostics.Drafts.Count(item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandExecutionFailed));
    }

    [Fact]
    public async Task 调用者取消链接到Target并映射Canceled()
    {
        using var context = WorkbenchCommandG3TestContext.Create();
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "caller-cancel");
        context.Workspace.DockFactory.SetActiveDockable(adapter);
        var target = Assert.IsType<WorkbenchCommandG3Document>(adapter.Model);
        target.BlockUntilCanceled = true;
        target.ReleaseAfterCancellation.TrySetResult();
        using var cancellation = new CancellationTokenSource();

        var running = context.Provider
            .GetRequiredService<WorkbenchCommandExecutor>()
            .ExecuteAsync(WorkbenchCommandG3TestContext.Command, cancellation.Token)
            .AsTask();
        await target.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WorkbenchCommandExecutionStatus.Canceled, result.Status);
        Assert.True(target.CancellationObservedBeforeDispose);
    }

    [Fact]
    public async Task HostShutdown取消插件Target并排空全局调用()
    {
        using var context = WorkbenchCommandG3TestContext.Create();
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "shutdown");
        context.Workspace.DockFactory.SetActiveDockable(adapter);
        var target = Assert.IsType<WorkbenchCommandG3Document>(adapter.Model);
        target.BlockUntilCanceled = true;
        target.ReleaseAfterCancellation.TrySetResult();
        var executor = context.Provider.GetRequiredService<WorkbenchCommandExecutor>();
        var running = executor.ExecuteAsync(WorkbenchCommandG3TestContext.Command).AsTask();
        await target.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        executor.BeginShutdown();

        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WorkbenchCommandExecutionStatus.Canceled, result.Status);
        Assert.True(await executor.WaitForDrainAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(
            WorkbenchCommandExecutionStatus.RejectedDuringShutdown,
            (await executor.ExecuteAsync(WorkbenchCommandG3TestContext.Command)).Status);
    }

    [Fact]
    public async Task 关闭先取消并排空Target再释放Scope且拒绝迟到调用()
    {
        using var context = WorkbenchCommandG3TestContext.Create();
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "close-drain");
        context.Workspace.DockFactory.SetActiveDockable(adapter);
        var target = Assert.IsType<WorkbenchCommandG3Document>(adapter.Model);
        target.BlockUntilCanceled = true;
        var executor = context.Provider.GetRequiredService<WorkbenchCommandExecutor>();
        var running = executor.ExecuteAsync(WorkbenchCommandG3TestContext.Command).AsTask();
        await target.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(context.Workspace.DockFactory.OnDockableClosing(adapter));
        await target.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(target.CancellationObservedBeforeDispose);
        Assert.Contains(adapter, context.Workspace.GetDocuments());
        Assert.False(target.Disposed.Task.IsCompleted);

        var late = await executor.ExecuteAsync(WorkbenchCommandG3TestContext.Command);
        Assert.Equal(WorkbenchCommandExecutionStatus.TargetUnavailable, late.Status);
        target.ReleaseAfterCancellation.TrySetResult();

        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WorkbenchCommandExecutionStatus.Canceled, result.Status);
        await target.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, target.SubscriberCount);
        Assert.DoesNotContain(adapter, context.Workspace.GetDocuments());
    }

    [Fact]
    public async Task Dock拒绝关闭后租约恢复接受新调用()
    {
        var leases = new WorkbenchDocumentCommandLeaseStore();
        using var context = WorkbenchCommandG3TestContext.Create();
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "reopen");

        Assert.True(leases.BeginClose(adapter).IsCompletedSuccessfully);
        Assert.False(leases.TryAcquire(adapter, out _));
        leases.Reopen(adapter);
        Assert.True(leases.TryAcquire(adapter, out var lease));
        lease!.Dispose();
        Assert.True(leases.BeginClose(adapter).IsCompletedSuccessfully);
        leases.CompleteClose(adapter);
    }

    [Fact]
    public async Task 活动Lease阻止恢复和强制完成且取消回调异常被诊断隔离()
    {
        var diagnostics = new RecordingWorkbenchCommandDiagnosticSink();
        var leases = new WorkbenchDocumentCommandLeaseStore(diagnostics);
        using var context = WorkbenchCommandG3TestContext.Create();
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "lease-errors");
        Assert.True(leases.TryAcquire(adapter, out var lease));
        using var registration = lease!.ClosingToken.Register(static () =>
            throw new InvalidOperationException("cancel-secret"));

        var drain = leases.BeginClose(adapter);

        Assert.False(drain.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => leases.Reopen(adapter));
        Assert.Throws<InvalidOperationException>(() => leases.CompleteClose(adapter));
        Assert.Contains(diagnostics.Drafts, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandDocumentCloseCancellationFailed);
        lease.Dispose();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        leases.CompleteClose(adapter);
    }

    [Fact]
    public async Task 干净Document排空后的重试异常会恢复命令入口()
    {
        using var context = WorkbenchCommandG3TestContext.Create();
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "retry-error");
        var leases = context.Provider.GetRequiredService<WorkbenchDocumentCommandLeaseStore>();
        var close = context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        Assert.True(leases.TryAcquire(adapter, out var lease));

        Assert.False(close.TryBeginDockClose(
            adapter,
            static () => throw new InvalidOperationException("retry-secret")));
        lease!.Dispose();

        await WaitUntilAsync(() => context.Interactions.Errors.Count != 0);
        Assert.Contains(context.Interactions.Errors, message =>
            message.Contains("保持打开", StringComparison.Ordinal));
        Assert.True(leases.TryAcquire(adapter, out var reopened));
        reopened!.Dispose();
        Assert.True(close.TryBeginDockClose(adapter, static () => { }));
        close.ReopenAfterDockRejection(adapter);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = TimeProvider.System.GetTimestamp() +
            TimeProvider.System.TimestampFrequency * 5;
        while (!predicate())
        {
            if (TimeProvider.System.GetTimestamp() >= timeout)
            {
                throw new TimeoutException("等待 G3 异步关闭断言超时。");
            }
            await Task.Yield();
        }
    }
}
