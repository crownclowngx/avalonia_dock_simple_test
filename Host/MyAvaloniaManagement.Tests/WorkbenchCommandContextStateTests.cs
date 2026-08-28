using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Context;
using MyAvaloniaManagement.Business.Commands.State;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 G3 Context v1、revision 和当前 Target 定向订阅。</summary>
public sealed class WorkbenchCommandContextStateTests
{
    [Fact]
    public void 空Context从零开始且不携带任何运行期对象()
    {
        using var store = new WorkbenchContextStore();

        var capture = store.Capture();

        Assert.False(capture.Snapshot.HasActiveDocument);
        Assert.Null(capture.Snapshot.ActiveDocumentTypeId);
        Assert.Null(capture.Snapshot.ActiveDocumentOwnerId);
        Assert.False(capture.Snapshot.IsActiveDocumentPersistable);
        Assert.Equal(0, capture.Snapshot.Revision);
        Assert.Null(capture.Document);
        Assert.Null(capture.Target);
        Assert.False(capture.ClosingToken.CanBeCanceled);
    }

    [Fact]
    public async Task Host与插件Document切换只在实例变化时递增Revision()
    {
        using var context = WorkbenchCommandG3TestContext.Create(persistable: true);
        _ = context.CreateMainWindowViewModel();
        var store = context.Provider.GetRequiredService<WorkbenchContextStore>();
        var initial = store.Capture();
        Assert.False(initial.Snapshot.HasActiveDocument);
        Assert.Equal(0, initial.Snapshot.Revision);

        var welcome = Assert.Single(context.Workspace.GetDocuments(), item =>
            item.Registration.Descriptor.DocumentTypeId == HostExtensionIds.WelcomeDocument);
        context.Workspace.DockFactory.SetActiveDockable(welcome);
        var host = store.Capture();
        Assert.True(host.Snapshot.HasActiveDocument);
        Assert.Equal(HostExtensionIds.WelcomeDocument, host.Snapshot.ActiveDocumentTypeId);
        Assert.Null(host.Snapshot.ActiveDocumentOwnerId);
        Assert.False(host.Snapshot.IsActiveDocumentPersistable);

        var first = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "first");
        context.Workspace.DockFactory.SetActiveDockable(first);
        var firstCapture = store.Capture();
        Assert.Same(first, firstCapture.Document);
        Assert.Equal(WorkbenchCommandG3TestContext.DocumentType, firstCapture.Snapshot.ActiveDocumentTypeId);
        Assert.Equal(TestPluginIds.Owner, firstCapture.Snapshot.ActiveDocumentOwnerId);
        Assert.True(firstCapture.Snapshot.IsActiveDocumentPersistable);
        Assert.NotNull(firstCapture.Target);

        context.Workspace.DockFactory.SetActiveDockable(first);
        Assert.Equal(firstCapture.Snapshot.Revision, store.Capture().Snapshot.Revision);

        var second = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "second");
        context.Workspace.DockFactory.SetActiveDockable(second);
        var secondCapture = store.Capture();
        Assert.Same(second, secondCapture.Document);
        Assert.True(secondCapture.Snapshot.Revision > firstCapture.Snapshot.Revision);

        var tool = context.Workspace.CreatedTools.Values.First();
        context.Workspace.DockFactory.SetActiveDockable(tool);
        Assert.Equal(secondCapture.Snapshot.Revision, store.Capture().Snapshot.Revision);
        Assert.Same(second, store.Capture().Document);
    }

    [Fact]
    public async Task 当前Target切换时成对退订且迟到事件被Revision隔离()
    {
        using var context = WorkbenchCommandG3TestContext.Create();
        var firstAdapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "first");
        var secondAdapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "second");
        var first = Assert.IsType<WorkbenchCommandG3Document>(firstAdapter.Model);
        var second = Assert.IsType<WorkbenchCommandG3Document>(secondAdapter.Model);
        var states = context.Provider.GetRequiredService<WorkbenchCommandStateQuery>();
        var invalidations = new List<WorkbenchCommandStateInvalidatedEventArgs>();
        states.StateInvalidated += (_, args) => invalidations.Add(args);

        context.Workspace.DockFactory.SetActiveDockable(firstAdapter);
        Assert.Equal(1, first.SubscriberCount);
        Assert.Equal(0, second.SubscriberCount);
        invalidations.Clear();

        var eventThread = 0;
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        states.StateInvalidated += (_, args) =>
        {
            if (args.CommandId == WorkbenchCommandG3TestContext.Command)
            {
                eventThread = Environment.CurrentManagedThreadId;
                observed.TrySetResult();
            }
        };
        var raisedThread = 0;
        await Task.Run(() =>
        {
            raisedThread = Environment.CurrentManagedThreadId;
            first.RaiseStateChanged(WorkbenchCommandG3TestContext.Command);
        });
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(raisedThread, eventThread);
        Assert.Contains(invalidations, item =>
            !item.IsFullRefresh && item.CommandId == WorkbenchCommandG3TestContext.Command);

        context.Workspace.DockFactory.SetActiveDockable(secondAdapter);
        Assert.Equal(0, first.SubscriberCount);
        Assert.Equal(1, second.SubscriberCount);
        invalidations.Clear();
        first.RaiseLateStateChanged(WorkbenchCommandG3TestContext.Command);
        second.RaiseStateChanged(WorkbenchCommandG3TestContext.UnknownCommand);
        Assert.Empty(invalidations);
    }

    [Fact]
    public async Task 状态查询区分目标缺失禁用与查询异常并写脱敏诊断()
    {
        var diagnostics = new RecordingWorkbenchCommandDiagnosticSink();
        using var context = WorkbenchCommandG3TestContext.Create(diagnostics);
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "state");
        context.Workspace.DockFactory.SetActiveDockable(adapter);
        var target = Assert.IsType<WorkbenchCommandG3Document>(adapter.Model);
        var states = context.Provider.GetRequiredService<WorkbenchCommandStateQuery>();

        Assert.Equal(
            WorkbenchCommandStateStatus.Enabled,
            states.Query(WorkbenchCommandG3TestContext.Command).Status);
        target.AllowExecute = false;
        Assert.Equal(
            WorkbenchCommandStateStatus.Disabled,
            states.Query(WorkbenchCommandG3TestContext.Command).Status);
        target.ThrowOnCanExecute = true;
        Assert.Equal(
            WorkbenchCommandStateStatus.Disabled,
            states.Query(WorkbenchCommandG3TestContext.Command).Status);
        var diagnostic = Assert.Single(diagnostics.Drafts, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandTargetStateFailed);
        Assert.Equal(WorkbenchCommandG3TestContext.Command.Value, diagnostic.StableId);
        Assert.Equal(TestPluginIds.Owner, diagnostic.PluginId);
    }

    [Fact]
    public async Task 普通Document没有Target能力时返回TargetUnavailable()
    {
        using var context = WorkbenchCommandG3TestContext.Create(targetImplemented: false);
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "plain");
        context.Workspace.DockFactory.SetActiveDockable(adapter);

        var state = context.Provider
            .GetRequiredService<WorkbenchCommandStateQuery>()
            .Query(WorkbenchCommandG3TestContext.Command);

        Assert.Equal(WorkbenchCommandStateStatus.TargetUnavailable, state.Status);
    }

    [Fact]
    public async Task Target事件访问器失败会禁用命令并隔离异常()
    {
        var diagnostics = new RecordingWorkbenchCommandDiagnosticSink();
        using var context = WorkbenchCommandG3TestContext.Create(diagnostics);
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "event-failure");
        var target = Assert.IsType<WorkbenchCommandG3Document>(adapter.Model);
        target.ThrowOnEventAdd = true;
        context.Workspace.DockFactory.SetActiveDockable(adapter);

        var state = context.Provider
            .GetRequiredService<WorkbenchCommandStateQuery>()
            .Query(WorkbenchCommandG3TestContext.Command);

        Assert.Equal(WorkbenchCommandStateStatus.Disabled, state.Status);
        Assert.Equal(0, target.SubscriberCount);
        Assert.Contains(diagnostics.Drafts, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandTargetSubscriptionFailed &&
            item.StableId == WorkbenchCommandG3TestContext.DocumentType.Value);
    }

    [Fact]
    public async Task Host状态观察者异常不会返回插件工作线程()
    {
        var diagnostics = new RecordingWorkbenchCommandDiagnosticSink();
        using var context = WorkbenchCommandG3TestContext.Create(diagnostics);
        var adapter = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "observer");
        context.Workspace.DockFactory.SetActiveDockable(adapter);
        var target = Assert.IsType<WorkbenchCommandG3Document>(adapter.Model);
        var states = context.Provider.GetRequiredService<WorkbenchCommandStateQuery>();
        states.StateInvalidated += static (_, _) =>
            throw new InvalidOperationException("observer-secret");

        var exception = Record.Exception(() =>
            target.RaiseStateChanged(WorkbenchCommandG3TestContext.Command));

        Assert.Null(exception);
        Assert.Contains(diagnostics.Drafts, item =>
            item.Code == HostDiagnosticCodes.WorkbenchCommandStateObserverFailed &&
            item.StableId == WorkbenchCommandG3TestContext.Command.Value);
    }
}
