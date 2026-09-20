using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

public sealed partial class DocumentWindowV16UiTests
{
    [AvaloniaTheory]
    [InlineData("command")]
    [InlineData("menu")]
    [InlineData("native")]
    public async Task C02关闭入口回收同一原生浮窗(string entry)
    {
        await using var test = new DocumentWindowTestContext();
        var page = await test.Create();
        var host = await test.Float(page);
        if (entry == "command") test.Workspace.DockFactory.CloseDockable(page);
        else if (entry == "native") host.Close();
        else
        {
            var tab = await DocumentWindowTestContext.WaitForTab(host, page);
            var menu = Assert.IsType<ContextMenu>(tab.DocumentContextMenu);
            var menuOwner = tab.GetVisualDescendants().OfType<Control>().First(control => ReferenceEquals(control.ContextMenu, menu));
            menu.Open(menuOwner);
            await DocumentWindowTestContext.Flush();
            var close = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Header?.ToString() is "关闭" or "Close");
            Assert.Same(page, close.CommandParameter);
            Assert.True(close.Command!.CanExecute(close.CommandParameter));
            close.Command.Execute(close.CommandParameter);
            menu.Close();
        }
        await DocumentWindowTestContext.Flush();
        Assert.False(host.IsVisible);
        Assert.Empty(test.Workspace.DockFactory.HostWindows);
        Assert.Equal(1, test.State.Models.Single().DisposeCount);
    }

    [AvaloniaTheory]
    [InlineData("save", true)]
    [InlineData("discard", true)]
    [InlineData("cancel", false)]
    [InlineData("save-cancel", false)]
    [InlineData("save-fail", false)]
    [InlineData("save-edit", false)]
    public async Task C03C04保存决策只在成功后拆除文档(string choice, bool closed)
    {
        await using var test = new DocumentWindowTestContext();
        var page = await test.Create();
        var model = Assert.IsType<DocumentWindowTestContext.Model>(page.Model);
        var view = page.PreparedView;
        var host = await test.Float(page);
        var group = (IDock)page.Owner!;
        model.Edit();
        test.State.Choice = choice == "discard" ? DocumentCloseChoice.Discard :
            choice == "cancel" ? DocumentCloseChoice.Cancel : DocumentCloseChoice.Save;
        test.Context.Storage.SavePath = choice == "save-cancel" ? null : Path.Combine(test.Context.TempDirectory, "v16.mamdoc");
        if (choice == "save-fail") test.Context.Storage.WriteException = new IOException("V16 保存失败");
        model.EditDuringSave = choice == "save-edit";
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = test.Context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        coordinator.StateChanged += (_, _) => { if (!coordinator.IsClosing(page)) finished.TrySetResult(); };
        host.Closed += (_, _) => finished.TrySetResult();
        await DocumentWindowTestContext.ClickClose(host, page);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await DocumentWindowTestContext.Flush();
        Assert.Equal(!closed, host.IsVisible);
        Assert.Equal(1, test.State.Confirmations);
        Assert.Equal(closed ? 1 : 0, model.DisposeCount);
        if (!closed)
        {
            Assert.Same(view, page.PreparedView);
            Assert.Same(page, group.ActiveDockable);
            Assert.Contains(page, group.VisibleDockables!);
            Assert.False(coordinator.IsClosing(page));
            Assert.Single(test.Workspace.DockFactory.HostWindows);
            Assert.False(page.ClosingToken.IsCancellationRequested);
        }
    }

    [AvaloniaTheory]
    [InlineData("capability", false)]
    [InlineData("last", false)]
    [InlineData("event", false)]
    [InlineData("capability", true)]
    [InlineData("last", true)]
    [InlineData("event", true)]
    public async Task C05单页和整窗关闭均遵守能力及可取消事件(string restriction, bool native)
    {
        await using var test = new DocumentWindowTestContext();
        var page = await test.Create();
        var host = await test.Float(page);
        var group = (IDock)page.Owner!;
        var reject = true;
        var count = 0;
        test.Workspace.DockFactory.DockableClosing += (_, args) =>
        {
            if (!ReferenceEquals(args.Dockable, page)) return;
            count++;
            if (restriction == "event" && reject) args.Cancel = true;
        };
        page.CanClose = restriction != "capability";
        group.CanCloseLastDockable = restriction != "last";
        try
        {
            if (native) host.Close(); else test.Workspace.DockFactory.CloseDockable(page);
            await DocumentWindowTestContext.Flush();
            Assert.True(host.IsVisible);
            Assert.Same(page, Assert.Single(group.VisibleDockables!));
            Assert.False(test.Context.Provider.GetRequiredService<DocumentCloseCoordinator>().IsClosing(page));
            reject = false; page.CanClose = true; group.CanCloseLastDockable = true;
            var before = count;
            if (native) host.Close(); else test.Workspace.DockFactory.CloseDockable(page);
            await DocumentWindowTestContext.Flush();
            Assert.False(host.IsVisible);
            Assert.Equal(before + 1, count);
        }
        finally { reject = false; page.CanClose = true; group.CanCloseLastDockable = true; }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task C06原生首次或重试取消后仍可重新关闭(int rejectedAttempt)
    {
        await using var test = new DocumentWindowTestContext();
        var page = await test.Create();
        var host = await test.Float(page);
        var group = page.Owner;
        var count = 0;
        EventHandler<WindowClosingEventArgs> reject = (_, args) => { if (++count == rejectedAttempt) args.Cancel = true; };
        host.Closing += reject;
        try
        {
            test.Workspace.DockFactory.CloseDockable(page);
            await DocumentWindowTestContext.Flush();
            Assert.True(host.IsVisible);
            Assert.Same(group, page.Owner);
            Assert.Single(test.Workspace.DockFactory.HostWindows);
            Assert.False(test.Context.Provider.GetRequiredService<DocumentCloseCoordinator>().IsClosing(page));
        }
        finally { host.Closing -= reject; }
        test.Workspace.DockFactory.CloseDockable(page);
        await DocumentWindowTestContext.Flush();
        Assert.False(host.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C07其他文档组或工具仍在时仅回收空组(bool tool)
    {
        await using var test = new DocumentWindowTestContext();
        var page = await test.Create();
        var host = await test.Float(page);
        var factory = test.Workspace.DockFactory;
        IDockable retained;
        IDock other;
        if (tool)
        {
            test.Workspace.ShowTool(HostExtensionIds.FileSystemTree);
            retained = test.Workspace.CreatedTools[HostExtensionIds.FileSystemTree.Value];
            other = new ToolDock { VisibleDockables = factory.CreateList<IDockable>() };
        }
        else
        {
            retained = await test.Create("另一组");
            other = new DocumentDock { VisibleDockables = factory.CreateList<IDockable>() };
        }
        factory.MoveDockable((IDock)retained.Owner!, other, retained, null);
        factory.SplitToDock((IDock)page.Owner!, other, DockOperation.Bottom);
        factory.CloseDockable(page);
        await DocumentWindowTestContext.Flush();
        Assert.True(host.IsVisible);
        Assert.True(DockTreeNavigator.IsDockableAttached(test.Workspace.RootDock!, retained));
        Assert.DoesNotContain(page, test.Workspace.GetDocuments());
    }

    [AvaloniaFact]
    public async Task C08主窗最后文档关闭后可重新新建()
    {
        await using var test = new DocumentWindowTestContext();
        foreach (var page in test.Workspace.GetDocuments()) test.Workspace.DockFactory.CloseDockable(page);
        Assert.Empty(test.Workspace.GetDocuments());
        Assert.True(test.Main.IsVisible);
        var created = await test.Create();
        Assert.True(DockTreeNavigator.IsDockableAttached(test.Workspace.RootDock!, created));
    }

    [AvaloniaFact]
    public async Task C09最后文档等待命令排空并去重关闭请求()
    {
        await using var test = new DocumentWindowTestContext();
        var page = await test.Create();
        var host = await test.Float(page);
        var leases = test.Context.Provider.GetRequiredService<WorkbenchDocumentCommandLeaseStore>();
        Assert.True(leases.TryAcquire(page, out var lease));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Closed += (_, _) => closed.TrySetResult();
        try
        {
            test.Workspace.DockFactory.CloseDockable(page);
            test.Workspace.DockFactory.CloseDockable(page);
            Assert.True(host.IsVisible);
            Assert.Equal(0, test.State.Models.Single().DisposeCount);
            lease!.Dispose();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(host.IsVisible);
            Assert.Equal(1, test.State.Models.Single().DisposeCount);
        }
        finally { lease!.Dispose(); }
    }

    [AvaloniaFact]
    public async Task C09单页等待期间变成最后一页可把已确认许可交给整窗()
    {
        await using var test = new DocumentWindowTestContext();
        var first = await test.Create();
        var second = await test.Create("相邻页");
        var host = await test.Float(first);
        var factory = test.Workspace.DockFactory;
        factory.MoveDockable((IDock)second.Owner!, (IDock)first.Owner!, second, null);
        Assert.IsType<DocumentWindowTestContext.Model>(first.Model).Edit();
        test.State.PendingChoice = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Closed += (_, _) => closed.TrySetResult();
        factory.CloseDockable(first);
        factory.CloseDockable(second);
        Assert.True(host.IsVisible);
        test.State.PendingChoice.SetResult(DocumentCloseChoice.Discard);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, test.State.Confirmations);
        Assert.All(test.State.Models, model => Assert.Equal(1, model.DisposeCount));
    }

    [AvaloniaFact]
    public async Task C10确认期间新增页面使旧关闭请求失效()
    {
        await using var test = new DocumentWindowTestContext();
        var page = await test.Create();
        var host = await test.Float(page);
        Assert.IsType<DocumentWindowTestContext.Model>(page.Model).Edit();
        test.State.PendingChoice = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = test.Context.Provider.GetRequiredService<DocumentCloseCoordinator>();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, _) => { if (!coordinator.IsClosing(page)) released.TrySetResult(); };
        test.Workspace.DockFactory.CloseDockable(page);
        var added = await test.Create("新加入");
        test.Workspace.DockFactory.MoveDockable((IDock)added.Owner!, (IDock)page.Owner!, added, null);
        test.State.PendingChoice.SetResult(DocumentCloseChoice.Discard);
        await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await DocumentWindowTestContext.Flush();
        Assert.True(host.IsVisible);
        Assert.Same(page.Owner, added.Owner);
        Assert.All(test.State.Models, model => Assert.Equal(0, model.DisposeCount));
        Assert.False(coordinator.IsClosing(page));
    }

    [AvaloniaFact]
    public async Task C11整窗关闭混合内容后工具原实例可恢复而文档只释放一次()
    {
        await using var test = new DocumentWindowTestContext();
        var page = await test.Create();
        var model = Assert.IsType<DocumentWindowTestContext.Model>(page.Model);
        var view = Assert.IsType<DocumentWindowTestContext.EditorView>(page.PreparedView);
        var host = await test.Float(page);
        var factory = test.Workspace.DockFactory;
        test.Workspace.ShowTool(HostExtensionIds.FileSystemTree);
        var tool = Assert.IsType<ManagedToolDockable>(test.Workspace.CreatedTools[HostExtensionIds.FileSystemTree.Value]);
        var toolView = tool.PreparedView;
        var group = new ToolDock { VisibleDockables = factory.CreateList<IDockable>() };
        factory.MoveDockable((IDock)tool.Owner!, group, tool, null);
        factory.SplitToDock((IDock)page.Owner!, group, DockOperation.Bottom);
        host.Close();
        await DocumentWindowTestContext.Flush();
        Assert.False(host.IsVisible);
        Assert.Empty(factory.HostWindows);
        Assert.Equal(1, model.DisposeCount);
        Assert.Equal(1, view.DisposeCount);
        Assert.DoesNotContain(page, test.Workspace.GetDocuments());
        test.Workspace.ShowTool(HostExtensionIds.FileSystemTree);
        await DocumentWindowTestContext.Flush();
        Assert.Same(tool, test.Workspace.CreatedTools[HostExtensionIds.FileSystemTree.Value]);
        Assert.Same(toolView, tool.PreparedView);
        Assert.NotNull(DockTreeNavigator.FindWindow(test.Workspace.RootDock!, tool));
        Assert.Single(factory.HostWindows);
    }
}
