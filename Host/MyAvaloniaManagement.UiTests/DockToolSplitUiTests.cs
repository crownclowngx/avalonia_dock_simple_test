using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 从 V3 文件恢复后，通过 DockManager 的生产入口操作真实 Host 窗口。
/// Headless 验证窗口协议、实例和文件；真实鼠标命中及屏幕坐标另列桌面验收，不能互相替代。
/// </summary>
public sealed class DockToolSplitUiTests
{
    private static readonly PluginId Plugin = new("myavalonia.plugin.p1-test");
    private static readonly ToolTypeId Third = new("myavalonia.plugin.p1-test.tool.target");
    private static string First => HostExtensionIds.FileSystemTree.Value;
    private static string Second => HostExtensionIds.PluginMenu.Value;

    [AvaloniaTheory]
    [InlineData("same", DockOperation.Top)]
    [InlineData("same", DockOperation.Bottom)]
    [InlineData("cross", DockOperation.Top)]
    [InlineData("cross", DockOperation.Bottom)]
    [InlineData("group", DockOperation.Top)]
    [InlineData("group", DockOperation.Bottom)]
    [InlineData("float-back", DockOperation.Top)]
    [InlineData("float-back", DockOperation.Bottom)]
    public async Task P1恢复后单项和整组局部分割保持实例且最终布局可重启(string mode, DockOperation operation)
    {
        DockLayoutSnapshotV3 saved;
        var wholeGroup = mode is "group" or "float-back";
        var targetId = wholeGroup ? Third.Value : Second;
        using (var context = Context(Initial(mode)))
        {
            var session = context.Workspace;
            var factory = session.DockFactory;
            var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
            main.Show();
            try
            {
                await Flush();
                var tools = session.CreatedTools.Values.Cast<ManagedToolDockable>().ToArray();
                var models = tools.Select(tool => tool.Model).ToArray();
                var views = tools.Select(tool => tool.PreparedView).ToArray();
                var document = Assert.Single(session.GetDocuments());
                var documentView = document.PreparedView;
                var moved = session.CreatedTools[First];
                var target = session.CreatedTools[targetId];
                HostFloatingWindow? oldWindow = null;
                if (mode == "float-back")
                {
                    factory.FloatAllDockables(moved);
                    await Flush();
                    oldWindow = Assert.IsType<HostFloatingWindow>(Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!)).Host);
                    Assert.True(oldWindow.IsVisible);
                }
                var lifecycle = context.Provider.GetRequiredService<DockLayoutLifecycle>();
                Assert.True(lifecycle.Save(session));
                await lifecycle.FlushAsync();
                var before = File.ReadAllBytes(context.LayoutPath);
                var transientEvents = 0;
                // DockService 先移动内容再挂接新组，两个 Factory 调用可能各有通知。
                // 在同步通知中检查旧文件，证明尚未挂接的新组不会提前覆盖最后有效布局。
                void Observe(object? sender, EventArgs args)
                {
                    if (!DockTreeNavigator.IsDockableAttached(session.RootDock!, moved))
                    {
                        transientEvents++;
                        Assert.Equal(before, File.ReadAllBytes(context.LayoutPath));
                    }
                }
                session.LayoutChanged += Observe;
                try
                {
                    var source = wholeGroup ? moved.Owner! : moved;
                    Assert.True(new DockManager(new DockService()).ValidateDockable(source, target.Owner!, DragAction.Move, operation, bExecute: true));
                    Assert.Equal(before, File.ReadAllBytes(context.LayoutPath));
                }
                finally { session.LayoutChanged -= Observe; }
                if (!wholeGroup) Assert.True(transientEvents > 0);
                await Flush();
                AssertLocalOrder(moved, target, operation);
                Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
                if (oldWindow is not null) Assert.False(oldWindow.IsVisible);
                if (wholeGroup) Assert.Equal([First, Second], ((IDock)moved.Owner!).VisibleDockables!.Select(item => item.Id));
                Assert.Same(document, Assert.Single(session.GetDocuments()));
                Assert.Same(documentView, document.PreparedView);
                Assert.False(document.ClosingToken.IsCancellationRequested);
                for (var index = 0; index < tools.Length; index++)
                {
                    Assert.Same(models[index], tools[index].Model);
                    Assert.Same(views[index], tools[index].PreparedView);
                    Assert.Equal(1, DockTreeNavigator.EnumerateWorkspace(session.RootDock!).Count(item => ReferenceEquals(item, tools[index])));
                }
                Assert.False(((ProbeTool)((ManagedToolDockable)session.CreatedTools[Third.Value]).Model!).Disposed);
                await lifecycle.FlushAsync();
                saved = context.Provider.GetRequiredService<DockLayoutV3Store>().Load()!;
                DockLayoutV3Validator.Validate(saved);
                Assert.All(saved.Tools, tool => Assert.Equal("visible", tool.State));
                Assert.Empty(saved.FloatingWindows);
                var groupId = ((IDock)moved.Owner!).Id;
                factory.HideDockable(moved);
                Assert.True(session.ShowTool(HostExtensionIds.FileSystemTree));
                await Flush();
                Assert.Equal(groupId, moved.Owner!.Id);
                AssertLocalOrder(moved, target, operation);
            }
            finally { main.Close(); await Flush(); }
        }
        using var restart = Context(saved);
        var reopened = new MainWindow(restart.Workspace.DockFactory.WindowContext) { DataContext = restart.ViewModel };
        reopened.Show();
        try
        {
            await Flush();
            AssertLocalOrder(restart.Workspace.CreatedTools[First], restart.Workspace.CreatedTools[targetId], operation);
            Assert.Single(restart.Workspace.GetDocuments());
            var again = restart.Workspace.LayoutState.Capture(restart.Workspace);
            DockLayoutV3Validator.Validate(again);
            Assert.Equal(saved.MainWindow.Root, again.MainWindow.Root, new NodeComparer());
        }
        finally { reopened.Close(); await Flush(); }
    }

    [AvaloniaTheory]
    [InlineData(DockOperation.Top)]
    [InlineData(DockOperation.Bottom)]
    public async Task P1浮窗内部连续上下分割不访问主窗口稳定停靠点(DockOperation operation)
    {
        using var context = Context(Initial("group"));
        var session = context.Workspace;
        var main = new MainWindow(session.DockFactory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        try
        {
            var moved = session.CreatedTools[First];
            var other = session.CreatedTools[Second];
            session.DockFactory.FloatAllDockables(moved);
            await Flush();
            var window = Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            var mainBefore = session.LayoutState.Capture(session).MainWindow.Root;
            foreach (var direction in new[] { operation, operation == DockOperation.Top ? DockOperation.Bottom : DockOperation.Top })
            {
                Assert.True(new DockManager(new DockService()).ValidateDockable(moved, other.Owner!, DragAction.Move, direction, bExecute: true));
                await Flush();
                AssertLocalOrder(moved, other, direction);
                Assert.Same(window, DockTreeNavigator.FindWindow(session.RootDock!, moved));
                Assert.Same(window, DockTreeNavigator.FindWindow(session.RootDock!, other));
                Assert.False(moved.CanPin);
                Assert.Same(window, Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!)));
                var captured = session.LayoutState.Capture(session);
                DockLayoutV3Validator.Validate(captured);
                Assert.Equal(mainBefore, captured.MainWindow.Root, new NodeComparer());
            }
            var lifecycle = context.Provider.GetRequiredService<DockLayoutLifecycle>();
            Assert.True(lifecycle.Save(session));
            await lifecycle.FlushAsync();
            var saved = context.Provider.GetRequiredService<DockLayoutV3Store>().Load()!;
            main.Close();
            await Flush();
            using var restart = Context(saved);
            var reopened = new MainWindow(restart.Workspace.DockFactory.WindowContext) { DataContext = restart.ViewModel };
            reopened.Show();
            try
            {
                await Flush();
                AssertLocalOrder(restart.Workspace.CreatedTools[First], restart.Workspace.CreatedTools[Second],
                    operation == DockOperation.Top ? DockOperation.Bottom : DockOperation.Top);
                Assert.Single(DockTreeNavigator.EnumerateWindows(restart.Workspace.RootDock!));
                var restored = restart.Workspace.LayoutState.Capture(restart.Workspace);
                Assert.Equal(saved.FloatingWindows[0].Root, restored.FloatingWindows[0].Root, new NodeComparer());
            }
            finally { reopened.Close(); await Flush(); }
        }
        finally { main.Close(); await Flush(); }
    }

    [AvaloniaTheory]
    [InlineData(DockOperation.Top, false)]
    [InlineData(DockOperation.Bottom, false)]
    [InlineData(DockOperation.Top, true)]
    [InlineData(DockOperation.Bottom, true)]
    public async Task P1从V3恢复后文档区及其拆分子组仍全宽停靠(DockOperation operation, bool splitDocument)
    {
        using var context = Context(Initial("group"));
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        try
        {
            await Flush();
            var document = Assert.Single(session.GetDocuments());
            var view = document.PreparedView;
            var target = (IDock)document.Owner!;
            if (splitDocument)
            {
                var subgroup = new DocumentDock { Id = "p1-secondary-documents", IsCollapsable = false };
                factory.SplitToDock(target, subgroup, DockOperation.Right);
                target = subgroup;
            }
            var moved = session.CreatedTools[First];
            Assert.True(new DockManager(new DockService()).ValidateDockable(moved, target, DragAction.Move, operation, bExecute: true));
            await Flush();
            var stableId = operation == DockOperation.Top ? DockLayoutIds.TopTools : DockLayoutIds.BottomTools;
            Assert.Equal(stableId, moved.Owner!.Id);
            var pane = Assert.IsAssignableFrom<IProportionalDock>(moved.Owner.Owner);
            var rows = Assert.IsAssignableFrom<IProportionalDock>(pane.Owner);
            Assert.Equal(DockLayoutIds.WorkspaceRows, rows.Id);
            var columns = Assert.Single(rows.VisibleDockables!, item => item.Id == DockLayoutIds.WorkspaceColumns);
            Assert.Equal(operation == DockOperation.Top ? -2 : 2,
                rows.VisibleDockables!.IndexOf(pane) - rows.VisibleDockables.IndexOf(columns));
            Assert.Same(document, Assert.Single(session.GetDocuments()));
            Assert.Same(view, document.PreparedView);
            Assert.False(document.ClosingToken.IsCancellationRequested);
            var lifecycle = context.Provider.GetRequiredService<DockLayoutLifecycle>();
            Assert.True(lifecycle.Save(session));
            await lifecycle.FlushAsync();
            var saved = context.Provider.GetRequiredService<DockLayoutV3Store>().Load()!;
            DockLayoutV3Validator.Validate(saved);
            main.Close();
            await Flush();
            using var restart = Context(saved);
            var reopened = new MainWindow(restart.Workspace.DockFactory.WindowContext) { DataContext = restart.ViewModel };
            reopened.Show();
            try
            {
                await Flush();
                // V3 分组身份与固定主骨架身份不同，恢复验证拓扑和持久化身份，不要求沿用运行时别名。
                var restoredGroup = restart.Workspace.CreatedTools[First].Owner!;
                var restoredSplit = Assert.IsAssignableFrom<IProportionalDock>(restoredGroup.Owner);
                Assert.Equal(Orientation.Vertical, restoredSplit.Orientation);
                Assert.Same(restoredGroup, operation == DockOperation.Top
                    ? restoredSplit.VisibleDockables![0] : restoredSplit.VisibleDockables![^1]);
                Assert.Equal(saved.MainWindow.Root, restart.Workspace.LayoutState.Capture(restart.Workspace).MainWindow.Root, new NodeComparer());
            }
            finally { reopened.Close(); await Flush(); }
        }
        finally { main.Close(); await Flush(); }
    }

    [AvaloniaFact]
    public async Task P1内容全屏期间分割入口不迁移工具或改变布局()
    {
        using var context = Context(Initial("group"));
        var session = context.Workspace;
        var main = new MainWindow(session.DockFactory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        try
        {
            await Flush();
            var moved = session.CreatedTools[First];
            var target = session.CreatedTools[Third.Value];
            var before = session.LayoutState.Capture(session).MainWindow.Root;
            using (var fullscreen = ((IWindowContentFullscreenHost)main).TryPresent(new Border()))
            {
                Assert.NotNull(fullscreen);
                new DockManager(new DockService()).ValidateDockable(moved, target.Owner!, DragAction.Move, DockOperation.Bottom, bExecute: true);
                Assert.Equal(before, session.LayoutState.Capture(session).MainWindow.Root, new NodeComparer());
            }
        }
        finally { main.Close(); await Flush(); }
    }

    private static void AssertLocalOrder(IDockable moved, IDockable target, DockOperation operation)
    {
        var first = Assert.IsAssignableFrom<IToolDock>(moved.Owner);
        var second = Assert.IsAssignableFrom<IToolDock>(target.Owner);
        Assert.NotSame(first, second);
        var parent = Assert.IsAssignableFrom<IProportionalDock>(first.Owner);
        Assert.Same(parent, second.Owner);
        Assert.Equal(Orientation.Vertical, parent.Orientation);
        var children = parent.VisibleDockables!;
        var index = children.IndexOf(second);
        Assert.Same(first, children[index + (operation == DockOperation.Top ? -2 : 2)]);
        Assert.IsAssignableFrom<IProportionalDockSplitter>(children[index + (operation == DockOperation.Top ? -1 : 1)]);
        foreach (var child in children) Assert.Same(parent, child.Owner);
        Assert.NotEqual(DockLayoutIds.TopTools, first.Id);
        Assert.NotEqual(DockLayoutIds.BottomTools, first.Id);
    }

    private static UiTestContext Context(DockLayoutSnapshotV3 snapshot) => new(initialLayoutV3: snapshot,
        modules: PluginModuleCatalog.CreateForTests([(Plugin, (IPluginModule)new Module())]));
    private static DockLayoutSnapshotV3 Initial(string mode)
    {
        var groups = mode == "cross"
            ? new[] { DockLayoutNode.Group("first", [First]), DockLayoutNode.Group("second", [Second]), DockLayoutNode.Group("third", [Third.Value]) }
            : new[] { DockLayoutNode.Group("pair", [First, Second]), DockLayoutNode.Group("third", [Third.Value]) };
        var nodes = new[] { DockLayoutNode.Documents() }.Concat(groups).Select(node => node with { Proportion = 1d / (groups.Length + 1) });
        return new(3, new("main", DockWindowBounds.Default, DockLayoutNode.Split("restored", "horizontal", nodes)), [],
            new[] { First, Second, Third.Value }.Select(id => new DockLayoutTool(id, "visible", DockLayoutIds.RightTools, 0)).ToArray());
    }
    private static Task Flush() => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();
    private sealed class Module : IPluginModule
    {
        public void Configure(IPluginRegistration registration) => registration.AddTool<ProbeTool, ProbeView>(
            new ToolDescriptor(Third, "分割目标", "验证整组移动的独立目标。", ToolDockSide.Right, ToolCloseBehavior.Hide));
    }
    public sealed class ProbeTool : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    public sealed class ProbeView : UserControl;
    private sealed class NodeComparer : IEqualityComparer<DockLayoutNode>
    {
        public bool Equals(DockLayoutNode? x, DockLayoutNode? y) => x is not null && y is not null &&
            x.Kind == y.Kind && x.Id == y.Id && Math.Abs(x.Proportion - y.Proportion) < 0.000001 &&
            x.Orientation == y.Orientation && x.ActiveToolId == y.ActiveToolId && x.ToolIds.SequenceEqual(y.ToolIds) &&
            x.Children.SequenceEqual(y.Children, this);
        public int GetHashCode(DockLayoutNode obj) => obj.Id.GetHashCode(StringComparison.Ordinal);
    }
}
