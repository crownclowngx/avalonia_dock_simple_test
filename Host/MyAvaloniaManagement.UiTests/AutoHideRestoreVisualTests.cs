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
        DockLayoutSnapshotV2 snapshot;
        using (var original = new UiTestContext())
        {
            snapshot = DockLayoutSnapshotMapper.Capture(original.Workspace.RootDock!, original.Workspace);
            snapshot = snapshot with
            {
                Tools = snapshot.Tools
                    .OrderBy(tool => tool.Id == HostExtensionIds.PluginStatus.Value ? 0 : 1)
                    .Select((tool, index) => tool with
                    {
                        DockId = ToolDockPlacement.GetDockId(alignment),
                        Order = index,
                        IsVisible = true,
                        IsPinned = !keepExpandedTool || tool.Id == HostExtensionIds.PluginStatus.Value
                    }).ToList()
            };
        }

        using var context = new UiTestContext(initialLayout: snapshot);
        var window = new MainWindow { Width = 1400, Height = 900, DataContext = context.ViewModel };
        var dockControl = window.GetLogicalDescendants().OfType<DockControl>().Single();
        ControlRecyclingDataTemplate.SetControlRecycling(
            dockControl, context.Provider.GetRequiredService<DocumentControlRecycling>());
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tool = Assert.IsType<ManagedToolDockable>(
                context.Workspace.CreatedTools[HostExtensionIds.PluginStatus.Value]);
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
                Assert.Equal(ToolDockPlacement.GetDockId(alignment), owner.Id);
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
