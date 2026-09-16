using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Tests;

public sealed class DockLayoutWorkspaceStateTests
{
    [Fact]
    public void 默认隐藏工具仍有完整布局位置且连续捕获身份稳定()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var first = context.Workspace.LayoutState.Capture(context.Workspace);
        var second = context.Workspace.LayoutState.Capture(context.Workspace);
        Assert.NotEmpty(first.Tools);
        Assert.All(first.Tools, tool => Assert.Equal("hidden", tool.State));
        Assert.Equal(DockLayoutV3Tests.Write(first), DockLayoutV3Tests.Write(second));
        Assert.Single(DockLayoutTree.Enumerate(first.MainWindow.Root), node => node.Kind == "documents");
    }

    [Fact]
    public void 隐藏最后一个浮窗工具后仍保留正常位置和原组()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var session = context.Workspace;
        var factory = session.DockFactory;
        // 此处验证纯模型捕获；实际原生窗口创建另由 HostFloatingWindow 的 Headless 场景覆盖。
        factory.HostWindowLocator![nameof(IDockWindow)] = () => null;
        Assert.True(session.ShowTool(HostExtensionIds.FileSystemTree));
        session.LayoutState.Capture(session);
        var tool = session.CreatedTools[HostExtensionIds.FileSystemTree.Value];
        factory.RemoveDockable(tool, collapse: false);
        var group = new ToolDock { VisibleDockables = factory.CreateList<IDockable>(tool), ActiveDockable = tool };
        var window = new DockWindow
        {
            Layout = new RootDock { VisibleDockables = factory.CreateList<IDockable>(group), ActiveDockable = group },
            X = -1300, Y = 60, Width = 600, Height = 400,
        };
        factory.AddWindow(session.RootDock!, window);
        var before = session.LayoutState.Capture(session);
        var remembered = Assert.Single(before.FloatingWindows);
        factory.HideDockable(tool);
        var hidden = session.LayoutState.Capture(session);
        var restored = Assert.Single(hidden.FloatingWindows);
        Assert.Equal(remembered.Id, restored.Id);
        Assert.Equal(remembered.Root.Id, restored.Root.Id);
        Assert.Equal(remembered.Bounds, restored.Bounds);
        Assert.Equal("hidden", hidden.Tools.Single(item => item.Id == tool.Id).State);
    }

    [Fact]
    public void 纯文档浮窗和文档标题不进入布局而自动隐藏保留工具状态()
    {
        using var context = new TestHostContext();
        _ = context.CreateMainWindowViewModel();
        var session = context.Workspace;
        var factory = session.DockFactory;
        // 此处验证纯模型捕获；实际原生窗口创建另由 HostFloatingWindow 的 Headless 场景覆盖。
        factory.HostWindowLocator![nameof(IDockWindow)] = () => null;
        var page = new Document { Id = "private-page-id", Title = "private-document-title" };
        var documentDock = new DocumentDock { VisibleDockables = factory.CreateList<IDockable>(page) };
        factory.AddWindow(session.RootDock!, new DockWindow
        {
            Layout = new RootDock { VisibleDockables = factory.CreateList<IDockable>(documentDock), ActiveDockable = documentDock },
        });
        Assert.True(session.ShowTool(HostExtensionIds.FileSystemTree));
        var tool = session.CreatedTools[HostExtensionIds.FileSystemTree.Value];
        factory.PinDockable(tool);
        var snapshot = session.LayoutState.Capture(session);
        Assert.Empty(snapshot.FloatingWindows);
        Assert.Equal("autoHidden", snapshot.Tools.Single(item => item.Id == tool.Id).State);
        var json = DockLayoutV3Tests.Write(snapshot);
        Assert.DoesNotContain(page.Id, json, StringComparison.Ordinal);
        Assert.DoesNotContain(page.Title!, json, StringComparison.Ordinal);
    }
}
