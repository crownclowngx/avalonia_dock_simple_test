using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// V11-P2 覆盖真实 ToolChrome 按钮的 Click 与 Command 联动，不能用 Window.Close 代替。
/// 同时保护命令/隐藏入口：最后一个工具先获准关闭窗口，再提交隐藏；取消不得清空内容。
/// </summary>
public sealed class DockToolWindowCloseUiTests
{
    [AvaloniaTheory]
    [InlineData("button", false)]
    [InlineData("button", true)]
    [InlineData("close", false)]
    [InlineData("close", true)]
    [InlineData("hide", false)]
    [InlineData("hide", true)]
    [InlineData("center", false)]
    [InlineData("center", true)]
    public async Task P2最后工具关闭获准后回收窗口且取消时保留内容(string entry, bool cancel)
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        session.ShowTool(HostExtensionIds.FileSystemTree);
        var tool = (ManagedToolDockable)session.CreatedTools[HostExtensionIds.FileSystemTree.Value];
        var originalModel = tool.Model;
        var originalView = tool.PreparedView;
        factory.FloatDockable(tool);
        await Flush();
        var window = Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
        var native = Assert.IsType<HostFloatingWindow>(window.Host);
        var group = (IDock)tool.Owner!;
        var saved = Assert.Single(session.LayoutState.Capture(session).FloatingWindows);
        var closed = 0;
        native.Closed += (_, _) => closed++;
        EventHandler<WindowClosingEventArgs> cancellation = (_, args) => args.Cancel = cancel;
        native.Closing += cancellation;
        try
        {
            Invoke(entry, context, native, tool);
            await Flush();
            var snapshot = session.LayoutState.Capture(session);
            DockLayoutV3Validator.Validate(snapshot);
            Assert.Same(originalModel, tool.Model);
            Assert.Same(originalView, tool.PreparedView);
            Assert.False(tool.IsViewReleased);
            Assert.True(main.IsVisible);
            if (cancel)
            {
                Assert.True(native.IsVisible);
                Assert.Equal(0, closed);
                Assert.Same(group, tool.Owner);
                Assert.Same(tool, group.ActiveDockable);
                Assert.Contains(tool, group.VisibleDockables!);
                Assert.Same(window, Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!)));
                Assert.Equal("visible", snapshot.Tools.Single(item => item.Id == tool.Id).State);
            }
            else
            {
                Assert.False(native.IsVisible);
                Assert.Equal(1, closed);
                Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
                Assert.Empty(factory.HostWindows);
                Assert.Null(window.Factory);
                Assert.Null(window.Layout);
                Assert.Equal("hidden", snapshot.Tools.Single(item => item.Id == tool.Id).State);
                var remembered = Assert.Single(snapshot.FloatingWindows);
                Assert.Equal(saved.Id, remembered.Id);
                Assert.Equal(saved.Root.Id, remembered.Root.Id);
                Assert.Equal(saved.Bounds, remembered.Bounds);
                Assert.True(session.ShowTool(HostExtensionIds.FileSystemTree));
                await Flush();
                var reopened = Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
                Assert.NotSame(native, reopened.Host);
                Assert.Same(originalModel, tool.Model);
                Assert.Same(originalView, tool.PreparedView);
                Assert.Equal(saved.Id, Assert.Single(session.LayoutState.Capture(session).FloatingWindows).Id);
            }
        }
        finally { native.Closing -= cancellation; main.Close(); await Flush(); }
    }

    private static void Invoke(string entry, UiTestContext context, HostFloatingWindow window, ManagedToolDockable tool)
    {
        switch (entry)
        {
            case "button":
                var chrome = Assert.Single(window.GetVisualDescendants().OfType<ToolChromeControl>());
                var button = Assert.Single(chrome.GetVisualDescendants().OfType<Button>(), item => item.Name == "PART_CloseButton");
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                break;
            case "close": context.Workspace.DockFactory.CloseDockable(tool); break;
            case "hide": context.Workspace.DockFactory.HideDockable(tool); break;
            case "center": context.Workspace.SetToolVisibility(tool.Id, false); break;
            default: throw new ArgumentOutOfRangeException(nameof(entry));
        }
    }

    private static async Task Flush()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        // 按钮坐标依赖模板完成测量；此处等待 Headless 软件渲染的下一帧，不模拟真实桌面验收。
        await Task.Delay(30);
    }
}
