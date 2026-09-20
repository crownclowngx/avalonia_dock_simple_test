using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Dock.Model;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

public sealed partial class DocumentWindowV16UiTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task N01主窗和两个浮窗的新建互不串组(int source)
    {
        await using var test = new DocumentWindowTestContext();
        var a = await test.Create("A");
        var b = await test.Create("B");
        var mainGroup = a.Owner;
        var wa = await test.Float(a);
        var wb = await test.Float(b);
        Window window = source == 0 ? test.Main : source == 1 ? wa : wb;
        var expected = source == 0 ? mainGroup : source == 1 ? a.Owner : b.Owner;
        var before = test.Workspace.GetDocuments().ToHashSet();
        var palette = await DocumentWindowTestContext.OpenPalette(window);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await palette.CurrentExecution;
        var added = Assert.Single(test.Workspace.GetDocuments(), page => !before.Contains(page));
        Assert.Same(expected, added.Owner);
        Assert.Equal(before.Count + 1, test.Workspace.GetDocuments().Count);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task N02N03多分组与工具焦点使用同窗最近活动文档组(bool floating, bool toolFocus)
    {
        await using var test = new DocumentWindowTestContext();
        var first = await test.Create("第一组");
        var second = await test.Create("第二组");
        var factory = test.Workspace.DockFactory;
        Window window = test.Main;
        if (floating)
        {
            window = await test.Float(first);
            factory.MoveDockable((IDock)second.Owner!, (IDock)first.Owner!, second, null);
        }
        var original = (IDock)second.Owner!;
        Assert.True(new DockService().SplitDockable(second, original, original, DockOperation.Right, true));
        await DocumentWindowTestContext.WaitForTab(window, second);
        await DocumentWindowTestContext.Flush();
        test.Workspace.ActivateDockable(second);
        var expected = second.Owner;
        if (toolFocus)
        {
            test.Workspace.ShowTool(HostExtensionIds.FileSystemTree);
            var tool = test.Workspace.CreatedTools[HostExtensionIds.FileSystemTree.Value];
            if (floating)
            {
                var toolGroup = new ToolDock { VisibleDockables = factory.CreateList<IDockable>() };
                factory.MoveDockable((IDock)tool.Owner!, toolGroup, tool, null);
                factory.SplitToDock((IDock)first.Owner!, toolGroup, DockOperation.Bottom);
            }
            // Tool 的活动事件不能覆盖刚才的文档组记录。
            test.Workspace.ActivateDockable(second);
            test.Workspace.ActivateDockable(tool);
        }
        var before = test.Workspace.GetDocuments().ToHashSet();
        Assert.Same(expected, test.Workspace.CaptureDocumentCreationTarget(factory.WindowContext.GetLayout(window)!).PreferredDock);
        var palette = await DocumentWindowTestContext.OpenPalette(window);
        await palette.ExecuteSelectionAsync();
        var added = Assert.Single(test.Workspace.GetDocuments(), page => !before.Contains(page));
        Assert.Same(expected, added.Owner);
    }

    [AvaloniaFact]
    public async Task N02点击另一组已选中页面仍更新新建落点()
    {
        await using var test = new DocumentWindowTestContext();
        var first = await test.Create("第一组");
        var second = await test.Create("第二组");
        var original = (IDock)second.Owner!;
        Assert.True(new DockService().SplitDockable(second, original, original, DockOperation.Right, true));
        await DocumentWindowTestContext.WaitForTab(test.Main, first);
        await DocumentWindowTestContext.WaitForTab(test.Main, second);
        await DocumentWindowTestContext.Flush();
        test.Workspace.ActivateDockable(second);
        // 两组各自已有选中页，点击第一组内容只改变 FocusedDockable，不改变 ActiveDockable。
        var editor = Assert.IsType<DocumentWindowTestContext.EditorView>(first.PreparedView).Editor;
        test.Main.UpdateLayout();
        var point = editor.TranslatePoint(new Point(5, 5), test.Main)!.Value;
        test.Main.MouseDown(point, MouseButton.Left);
        test.Main.MouseUp(point, MouseButton.Left);
        var before = test.Workspace.GetDocuments().ToHashSet();
        var palette = await DocumentWindowTestContext.OpenPalette(test.Main);
        await palette.ExecuteSelectionAsync();
        Assert.Same(first.Owner, Assert.Single(test.Workspace.GetDocuments(), page => !before.Contains(page)).Owner);
    }

    [AvaloniaFact]
    public async Task N04纯工具浮窗新建回到主窗活动组且不改造工具窗口()
    {
        await using var test = new DocumentWindowTestContext();
        var page = await test.Create();
        var other = await test.Create("主窗其他组");
        var original = (IDock)other.Owner!;
        Assert.True(new DockService().SplitDockable(other, original, original, DockOperation.Right, true));
        await DocumentWindowTestContext.WaitForTab(test.Main, other);
        await DocumentWindowTestContext.Flush();
        test.Workspace.ActivateDockable(other);
        var factory = test.Workspace.DockFactory;
        test.Workspace.ShowTool(HostExtensionIds.FileSystemTree);
        var tool = test.Workspace.CreatedTools[HostExtensionIds.FileSystemTree.Value];
        factory.FloatDockable(tool);
        var host = Assert.IsType<HostFloatingWindow>(DockTreeNavigator.FindWindow(test.Workspace.RootDock!, tool)!.Host);
        var expected = other.Owner;
        var palette = await DocumentWindowTestContext.OpenPalette(host);
        await palette.ExecuteSelectionAsync();
        Assert.Same(expected, test.Workspace.GetActiveDocument()!.Owner);
        Assert.Same(tool, Assert.Single(DockTreeNavigator.Enumerate(host.Window!.Layout!), item => item is Dock.Model.Controls.ITool or Dock.Model.Controls.IDocument));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task N05初始化或串行门等待期间切窗不改变落点(bool queued)
    {
        await using var test = new DocumentWindowTestContext();
        var a = await test.Create("A");
        var b = await test.Create("B");
        var wa = await test.Float(a);
        var wb = await test.Float(b);
        var expected = a.Owner;
        var blocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? occupied = null;
        if (queued) occupied = test.Context.Provider.GetRequiredService<DocumentOperationGate>().RunAsync(async () => { await blocker.Task; return true; });
        else test.State.Blocker = blocker;
        var palette = await DocumentWindowTestContext.OpenPalette(wa);
        wa.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        var pending = palette.CurrentExecution;
        try
        {
            Assert.False(pending.IsCompleted);
            wb.Activate();
            var editor = Assert.IsType<DocumentWindowTestContext.EditorView>(b.PreparedView).Editor;
            wb.UpdateLayout(); editor.Focus();
            blocker.SetResult();
            if (occupied is not null) await occupied;
            await pending;
            await DocumentWindowTestContext.Flush();
            var added = Assert.Single(test.Workspace.GetDocuments(), page => page.Model is DocumentWindowTestContext.Model && page != a && page != b);
            Assert.Same(expected, added.Owner);
            Assert.True(wb.IsActive);
            Assert.False(Assert.IsType<DocumentWindowTestContext.EditorView>(added.PreparedView).Editor.IsFocused);
        }
        finally { blocker.TrySetResult(); if (occupied is not null) await occupied; await pending; }
    }

    [AvaloniaTheory]
    [InlineData("group")]
    [InlineData("window")]
    [InlineData("closing")]
    public async Task N06创建提交前原组移走或来源关闭按规则回退(string change)
    {
        await using var test = new DocumentWindowTestContext();
        var first = await test.Create("来源");
        var other = await test.Create("同窗另一组");
        var factory = test.Workspace.DockFactory;
        var mainGroup = (IDock)other.Owner!;
        var host = await test.Float(first);
        factory.MoveDockable((IDock)other.Owner!, (IDock)first.Owner!, other, null);
        var group = (IDock)other.Owner!;
        Assert.True(new DockService().SplitDockable(other, group, group, DockOperation.Right, true));
        test.Workspace.ActivateDockable(first);
        var source = host.Window!.Layout!;
        var target = test.Workspace.CaptureDocumentCreationTarget(source);
        test.State.Blocker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // 通过同一个生产创建用例控制等待，不绕过发布。面板 Busy 自身禁止关闭其来源窗，
        // 因此窗口失效分支在用例层验证，真实面板等待/切窗另由 N05 覆盖。
        var pending = test.Context.Provider.GetRequiredService<DocumentPersistenceCoordinator>()
            .CreateDocumentAsync(DocumentWindowTestContext.Type, target: target);
        object expected = mainGroup;
        if (change == "group")
        {
            factory.MoveDockable((IDock)first.Owner!, mainGroup, first, null);
            expected = other.Owner!;
        }
        else if (change == "window")
        {
            host.Close();
            await DocumentWindowTestContext.Flush();
            Assert.False(host.IsVisible);
        }
        else
        {
            Assert.IsType<DocumentWindowTestContext.Model>(first.Model).Edit();
            test.State.PendingChoice = new(TaskCreationOptions.RunContinuationsAsynchronously);
            host.Close();
            Assert.True(host.IsVisible);
        }
        test.State.Blocker.SetResult();
        var result = await pending;
        Assert.Equal(string.Empty, result.Error);
        var added = test.Workspace.GetDocuments().Single(page => page.PageId == result.CreatedPageId);
        Assert.Same(expected, added.Owner);
    }

    [AvaloniaFact]
    public async Task N08浮窗初始化失败不新增标签且原查询可重试()
    {
        await using var test = new DocumentWindowTestContext();
        var first = await test.Create();
        var host = await test.Float(first);
        var expected = first.Owner;
        test.State.FailInitialization = true;
        var palette = await DocumentWindowTestContext.OpenPalette(host);
        await palette.ExecuteSelectionAsync();
        Assert.False(palette.IsBusy);
        Assert.Contains("未新增页面", palette.FindControl<TextBlock>("OperationStatus")!.Text);
        Assert.Equal("V16 新建", palette.FindControl<TextBox>("SearchBox")!.Text);
        Assert.Equal(1, test.State.Models.Last().DisposeCount);
        Assert.Equal(2, test.Workspace.GetDocuments().Count);
        test.State.FailInitialization = false;
        await palette.ExecuteSelectionAsync();
        Assert.Same(expected, test.Workspace.GetActiveDocument()!.Owner);
        Assert.Equal(3, test.Workspace.GetDocuments().Count);
    }

    [AvaloniaFact]
    public async Task N09同一共享动作的并发请求分别保留目标()
    {
        await using var test = new DocumentWindowTestContext();
        var a = await test.Create("A");
        var b = await test.Create("B");
        var wa = await test.Float(a);
        var wb = await test.Float(b);
        var targetA = test.Workspace.CaptureDocumentCreationTarget(wa.Window!.Layout!);
        var targetB = test.Workspace.CaptureDocumentCreationTarget(wb.Window!.Layout!);
        test.State.Blocker = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = test.Context.Provider.GetRequiredService<DocumentPersistenceCoordinator>();
        var first = service.CreateDocumentAsync(DocumentWindowTestContext.Type, target: targetA);
        var second = service.CreateDocumentAsync(DocumentWindowTestContext.Type, target: targetB);
        test.State.Blocker.SetResult();
        var firstResult = await first;
        var secondResult = await second;
        Assert.Same(a.Owner, test.Workspace.GetDocuments().Single(page => page.PageId == firstResult.CreatedPageId).Owner);
        Assert.Same(b.Owner, test.Workspace.GetDocuments().Single(page => page.PageId == secondResult.CreatedPageId).Owner);
    }

    [AvaloniaFact]
    public async Task N09跨窗切换面板会话不会沿用上次来源()
    {
        await using var test = new DocumentWindowTestContext();
        var a = await test.Create("A");
        var b = await test.Create("B");
        var wa = await test.Float(a);
        var wb = await test.Float(b);
        _ = await DocumentWindowTestContext.OpenPalette(wa);
        var palette = await DocumentWindowTestContext.OpenPalette(wb);
        await palette.ExecuteSelectionAsync();
        Assert.Same(b.Owner, test.Workspace.GetActiveDocument()!.Owner);
        Assert.Single(((IDock)a.Owner!).VisibleDockables!);
    }

    [AvaloniaTheory]
    [InlineData("page")]
    [InlineData("close")]
    [InlineData("session")]
    public async Task N10迟到焦点恢复不能覆盖切页关页或新面板会话(string change)
    {
        await using var test = new DocumentWindowTestContext();
        var first = await test.Create();
        var palette = await DocumentWindowTestContext.OpenPalette(test.Main);
        await palette.ExecuteSelectionAsync();
        var added = test.Workspace.GetActiveDocument()!;
        var addedView = Assert.IsType<DocumentWindowTestContext.EditorView>(added.PreparedView);
        var addedModel = Assert.IsType<DocumentWindowTestContext.Model>(added.Model);
        Assert.NotSame(first, added);
        Control expected;
        if (change == "session")
        {
            // 直接打开新会话，暂不让 Background 队列消费旧的焦点恢复。
            test.Main.OpenCommandPalette();
            expected = palette.FindControl<TextBox>("SearchBox")!;
        }
        else
        {
            if (change == "close") test.Workspace.DockFactory.CloseDockable(added);
            Assert.True(test.Workspace.TryActivatePage(first.PageId));
            test.Main.UpdateLayout();
            expected = Assert.IsType<DocumentWindowTestContext.EditorView>(first.PreparedView).Editor;
            expected.Focus();
        }
        await DocumentWindowTestContext.Flush();
        Assert.True(expected.IsFocused);
        Assert.False(addedView.Editor.IsFocused);
        if (change == "close")
        {
            Assert.Null(added.PreparedView);
            Assert.Equal(1, addedView.DisposeCount);
            Assert.Equal(1, addedModel.DisposeCount);
        }
    }

    [AvaloniaFact]
    public async Task N11辅助窗口不作为来源且跨窗已有页只定位原实例()
    {
        await using var test = new DocumentWindowTestContext();
        var a = await test.Create("A");
        var b = await test.Create("B");
        var wa = await test.Float(a);
        var wb = await test.Float(b);
        var owner = a.Owner;
        var count = test.Workspace.GetDocuments().Count;
        var auxiliary = new Window();
        try
        {
            auxiliary.Show();
            auxiliary.Activate();
            var windows = test.Workspace.DockFactory.WindowContext;
            Assert.Null(windows.GetLayout(auxiliary));
            Assert.NotSame(auxiliary, windows.SelectOwner(auxiliary));
            var palette = await DocumentWindowTestContext.OpenPalette(wb);
            palette.FindControl<TextBox>("SearchBox")!.Text = string.Empty;
            await DocumentWindowTestContext.Flush();
            var list = palette.FindControl<ListBox>("PaletteItems")!;
            list.SelectedItem = Assert.Single(list.Items.OfType<WorkbenchCommandPaletteProjectionEntry>(),
                entry => entry.Identity is PagePaletteIdentity page && page.Id == a.PageId);
            await palette.ExecuteSelectionAsync();
            Assert.Same(a, test.Workspace.GetActiveDocument());
            Assert.Same(owner, a.Owner);
            Assert.Equal(count, test.Workspace.GetDocuments().Count);
            Assert.True(wa.IsActive);
        }
        finally { auxiliary.Close(); }
    }
}
