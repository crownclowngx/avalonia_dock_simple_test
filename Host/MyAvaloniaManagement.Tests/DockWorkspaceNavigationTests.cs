using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Tests;

/// <summary>多窗口查找必须找到原实例，同时保留主窗口稳定 ID 的局部作用域。</summary>
public sealed class DockWorkspaceNavigationTests
{
    [Fact]
    public void 浮窗与嵌套浮窗可定位且窗口回边不会无限遍历()
    {
        var document = new Document { Id = "page" };
        var tool = new Tool { Id = "tool" };
        var group = new DocumentDock { Id = "Documents", VisibleDockables = [document] };
        var floatingRoot = new RootDock { VisibleDockables = [group] };
        var secondRoot = new RootDock { VisibleDockables = [new ToolDock { VisibleDockables = [tool] }] };
        var root = new RootDock { VisibleDockables = [new DocumentDock { Id = "Documents" }] };
        var window = new DockWindow { Layout = floatingRoot };
        root.Windows = [window];
        floatingRoot.Windows = [new DockWindow { Layout = secondRoot }];
        secondRoot.Windows = [new DockWindow { Layout = root }];

        Assert.Same(group, DockTreeNavigator.FindDocumentDock(root, document));
        Assert.NotSame(group, DockTreeNavigator.FindDockById<DocumentDock>(root, "Documents"));
        Assert.Same(window, DockTreeNavigator.FindWindow(root, document));
        Assert.True(DockTreeNavigator.IsDockableAttached(root, tool));
        Assert.NotNull(DockTreeNavigator.FindToolDock(root, tool));
        Assert.Equal(8, DockTreeNavigator.EnumerateWorkspace(root).Count());
        Assert.Equal(3, DockTreeNavigator.EnumerateWindows(root).Count());
    }
}
