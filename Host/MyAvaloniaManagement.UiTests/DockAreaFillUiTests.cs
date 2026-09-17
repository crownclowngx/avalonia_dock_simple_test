using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Contract;
using Dock.Avalonia.Controls;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Settings;
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
/// 使用 Host 的真实样式、模型和生命周期验证区域合并。目标命中测试与真实标签输入测试分别报告，
/// 不把 Headless 的屏幕坐标模拟当作原生桌面的遮挡、DPI 或窗口层级验收。
/// </summary>
public sealed class DockAreaFillUiTests
{
    private static readonly PluginId Plugin = new("myavalonia.plugin.area-fill-test");
    private static readonly DocumentTypeId DocumentType = new("myavalonia.plugin.area-fill-test.document.probe");
    private static string FirstTool => HostExtensionIds.FileSystemTree.Value;
    private static string SecondTool => HostExtensionIds.PluginMenu.Value;

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task 文档内容区合并保留原模型视图和作用域且关闭源空窗(bool wholeGroup, bool targetFloating)
    {
        using var context = Context();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = Show(context);
        try
        {
            var targetDocument = Assert.Single(session.GetDocuments());
            var first = await session.CreateAndPublishDocumentAsync(DocumentType, new NewDocumentActivation("第一个"));
            var second = await session.CreateAndPublishDocumentAsync(DocumentType, new NewDocumentActivation("第二个"));
            factory.FloatDockable(first);
            var sourceGroup = Assert.IsAssignableFrom<IDocumentDock>(first.Owner);
            if (wholeGroup)
                Assert.True(new DockManager(new DockService()).ValidateDockable(second, sourceGroup, DragAction.Move, DockOperation.Fill, true));
            if (targetFloating) factory.FloatDockable(targetDocument);
            await Flush();
            var sourceWindow = Assert.IsType<HostFloatingWindow>(DockTreeNavigator.FindWindow(session.RootDock!, first)!.Host);
            var targetGroup = Assert.IsAssignableFrom<IDocumentDock>(targetDocument.Owner);
            var targetContents = targetGroup.VisibleDockables!.ToArray();
            var destination = targetFloating ? Assert.IsType<HostFloatingWindow>(DockTreeNavigator.FindWindow(session.RootDock!, targetDocument)!.Host) : (Window)main;
            var documents = session.GetDocuments().ToArray();
            var models = documents.Select(document => document.Model).ToArray();
            var views = documents.Select(document => document.PreparedView).ToArray();
            var area = FindArea(destination, targetGroup);
            using (var preview = new Preview(area))
            {
                IDockable source = wholeGroup ? sourceGroup : first;
                var operation = preview.Resolve(source, targetGroup, factory);
                Assert.Equal(DockOperation.Fill, operation);
                Assert.Same(sourceGroup, first.Owner); // 悬停不能提前修改树或关闭窗口。
                Assert.True(sourceWindow.IsVisible);
                Assert.True(new DockManager(new DockService()).ValidateDockable(source, targetGroup, DragAction.Move, operation, true));
            }
            await Flush();
            Assert.False(sourceWindow.IsVisible);
            Assert.Same(targetGroup, first.Owner);
            if (wholeGroup) Assert.Same(targetGroup, second.Owner);
            Assert.Equal(targetContents.Concat(wholeGroup ? [first, second] : new IDockable[] { first }),
                targetGroup.VisibleDockables!);
            Assert.Contains(targetGroup.ActiveDockable, wholeGroup ? [first, second] : new IDockable[] { first });
            for (var index = 0; index < documents.Length; index++)
            {
                Assert.Same(models[index], documents[index].Model);
                Assert.Same(views[index], documents[index].PreparedView);
                Assert.False(documents[index].ClosingToken.IsCancellationRequested);
                Assert.Equal(1, DockTreeNavigator.EnumerateWorkspace(session.RootDock!).Count(item => ReferenceEquals(item, documents[index])));
            }
            Assert.False(((ProbeDocument)first.Model).Disposed);
            Assert.False(((ProbeDocument)second.Model).Disposed);
            var snapshot = session.LayoutState.Capture(session);
            DockLayoutV3Validator.Validate(snapshot);
            Assert.Empty(snapshot.FloatingWindows); // 文档浮窗不进入工具布局快照。
        }
        finally { main.Close(); await Flush(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 工具合并回主窗口或浮窗后保持实例和可恢复的V3布局(bool targetFloating)
    {
        DockLayoutSnapshotV3 saved;
        using (var context = new UiTestContext(initialLayoutV3: ToolLayout()))
        {
            var session = context.Workspace;
            var factory = session.DockFactory;
            var main = Show(context);
            try
            {
                var moved = (ManagedToolDockable)session.CreatedTools[FirstTool];
                var target = session.CreatedTools[SecondTool];
                var model = moved.Model;
                var view = moved.PreparedView;
                factory.FloatDockable(moved);
                if (targetFloating) factory.FloatDockable(target);
                await Flush();
                var sourceWindow = (HostFloatingWindow)DockTreeNavigator.FindWindow(session.RootDock!, moved)!.Host!;
                var destination = targetFloating ? (Window)DockTreeNavigator.FindWindow(session.RootDock!, target)!.Host! : main;
                var group = Assert.IsAssignableFrom<IToolDock>(target.Owner);
                using (var preview = new Preview(FindArea(destination, group)))
                {
                    var operation = preview.Resolve(moved.Owner!, group, factory);
                    Assert.Equal(DockOperation.Fill, operation);
                    Assert.True(new DockManager(new DockService()).ValidateDockable(moved.Owner!, group, DragAction.Move, operation, true));
                }
                await Flush();
                Assert.Same(group, moved.Owner);
                Assert.Same(model, moved.Model);
                Assert.Same(view, moved.PreparedView);
                Assert.False(sourceWindow.IsVisible);
                Assert.Equal(!targetFloating, moved.CanPin);
                var lifecycle = context.Provider.GetRequiredService<DockLayoutLifecycle>();
                Assert.True(lifecycle.Save(session));
                await lifecycle.FlushAsync();
                saved = context.Provider.GetRequiredService<DockLayoutV3Store>().Load()!;
                DockLayoutV3Validator.Validate(saved);
                Assert.Equal(targetFloating ? 1 : 0, saved.FloatingWindows.Count);
            }
            finally { main.Close(); await Flush(); }
        }
        using var restart = new UiTestContext(initialLayoutV3: saved);
        var reopened = Show(restart);
        try
        {
            await Flush();
            Assert.Same(restart.Workspace.CreatedTools[FirstTool].Owner, restart.Workspace.CreatedTools[SecondTool].Owner);
            Assert.Equal(targetFloating ? 1 : 0, DockTreeNavigator.EnumerateWindows(restart.Workspace.RootDock!).Count());
        }
        finally { reopened.Close(); await Flush(); }
    }

    [AvaloniaTheory]
    [InlineData(DockOperation.Top)]
    [InlineData(DockOperation.Bottom)]
    public async Task 主文档区工具上下分屏使用全宽预览而工具与浮窗目标保持局部(DockOperation operation)
    {
        using var context = new UiTestContext(initialLayoutV3: ToolLayout());
        var session = context.Workspace;
        var main = Show(context);
        try
        {
            await Flush();
            var factory = session.DockFactory;
            var moved = session.CreatedTools[FirstTool];
            var document = Assert.Single(session.GetDocuments());
            var area = FindArea(main, document.Owner!);
            var provider = (IDockPreviewProvider)factory;
            var bounds = provider.GetPreviewBounds(moved, document.Owner!, operation, area);
            Assert.NotNull(bounds);
            var rows = main.GetVisualDescendants().OfType<ProportionalDockControl>()
                .Single(control => control.DataContext is IDock { Id: DockLayoutIds.WorkspaceRows });
            Assert.Equal(rows.Bounds.Width, bounds.Value.Width, 2);
            Assert.Null(provider.GetPreviewBounds(moved, session.CreatedTools[SecondTool].Owner!, operation,
                FindArea(main, session.CreatedTools[SecondTool].Owner!)));
            Assert.True(new DockManager(new DockService()).ValidateDockable(moved, document.Owner!, DragAction.Move, operation, true));
            await Flush();
            Assert.Equal(operation == DockOperation.Top ? DockLayoutIds.TopTools : DockLayoutIds.BottomTools, moved.Owner!.Id);
            area = FindArea(main, document.Owner!);
            // 第二次向同一方向停靠会进入已有工具组，预览应精确指向已存在的组。
            var existing = main.GetVisualDescendants().OfType<ToolDockControl>().Single(control => ReferenceEquals(control.DataContext, moved.Owner));
            var again = provider.GetPreviewBounds(session.CreatedTools[SecondTool], document.Owner!, operation, area);
            Assert.NotNull(again);
            Assert.Equal(existing.Bounds.Size, again.Value.Size);
            factory.FloatDockable(document);
            await Flush();
            var floating = (Window)DockTreeNavigator.FindWindow(session.RootDock!, document)!.Host!;
            Assert.Null(provider.GetPreviewBounds(moved, document.Owner!, operation, FindArea(floating, document.Owner!)));
        }
        finally { main.Close(); await Flush(); }
    }

    [AvaloniaFact]
    public async Task 全屏限制在预览查询阶段生效并在退出后恢复()
    {
        using var context = new UiTestContext();
        var main = Show(context);
        try
        {
            await Flush();
            var document = Assert.Single(context.Workspace.GetDocuments());
            var guard = (IDockDropGuard)context.Workspace.DockFactory;
            using (var fullscreen = ((IWindowContentFullscreenHost)main).TryPresent(new Border()))
            {
                Assert.NotNull(fullscreen);
                Assert.False(guard.CanDrop(document, document.Owner!, DockOperation.Fill));
                Assert.False(guard.CanDrop(document, document.Owner!, DockOperation.Top));
            }
            Assert.True(guard.CanDrop(document, document.Owner!, DockOperation.Fill));
        }
        finally { main.Close(); await Flush(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 文档标签栏和工具标题不属于默认合并正文(bool tool)
    {
        using var context = new UiTestContext(initialLayoutV3: ToolLayout());
        var main = Show(context);
        try
        {
            await Flush();
            var session = context.Workspace;
            IDockable source = session.CreatedTools[FirstTool];
            var target = tool ? session.CreatedTools[SecondTool].Owner! : Assert.Single(session.GetDocuments()).Owner!;
            var area = FindArea(main, target);
            using var preview = new Preview(area);
            var operation = preview.Resolve(source, target, session.DockFactory, new Point(30, 5));
            Assert.True(operation == DockOperation.Window, $"{operation}; area={area.GetType().Name}; template={area.TemplatedParent?.GetType().Name}; " +
                string.Join(";", area.GetVisualDescendants().OfType<Border>().Where(border => border.Name == "PART_Border").Select(border =>
                    $"tpl={border.TemplatedParent?.GetType().Name};model={((border.TemplatedParent as Control)?.DataContext as IDockable)?.Id};same={ReferenceEquals((border.TemplatedParent as Control)?.DataContext, target)};origin={border.TranslatePoint(default, area)};size={border.Bounds.Size}")));
        }
        finally { main.Close(); await Flush(); }
    }

    [AvaloniaFact]
    public async Task 真实标签鼠标输入在内容区松开完成回停()
    {
        using var context = Context();
        var session = context.Workspace;
        var main = Show(context);
        try
        {
            var target = Assert.Single(session.GetDocuments());
            var moved = await session.CreateAndPublishDocumentAsync(DocumentType, new NewDocumentActivation("拖动"));
            session.DockFactory.FloatDockable(moved);
            await Flush();
            var floating = (HostFloatingWindow)DockTreeNavigator.FindWindow(session.RootDock!, moved)!.Host!;
            floating.Width = 400;
            floating.Height = 300;
            floating.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var tab = floating.GetVisualDescendants().OfType<DocumentTabStripItem>().Single(item => ReferenceEquals(item.DataContext, moved));
            var start = tab.TranslatePoint(new Point(15, tab.Bounds.Height / 2), floating)!.Value;
            var area = FindArea(main, target.Owner!);
            // Headless 的屏幕坐标忽略原生 Position；使用源窗尺寸之外的主区落点，避免测试平台
            // 将本应并排的两个窗口视为完全重叠。原生遮挡由专门桌面矩阵验证。
            var finish = floating.PointToClient(area.PointToScreen(new Point(area.Bounds.Width * .8, area.Bounds.Height * .75)));
            floating.MouseDown(start, MouseButton.Left);
            floating.MouseMove(finish, RawInputModifiers.LeftMouseButton);
            Assert.Contains(main.GetVisualDescendants(), visual => visual is DockTarget);
            await Flush();
            floating.MouseMove(finish, RawInputModifiers.LeftMouseButton);
            Assert.NotSame(target.Owner, moved.Owner);
            floating.MouseUp(finish, MouseButton.Left);
            await Flush();
            Assert.Same(target.Owner, moved.Owner);
            Assert.False(floating.IsVisible);
            Assert.False(((ProbeDocument)moved.Model).Disposed);
        }
        finally { main.Close(); await Flush(); }
    }

    private static Control FindArea(Window window, IDockable group)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
        var area = controls.FirstOrDefault(control => DockProperties.GetIsDockTarget(control) && ReferenceEquals(control.DataContext, group) && control.IsEffectivelyVisible);
        Assert.True(area is not null, $"目标 {group.Id}；" + string.Join(";", controls.Where(control => control.GetType().Name.Contains("Dock") || DockProperties.GetIsDockTarget(control))
            .Select(control => $"{control.GetType().Name}/{(control.DataContext as IDockable)?.Id}/{control.IsEffectivelyVisible}/{control.Bounds}")));
        return area!;
    }

    /// <summary>挂到实际内容区域的真实 DockTarget；只让测试驱动命中，不替代 Dock 的能力校验或提交。</summary>
    private sealed class Preview : IDisposable
    {
        private readonly Control _area;
        private readonly AdornerLayer _layer;
        private readonly DockTarget _target = new();
        internal Preview(Control area)
        {
            _area = area;
            _layer = AdornerLayer.GetAdornerLayer(area)!;
            Assert.NotNull(_layer);
            AdornerLayer.SetAdornedElement(_target, area);
            _layer.Children.Add(_target);
            ((Window)TopLevel.GetTopLevel(area)!).UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.True(_target.FillOnAreaDrop); // 验证生产 App 样式实际启用，而非测试手动赋值。
        }
        internal DockOperation Resolve(IDockable source, IDockable target, HostDockFactory factory, Point? point = null) =>
            _target.GetDockOperation(point ?? new Point(30, 100), _area, _area, DragAction.Move, (_, operation, _, _) =>
                ((IDockDropGuard)factory).CanDrop(source, target, operation) &&
                new DockManager(new DockService()).ValidateDockable(source, target, DragAction.Move, operation, false));
        public void Dispose() { ((IDockTarget)_target).Reset(); _layer.Children.Remove(_target); }
    }

    private static MainWindow Show(UiTestContext context)
    {
        var main = new MainWindow(context.Workspace.DockFactory.WindowContext)
            { Width = 1200, Height = 800, Position = default, DataContext = context.ViewModel };
        main.Show();
        return main;
    }
    private static async Task Flush()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
    private static DockLayoutSnapshotV3 ToolLayout() => new(3,
        new("main", DockWindowBounds.Default, DockLayoutNode.Split("restored", "horizontal",
            [DockLayoutNode.Group("first", [FirstTool]) with { Proportion = .25 },
             DockLayoutNode.Documents() with { Proportion = .5 },
             DockLayoutNode.Group("second", [SecondTool]) with { Proportion = .25 }])), [],
        new[] { FirstTool, SecondTool }.Select(id => new DockLayoutTool(id, "visible", DockLayoutIds.LeftTools, 0)).ToArray());
    private static UiTestContext Context() => new(modules: PluginModuleCatalog.CreateForTests([(Plugin, (IPluginModule)new Module())]));
    private sealed class Module : IPluginModule
    {
        public void Configure(IPluginRegistration registration) => registration.AddDocument<ProbeDocument, ProbeView>(
            new(DocumentType, "区域测试", "验证移动不释放资源。", "测试"));
    }
    public sealed class ProbeDocument : IPluginDocument, IDisposable
    {
        public bool Disposed { get; private set; }
        public DocumentPresentationState Presentation { get; } = new("区域测试");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public void Dispose() => Disposed = true;
    }
    public sealed class ProbeView : UserControl;
}
