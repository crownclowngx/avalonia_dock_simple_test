using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
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
    [InlineData("menu", false)]
    [InlineData("menu", true)]
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
            var accepted = await Invoke(entry, context, native, tool);
            if (entry == "center") Assert.Equal(!cancel, accepted);
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

    [AvaloniaTheory]
    [InlineData("button")]
    [InlineData("close")]
    [InlineData("hide")]
    [InlineData("center")]
    public async Task P2同组工具逐项关闭仅最后一项回收窗口(string entry)
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        try
        {
            session.ShowTool(HostExtensionIds.FileSystemTree);
            session.ShowTool(HostExtensionIds.PluginMenu);
            var first = (ManagedToolDockable)session.CreatedTools[HostExtensionIds.FileSystemTree.Value];
            var second = (ManagedToolDockable)session.CreatedTools[HostExtensionIds.PluginMenu.Value];
            factory.MoveDockable((IDock)second.Owner!, (IDock)first.Owner!, second, null);
            factory.FloatAllDockables(first);
            await Flush();
            var model = Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            var host = Assert.IsType<HostFloatingWindow>(model.Host);
            var group = (IDock)first.Owner!;
            var originalOrder = group.VisibleDockables!.Select(item => item.Id).ToArray();
            group.ActiveDockable = first;
            await Flush();
            await Invoke(entry, context, host, first);
            await Flush();
            Assert.True(host.IsVisible);
            Assert.Same(model, Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!)));
            Assert.Same(second, Assert.Single(group.VisibleDockables!));
            Assert.Same(second, group.ActiveDockable);
            Assert.False(first.IsViewReleased);
            await Invoke(entry, context, host, second);
            await Flush();
            Assert.False(host.IsVisible);
            Assert.Empty(factory.HostWindows);
            Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            Assert.False(second.IsViewReleased);
            var saved = Assert.Single(session.LayoutState.Capture(session).FloatingWindows);
            Assert.Equal(originalOrder, saved.Root.ToolIds);
        }
        finally { main.Close(); await Flush(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task P2主窗或混合浮窗隐藏Tool保留Document原实例(bool floating)
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        try
        {
            session.ShowTool(HostExtensionIds.FileSystemTree);
            var tool = session.CreatedTools[HostExtensionIds.FileSystemTree.Value];
            var document = session.GetDocuments().Single();
            var view = document.PreparedView;
            HostFloatingWindow? host = null;
            if (floating)
            {
                factory.FloatDockable(document);
                var window = Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
                host = Assert.IsType<HostFloatingWindow>(window.Host);
                // 使用框架分割协议建立工具与文档并存的真实浮窗，而非伪造内容计数。
                var documentGroup = (IDock)document.Owner!;
                var toolGroup = new ToolDock { VisibleDockables = factory.CreateList<IDockable>() };
                factory.MoveDockable((IDock)tool.Owner!, toolGroup, tool, null);
                factory.SplitToDock(documentGroup, toolGroup, DockOperation.Bottom);
            }
            await Flush();
            factory.CloseDockable(tool);
            await Flush();
            Assert.True(main.IsVisible);
            Assert.Same(document, session.GetDocuments().Single());
            Assert.Same(view, document.PreparedView);
            Assert.False(document.ClosingToken.IsCancellationRequested);
            Assert.False(DockTreeNavigator.IsDockableAttached(session.RootDock!, tool));
            if (host is not null)
            {
                Assert.True(host.IsVisible);
                Assert.Single(factory.HostWindows);
            }
            else Assert.Empty(factory.HostWindows);
        }
        finally { main.Close(); await Flush(); }
    }

    [AvaloniaTheory]
    [InlineData("capability")]
    [InlineData("last")]
    [InlineData("event")]
    public async Task P2关闭命令遵守工具能力和框架取消(string restriction)
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        session.ShowTool(HostExtensionIds.FileSystemTree);
        var tool = session.CreatedTools[HostExtensionIds.FileSystemTree.Value];
        factory.FloatDockable(tool);
        await Flush();
        var window = Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
        var host = Assert.IsType<HostFloatingWindow>(window.Host);
        var group = (IDock)tool.Owner!;
        var reject = true;
        var closing = 0;
        factory.DockableClosing += (_, args) =>
        {
            if (!ReferenceEquals(args.Dockable, tool)) return;
            closing++;
            if (restriction == "event" && reject) args.Cancel = true;
        };
        if (restriction == "capability") tool.CanClose = false;
        if (restriction == "last") group.CanCloseLastDockable = false;
        try
        {
            factory.CloseDockable(tool);
            await Flush();
            Assert.True(host.IsVisible);
            Assert.Same(tool, Assert.Single(group.VisibleDockables!));
            Assert.Same(window, Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!)));
            Assert.Equal(restriction == "event" ? 1 : 0, closing);
            reject = false;
            tool.CanClose = true;
            group.CanCloseLastDockable = true;
            var before = closing;
            factory.CloseDockable(tool);
            await Flush();
            Assert.False(host.IsVisible);
            Assert.Empty(factory.HostWindows);
            Assert.Equal(before + 1, closing);
        }
        finally { reject = false; tool.CanClose = true; group.CanCloseLastDockable = true; main.Close(); await Flush(); }
    }

    [AvaloniaFact]
    public async Task P2按钮关闭写入V3后重启不产生空窗且按原窗口恢复工具()
    {
        DockLayoutSnapshotV3 saved;
        DockLayoutWindow before;
        using (var context = new UiTestContext())
        {
            var session = context.Workspace;
            var main = new MainWindow(session.DockFactory.WindowContext) { DataContext = context.ViewModel };
            main.Show();
            session.ShowTool(HostExtensionIds.FileSystemTree);
            var tool = (ManagedToolDockable)session.CreatedTools[HostExtensionIds.FileSystemTree.Value];
            session.DockFactory.FloatDockable(tool);
            await Flush();
            var host = (HostFloatingWindow)Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!)).Host!;
            session.DockFactory.WindowContext.ApplyBounds(host, new(170, 120, 640, 470, false, null));
            await Flush();
            before = Assert.Single(session.LayoutState.Capture(session).FloatingWindows);
            await Invoke("button", context, host, tool);
            await Flush();
            var lifecycle = context.Provider.GetRequiredService<DockLayoutLifecycle>();
            Assert.True(lifecycle.Save(session));
            await lifecycle.FlushAsync();
            using var reader = new DockLayoutV3Store(context.TempDirectory);
            saved = reader.Load()!;
            Assert.Equal("hidden", saved.Tools.Single(item => item.Id == tool.Id).State);
            Assert.Equal(before.Bounds, Assert.Single(saved.FloatingWindows).Bounds);
            main.Close();
            await Flush();
        }
        using var restart = new UiTestContext(initialLayoutV3: saved);
        var reopened = new MainWindow(restart.Workspace.DockFactory.WindowContext) { DataContext = restart.ViewModel };
        reopened.Show();
        try
        {
            await Flush();
            Assert.Empty(DockTreeNavigator.EnumerateWindows(restart.Workspace.RootDock!));
            Assert.Single(restart.Workspace.GetDocuments());
            Assert.True(restart.Workspace.ShowTool(HostExtensionIds.FileSystemTree));
            await Flush();
            var restored = Assert.Single(restart.Workspace.LayoutState.Capture(restart.Workspace).FloatingWindows);
            Assert.Equal(before.Id, restored.Id);
            Assert.Equal(before.Root.Id, restored.Root.Id);
            Assert.Equal(before.Root.ToolIds, restored.Root.ToolIds);
            Assert.Equal(before.Bounds, restored.Bounds);
            Assert.Single(restart.Workspace.DockFactory.HostWindows);
        }
        finally { reopened.Close(); await Flush(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task P2多组整窗关闭保留比例和位置且取消不隐藏任何成员(bool cancel)
    {
        var firstId = HostExtensionIds.FileSystemTree.Value;
        var secondId = HostExtensionIds.PluginMenu.Value;
        var initial = new DockLayoutSnapshotV3(3, new("main", DockWindowBounds.Default, DockLayoutNode.Documents()),
            [new("two-groups", new(130, 100, 650, 480, false, null), DockLayoutNode.Split("vertical", "vertical",
                [DockLayoutNode.Group("upper", [firstId], 0.35), DockLayoutNode.Group("lower", [secondId], 0.65)]))],
            [new(firstId, "visible", DockLayoutIds.LeftTools, 0), new(secondId, "visible", DockLayoutIds.RightTools, 0)]);
        using var context = new UiTestContext(initialLayoutV3: initial);
        var session = context.Workspace;
        var main = new MainWindow(session.DockFactory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        await Flush();
        var window = Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
        var host = Assert.IsType<HostFloatingWindow>(window.Host);
        var before = Assert.Single(session.LayoutState.Capture(session).FloatingWindows);
        EventHandler<WindowClosingEventArgs> cancellation = (_, args) => args.Cancel = cancel;
        host.Closing += cancellation;
        try
        {
            host.Close();
            await Flush();
            Assert.Equal(cancel, host.IsVisible);
            var snapshot = session.LayoutState.Capture(session);
            var saved = Assert.Single(snapshot.FloatingWindows);
            Assert.Equal(before.Bounds, saved.Bounds);
            Assert.Equal("vertical", saved.Root.Orientation);
            Assert.Equal(new[] { "upper", "lower" }, saved.Root.Children.Select(node => node.Id));
            Assert.Equal(new[] { 0.35, 0.65 }, saved.Root.Children.Select(node => node.Proportion));
            Assert.Equal(new[] { firstId, secondId }, saved.Root.Children.SelectMany(node => node.ToolIds));
            Assert.All(snapshot.Tools, tool => Assert.Equal(cancel ? "visible" : "hidden", tool.State));
            Assert.All(session.CreatedTools.Values.Cast<ManagedToolDockable>(), tool => Assert.False(tool.IsViewReleased));
            Assert.Equal(cancel ? 1 : 0, session.DockFactory.HostWindows.Count);
        }
        finally { host.Closing -= cancellation; main.Close(); await Flush(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task P2异步重试被窗口或Factory取消后仍可再次正常关闭(bool factoryCancellation)
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var main = new MainWindow(session.DockFactory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        session.ShowTool(HostExtensionIds.FileSystemTree);
        var tool = (ManagedToolDockable)session.CreatedTools[HostExtensionIds.FileSystemTree.Value];
        session.DockFactory.FloatDockable(tool);
        await Flush();
        var host = (HostFloatingWindow)Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!)).Host!;
        var reject = true;
        var attempts = 0;
        host.Closing += (_, args) => { attempts++; if (!factoryCancellation && reject && attempts == 2) args.Cancel = true; };
        session.DockFactory.WindowClosing += (_, args) => { if (factoryCancellation && reject) args.Cancel = true; };
        try
        {
            await Invoke("button", context, host, tool);
            await Flush();
            Assert.Equal(2, attempts);
            Assert.True(host.IsVisible);
            Assert.True(DockTreeNavigator.IsDockableAttached(session.RootDock!, tool));
            reject = false;
            await Invoke("button", context, host, tool);
            await Flush();
            Assert.False(host.IsVisible);
            Assert.Empty(session.DockFactory.HostWindows);
            Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
        }
        finally { reject = false; main.Close(); await Flush(); }
    }

    private static async Task<bool?> Invoke(string entry, UiTestContext context, HostFloatingWindow window, ManagedToolDockable tool)
    {
        switch (entry)
        {
            case "button":
                var chrome = await WaitForChrome(window);
                var button = Assert.Single(chrome.GetVisualDescendants().OfType<Button>(), item => item.Name == "PART_CloseButton");
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                break;
            case "close": context.Workspace.DockFactory.CloseDockable(tool); break;
            case "hide": context.Workspace.DockFactory.HideDockable(tool); break;
            case "center": return context.Workspace.SetToolVisibility(tool.Id, false).Succeeded;
            case "menu":
                var menuChrome = await WaitForChrome(window);
                var menu = Assert.IsType<MenuFlyout>(menuChrome.ToolFlyout);
                var menuButton = Assert.Single(menuChrome.GetVisualDescendants().OfType<Button>(), item => item.Name == "PART_MenuButton");
                menu.ShowAt(menuButton);
                var close = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.Header?.ToString() == "隐藏工具");
                Assert.Same(tool, close.CommandParameter);
                Assert.NotNull(close.Command);
                Assert.True(close.Command.CanExecute(close.CommandParameter));
                close.Command.Execute(close.CommandParameter);
                menu.Hide();
                break;
            default: throw new ArgumentOutOfRangeException(nameof(entry));
        }
        return null;
    }

    private static async Task Flush()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        // 按钮坐标依赖模板完成测量；此处等待 Headless 软件渲染的下一帧，不模拟真实桌面验收。
        await Task.Delay(30);
    }

    private static async Task<ToolChromeControl> WaitForChrome(HostFloatingWindow window)
    {
        // ToolChrome 使用延后模板，测试必须等待可点击控件完成布局，不能假定一次 Dispatcher
        // Background 或固定一帧就足够。这里只等待就绪，关闭动作本身不重试，避免掩盖生产错误。
        for (var attempt = 0; attempt < 100; attempt++)
        {
            window.UpdateLayout();
            var chrome = window.GetVisualDescendants().OfType<ToolChromeControl>().SingleOrDefault();
            if (chrome?.GetVisualDescendants().OfType<Button>().Any(button =>
                    button.Name == "PART_CloseButton" && button.Bounds.Width > 0 && button.Bounds.Height > 0) == true)
                return chrome;
            await Task.Delay(10);
        }
        throw new TimeoutException("ToolChrome 关闭按钮未在 Headless 布局中就绪。");
    }
}
