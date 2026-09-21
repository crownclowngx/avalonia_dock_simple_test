using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Recycling;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

public sealed class AutoHideRestoreVisualTests
{
    [AvaloniaTheory]
    [InlineData(Alignment.Left, false)]
    [InlineData(Alignment.Left, true)]
    [InlineData(Alignment.Right, false)]
    [InlineData(Alignment.Right, true)]
    [InlineData(Alignment.Top, false)]
    [InlineData(Alignment.Top, true)]
    [InlineData(Alignment.Bottom, false)]
    [InlineData(Alignment.Bottom, true)]
    public void 重启后边栏工具展开及固定均有可见内容(Alignment alignment, bool keepExpandedTool)
    {
        DockLayoutSnapshotV3 snapshot;
        using (var original = new UiTestContext())
        {
            // 使用实际会话捕获工具身份，测试输入只描述 V3 树和状态，不模拟旧格式转换。
            var captured = original.Workspace.LayoutState.Capture(original.Workspace);
            var tools = captured.Tools.OrderBy(tool => tool.Id == HostExtensionIds.PluginMenu.Value ? 0 : 1)
                .Select((tool, index) => tool with
                {
                    ReturnDockId = ToolDockPlacement.GetDockId(alignment), ReturnOrder = index,
                    State = !keepExpandedTool || tool.Id == HostExtensionIds.PluginMenu.Value ? "autoHidden" : "visible"
                }).ToArray();
            var group = DockLayoutNode.Group("edge-tools", tools.Select(tool => tool.Id), 0.25);
            var documents = DockLayoutNode.Documents() with { Proportion = 0.75 };
            var leading = alignment is Alignment.Left or Alignment.Top;
            snapshot = captured with
            {
                Tools = tools,
                MainWindow = captured.MainWindow with
                {
                    Root = DockLayoutNode.Split("edge-split", alignment is Alignment.Top or Alignment.Bottom ? "vertical" : "horizontal",
                        leading ? [group, documents] : [documents, group])
                }
            };
        }

        using var context = new UiTestContext(initialLayoutV3: snapshot);
        var window = new MainWindow { Width = 1400, Height = 900, DataContext = context.ViewModel };
        var dockControl = window.GetLogicalDescendants().OfType<DockControl>().Single();
        ControlRecyclingDataTemplate.SetControlRecycling(
            dockControl, context.Provider.GetRequiredService<DocumentControlRecycling>());
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tool = Assert.IsType<ManagedToolDockable>(
                context.Workspace.CreatedTools[HostExtensionIds.PluginMenu.Value]);
            var factory = context.Workspace.DockFactory;
            var root = factory.FindRoot(tool, _ => true)!;
            var prepared = Assert.IsAssignableFrom<Control>(tool.PreparedView);
            for (var cycle = 0; cycle < 2; cycle++)
            {
                Assert.True(DockTreeNavigator.IsToolPinned(context.Workspace.RootDock!, tool));
                factory.PreviewPinnedDockable(tool);
                Dispatcher.UIThread.RunJobs();
                AssertContentVisible(window, prepared, "preview");
                if (cycle == 0)
                {
                    Assert.True(alignment is Alignment.Left or Alignment.Right
                        ? prepared.Bounds.Width >= 300
                        : prepared.Bounds.Height >= 180,
                        $"Restored preview is too small: {prepared.Bounds}");
                }

                var button = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    button.Name == "PART_PinButton" && ReferenceEquals(button.DataContext, root.PinnedDock));
                var point = button.TranslatePoint(
                    new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.False(DockTreeNavigator.IsToolPinned(context.Workspace.RootDock!, tool));
                Assert.True(DockTreeNavigator.IsDockableAttached(context.Workspace.RootDock!, tool));
                var owner = Assert.IsAssignableFrom<IToolDock>(tool.Owner);
                Assert.Equal(alignment, owner.Alignment);
                Assert.Null(DockTreeNavigator.FindWindow(context.Workspace.RootDock!, tool));
                Assert.Same(tool, owner.ActiveDockable);
                AssertContentVisible(window, prepared, "fixed");
                Assert.Same(prepared, tool.PreparedView);
                Assert.Same(tool.Model, prepared.DataContext);

                if (cycle == 0)
                {
                    factory.PinDockable(tool);
                    Dispatcher.UIThread.RunJobs();
                }
            }
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void AssertContentVisible(Window window, Control view, string stage)
    {
        Assert.True(view.GetVisualAncestors().Contains(window), $"{stage}: tool view is detached");
        Assert.True(view.IsEffectivelyVisible, $"{stage}: tool view is hidden");
        Assert.True(view.Bounds.Width >= 150, $"{stage}: width is {view.Bounds.Width}");
        Assert.True(view.Bounds.Height >= 60, $"{stage}: height is {view.Bounds.Height}");
    }
}
