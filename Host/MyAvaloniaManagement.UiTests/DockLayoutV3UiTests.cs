using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>通过实际 HostWindow、Factory 初始化与现有 View 验证恢复，不直接把数据对象当恢复成功。</summary>
public sealed class DockLayoutV3UiTests
{
    [AvaloniaFact]
    public async Task 工具浮窗恢复复用原模型和View且关闭后按原组只显示目标工具()
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        var tool = Assert.IsType<ManagedToolDockable>(session.CreatedTools[HostExtensionIds.FileSystemTree.Value]);
        var other = session.CreatedTools[HostExtensionIds.PluginMenu.Value];
        var model = tool.Model;
        var view = tool.PreparedView;
        var document = session.GetDocuments().Single();
        var snapshot = Floating(visible: true);
        try
        {
            context.ViewModel.Layout = session.LayoutState.Apply(session, snapshot);
            await Flush();
            var first = Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            Assert.True(Assert.IsType<HostFloatingWindow>(first.Host).IsVisible);
            Assert.Same(model, tool.Model);
            Assert.Same(view, tool.PreparedView);
            Assert.Same(document, session.GetDocuments().Single());
            Assert.False(DockTreeNavigator.IsDockableAttached(session.RootDock!, other));
            Assert.Contains(tool, DockTreeNavigator.EnumerateWorkspace(session.RootDock!));
            var captured = session.LayoutState.Capture(session);
            Assert.Equal("saved-window", Assert.Single(captured.FloatingWindows).Id);
            Assert.Equal("saved-group", captured.FloatingWindows[0].Root.Id);

            Assert.IsType<HostFloatingWindow>(first.Host).Close();
            await Flush();
            Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            Assert.Equal("hidden", session.LayoutState.Capture(session).Tools.Single(item => item.Id == tool.Id).State);
            Assert.True(session.ShowTool(HostExtensionIds.FileSystemTree));
            await Flush();
            var restored = session.LayoutState.Capture(session);
            Assert.Equal("saved-window", Assert.Single(restored.FloatingWindows).Id);
            Assert.Equal("saved-group", restored.FloatingWindows[0].Root.Id);
            Assert.Equal("hidden", restored.Tools.Single(item => item.Id == other.Id).State);
            Assert.Same(model, tool.Model);
            Assert.Same(view, tool.PreparedView);
        }
        finally { await CloseWindows(context, main); }
    }

    [AvaloniaFact]
    public async Task 全隐藏或插件缺失的浮窗不创建空窗口但保留恢复记录()
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var main = new MainWindow(session.DockFactory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        try
        {
            var snapshot = Floating(visible: false);
            snapshot = snapshot with
            {
                FloatingWindows = [.. snapshot.FloatingWindows, new("missing-window", DockWindowBounds.Default,
                    DockLayoutNode.Group("missing-group", ["missing.plugin.tool"]))],
                Tools = [.. snapshot.Tools, new("missing.plugin.tool", "visible", DockLayoutIds.RightTools, 0)],
            };
            context.ViewModel.Layout = session.LayoutState.Apply(session, snapshot);
            await Flush();
            Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            var captured = session.LayoutState.Capture(session);
            Assert.Equal(2, captured.FloatingWindows.Count);
            Assert.Equal("visible", captured.Tools.Single(tool => tool.Id == "missing.plugin.tool").State);
            Assert.True(session.ShowTool(HostExtensionIds.FileSystemTree));
            await Flush();
            Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
        }
        finally { await CloseWindows(context, main); }
    }

    [AvaloniaFact]
    public async Task 应用工具布局不重开文档且把已有浮动文档原实例安全回停()
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        var document = session.GetDocuments().Single();
        var view = document.PreparedView;
        factory.RemoveDockable(document, collapse: false);
        var group = new DocumentDock { VisibleDockables = factory.CreateList<IDockable>(document), ActiveDockable = document };
        var root = new RootDock { VisibleDockables = factory.CreateList<IDockable>(group), ActiveDockable = group };
        var window = new DockWindow { Layout = root, Width = 700, Height = 500 };
        root.Window = window;
        factory.AddWindow(session.RootDock!, window);
        window.Present(false);
        var host = Assert.IsType<HostFloatingWindow>(window.Host);
        try
        {
            Assert.Empty(session.LayoutState.Capture(session).FloatingWindows);
            context.ViewModel.Layout = session.LayoutState.Apply(session, Floating(visible: false));
            await Flush();
            Assert.False(host.IsVisible);
            Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            Assert.Same(document, session.GetDocuments().Single());
            Assert.Same(view, document.PreparedView);
            Assert.False(document.ClosingToken.IsCancellationRequested);
            Assert.NotNull(DockTreeNavigator.FindDocumentDock(session.RootDock!, document));
        }
        finally { await CloseWindows(context, main); }
    }

    private static DockLayoutSnapshotV3 Floating(bool visible) => new(3,
        new("main", DockWindowBounds.Default, DockLayoutNode.Documents()),
        [new("saved-window", new(80, 60, 600, 400, false, null), DockLayoutNode.Group("saved-group",
            [HostExtensionIds.FileSystemTree.Value, HostExtensionIds.PluginMenu.Value]))],
        [new(HostExtensionIds.FileSystemTree.Value, visible ? "visible" : "hidden", DockLayoutIds.LeftTools, 0),
         new(HostExtensionIds.PluginMenu.Value, "hidden", DockLayoutIds.LeftTools, 1)]);

    private static Task Flush() => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();
    private static async Task CloseWindows(UiTestContext context, MainWindow main)
    {
        foreach (var window in DockTreeNavigator.EnumerateWindows(context.Workspace.RootDock!).ToArray())
            (window.Host as Window)?.Close();
        await Flush();
        main.Close();
    }
}
