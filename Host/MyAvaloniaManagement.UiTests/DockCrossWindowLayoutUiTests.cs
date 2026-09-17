using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Settings;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 覆盖源窗口继续存活时的正文交接。显式控制布局处理顺序，避免测试提前 Flush
/// 消化旧任务，掩盖实际鼠标投放后才发生的跨窗口布局异常。
/// </summary>
public sealed class DockCrossWindowLayoutUiTests
{
    private static readonly PluginId Plugin = new("myavalonia.plugin.layout-transfer-test");
    private static readonly DocumentTypeId DocumentType = new("myavalonia.plugin.layout-transfer-test.document.probe");

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task 浮窗移出正文后按剩余文档决定保留或关闭源窗(bool targetFloating, bool keepSource)
    {
        using var context = new UiTestContext(modules: PluginModuleCatalog.CreateForTests([(Plugin, (IPluginModule)new Module())]));
        var session = context.Workspace;
        var main = new MainWindow(session.DockFactory.WindowContext)
            { Width = 1000, Height = 700, DataContext = context.ViewModel };
        main.Show();
        try
        {
            var welcome = Assert.Single(session.GetDocuments());
            var moved = await session.CreateAndPublishDocumentAsync(DocumentType, new NewDocumentActivation("移动"));
            session.DockFactory.FloatDockable(moved);
            if (keepSource)
            {
                var retained = await session.CreateAndPublishDocumentAsync(DocumentType, new NewDocumentActivation("留在源窗"));
                Assert.True(new DockManager(new DockService()).ValidateDockable(retained, moved.Owner!, DragAction.Move, DockOperation.Fill, true));
            }
            if (targetFloating) session.DockFactory.FloatDockable(welcome);
            session.ActivateDockable(moved);
            await Flush(); Dispatcher.UIThread.RunJobs();
            var source = (HostFloatingWindow)DockTreeNavigator.FindWindow(session.RootDock!, moved)!.Host!;
            source.UpdateLayout();
            var target = targetFloating ? (Window)DockTreeNavigator.FindWindow(session.RootDock!, welcome)!.Host! : main;
            var view = Assert.IsType<ProbeView>(moved.PreparedView);
            Assert.Same(source, TopLevel.GetTopLevel(view));
            view.Editor.Text = "浮窗转交状态";
            view.InvalidateArrange(); view.Editor.InvalidateArrange();
            Assert.True(new DockManager(new DockService()).ValidateDockable(moved, welcome.Owner!, DragAction.Move, DockOperation.Fill, true));
            await Flush(); Dispatcher.UIThread.RunJobs(); source.UpdateLayout(); target.UpdateLayout();
            Assert.Equal(keepSource, source.IsVisible);
            Assert.Same(target, TopLevel.GetTopLevel(view));
            Assert.Same(view, moved.PreparedView);
            Assert.Same(welcome.Owner, moved.Owner);
            Assert.True(view.IsMeasureValid && view.IsArrangeValid);
            Assert.Equal("浮窗转交状态", view.Editor.Text);
            Assert.Equal(0, ((ProbeDocument)moved.Model).DisposeCount);
            Assert.False(moved.ClosingToken.IsCancellationRequested);
        }
        finally { main.Close(); await Flush(); }
    }

    [AvaloniaFact]
    public void 同一正文和同窗交接不清空当前Presenter()
    {
        var recycling = new DocumentControlRecycling();
        var key = new object();
        var view = new ProbeView();
        recycling.Add(key, view);
        var first = new ContentPresenter { Content = view };
        var second = new ContentPresenter();
        var window = new Window { Content = new StackPanel { Children = { first, second } } };
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.Same(view, recycling.Build(key, view, first));
            Assert.Same(view, first.Child);
            second.Content = recycling.Build(key, null, second); second.UpdateChild(); window.UpdateLayout();
            Assert.Null(first.Child);
            Assert.Same(view, second.Child);
            Assert.Same(window, TopLevel.GetTopLevel(view));
            Assert.True(view.IsMeasureValid && view.IsArrangeValid);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 主窗真实标签拖入已有浮窗正文后两窗可继续布局(bool keepAnotherDocument)
    {
        using var context = new UiTestContext(modules: PluginModuleCatalog.CreateForTests([(Plugin, (IPluginModule)new Module())]));
        var session = context.Workspace;
        var main = new MainWindow(session.DockFactory.WindowContext)
            { Width = 400, Height = 300, DataContext = context.ViewModel };
        main.Show();
        try
        {
            var welcome = Assert.Single(session.GetDocuments());
            if (keepAnotherDocument)
                await session.CreateAndPublishDocumentAsync(DocumentType, new NewDocumentActivation("保留"));
            var moved = await session.CreateAndPublishDocumentAsync(DocumentType, new NewDocumentActivation("拖入"));
            session.DockFactory.FloatDockable(welcome);
            session.ActivateDockable(moved);
            await Flush(); Dispatcher.UIThread.RunJobs(); main.UpdateLayout();
            var target = (HostFloatingWindow)DockTreeNavigator.FindWindow(session.RootDock!, welcome)!.Host!;
            target.Width = 1100; target.Height = 800;
            target.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = Assert.IsType<ProbeView>(moved.PreparedView);
            Assert.Same(main, TopLevel.GetTopLevel(view));
            var tab = main.GetVisualDescendants().OfType<DocumentTabStripItem>().Single(item => ReferenceEquals(item.DataContext, moved));
            var area = target.GetVisualDescendants().OfType<Control>().First(control =>
                DockProperties.GetIsDockTarget(control) && ReferenceEquals(control.DataContext, welcome.Owner) && control.IsEffectivelyVisible);
            var start = tab.TranslatePoint(new Point(15, tab.Bounds.Height / 2), main)!.Value;
            // Headless 不模拟原生 Position。落点置于小源窗以外，确保测试命中已有目标浮窗。
            var finish = main.PointToClient(area.PointToScreen(new Point(area.Bounds.Width * .8, area.Bounds.Height * .75)));
            main.MouseDown(start, MouseButton.Left);
            main.MouseMove(finish, RawInputModifiers.LeftMouseButton);
            await Flush(); Dispatcher.UIThread.RunJobs();
            Assert.Contains(target.GetVisualDescendants(), visual => visual is DockTarget);
            main.MouseMove(finish, RawInputModifiers.LeftMouseButton);
            Assert.NotSame(welcome.Owner, moved.Owner);
            view.InvalidateArrange(); view.Editor.InvalidateArrange();
            view.BeforeMeasure = main.UpdateLayout;
            main.MouseUp(finish, MouseButton.Left);
            await Flush(); Dispatcher.UIThread.RunJobs(); main.UpdateLayout(); target.UpdateLayout();
            Assert.Same(welcome.Owner, moved.Owner);
            Assert.Same(view, moved.PreparedView);
            Assert.Same(target, TopLevel.GetTopLevel(view));
            Assert.True(main.IsVisible && target.IsVisible);
            Assert.True(view.IsMeasureValid && view.IsArrangeValid);
            Assert.True(view.Editor.Bounds.Width > 0);
            Assert.Equal(0, ((ProbeDocument)moved.Model).DisposeCount);
        }
        finally { main.Close(); await Flush(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task 主窗文档合并到已有浮窗后旧布局任务不会越过窗口边界(bool keepAnotherDocument, bool targetFirst)
    {
        using var context = new UiTestContext(modules: PluginModuleCatalog.CreateForTests([(Plugin, (IPluginModule)new Module())]));
        var session = context.Workspace;
        var main = new MainWindow(session.DockFactory.WindowContext)
            { Width = 1000, Height = 700, DataContext = context.ViewModel };
        main.Show();
        try
        {
            var welcome = Assert.Single(session.GetDocuments());
            if (keepAnotherDocument)
                await session.CreateAndPublishDocumentAsync(DocumentType, new NewDocumentActivation("保留"));
            var moved = await session.CreateAndPublishDocumentAsync(DocumentType, new NewDocumentActivation("移动"));
            session.DockFactory.FloatDockable(welcome);
            session.ActivateDockable(moved);
            main.UpdateLayout();
            await Flush();
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
            var target = (HostFloatingWindow)DockTreeNavigator.FindWindow(session.RootDock!, welcome)!.Host!;
            var group = Assert.IsAssignableFrom<IDocumentDock>(welcome.Owner);
            var view = Assert.IsType<ProbeView>(moved.PreparedView);
            Assert.Same(main, TopLevel.GetTopLevel(view));
            view.Editor.Text = "迁移后仍应保留的编辑内容";
            // 新窗口挂接子树时，确定性地让旧窗口先消费尚未处理的任务。这是窗口
            // 渲染先后顺序的边界探针，不修改 Dock 提交，也不重建被测正文。
            var boundaryObserved = false;
            view.AttachedToVisualTree += OnAttached;
            void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
            {
                if (!ReferenceEquals(TopLevel.GetTopLevel(view), target)) return;
                boundaryObserved = true;
                if (!targetFirst) view.BeforeMeasure = main.UpdateLayout;
            }
            try
            {
                view.InvalidateArrange();
                view.Editor.InvalidateArrange();
                Assert.True(new DockManager(new DockService()).ValidateDockable(moved, group, DragAction.Move, DockOperation.Fill, true));
                target.UpdateLayout();
                await Flush();
                Dispatcher.UIThread.RunJobs();
                if (targetFirst) { target.UpdateLayout(); main.UpdateLayout(); }
                else { main.UpdateLayout(); target.UpdateLayout(); }
                Assert.True(boundaryObserved);
                Assert.True(main.IsVisible);
                Assert.Same(group, moved.Owner);
                Assert.Same(view, moved.PreparedView);
                Assert.Same(target, TopLevel.GetTopLevel(view));
                Assert.Equal("迁移后仍应保留的编辑内容", view.Editor.Text);
                Assert.False(moved.ClosingToken.IsCancellationRequested);
                Assert.Equal(0, ((ProbeDocument)moved.Model).DisposeCount);
                Assert.True(view.IsMeasureValid);
                Assert.True(view.IsArrangeValid);
                Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0);
                Assert.Equal(1, DockTreeNavigator.EnumerateWorkspace(session.RootDock!).Count(item => ReferenceEquals(item, moved)));
                // 真正关闭后才释放模型与视图；延迟模板再次请求不能复活旧正文。
                session.DockFactory.CloseDockable(moved);
                await Flush(); Dispatcher.UIThread.RunJobs();
                Assert.True(moved.ClosingToken.IsCancellationRequested);
                Assert.Equal(1, ((ProbeDocument)moved.Model).DisposeCount);
                Assert.Equal(1, view.DisposeCount);
                Assert.Null(TestAppBuilder.ControlRecycling.Build(moved, null, new ContentPresenter()));
                Assert.Equal(1, view.DisposeCount);
            }
            finally { view.AttachedToVisualTree -= OnAttached; }
        }
        finally { main.Close(); await Flush(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void 正文回收器跨窗口转交保留旧根布局队列时仍可完成布局(bool duringArrange)
    {
        var recycling = new DocumentControlRecycling();
        var key = new object();
        var view = new ProbeView();
        recycling.Add(key, view);
        var trigger = new ArrangeCallback();
        var presenter = new ContentPresenter { Content = view };
        var source = new Window { Width = 700, Height = 500, Content = new StackPanel { Children = { trigger, presenter } } };
        var destination = new ContentPresenter();
        var target = new Window { Width = 800, Height = 600, Content = destination };
        try
        {
            source.Show();
            target.Show();
            source.UpdateLayout();
            target.UpdateLayout();
            var transfers = 0;
            void Transfer()
            {
                transfers++;
                destination.Content = recycling.Build(key, null, destination);
                destination.UpdateChild();
            }
            view.InvalidateArrange();
            view.Editor.InvalidateArrange();
            if (duringArrange)
            {
                trigger.Callback = Transfer;
                trigger.InvalidateArrange();
            }
            else Transfer();
            source.UpdateLayout();
            target.UpdateLayout();
            Assert.Equal(1, transfers);
            Assert.Same(view, destination.Child);
            Assert.Same(target, TopLevel.GetTopLevel(view));
            Assert.True(view.IsArrangeValid);
            Assert.True(view.Editor.IsArrangeValid);
            Assert.True(source.IsVisible);
        }
        finally { source.Close(); target.Close(); }
    }

    private static async Task Flush()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private sealed class ArrangeCallback : Control
    {
        internal Action? Callback { get; set; }
        protected override Size ArrangeOverride(Size finalSize)
        {
            var callback = Callback;
            Callback = null;
            callback?.Invoke();
            return finalSize;
        }
    }

    private sealed class Module : IPluginModule
    {
        public void Configure(IPluginRegistration registration) => registration.AddDocument<ProbeDocument, ProbeView>(
            new(DocumentType, "跨窗口布局", "验证正文跨窗口迁移。", "测试"));
    }

    public sealed class ProbeDocument : IPluginDocument, IDisposable
    {
        public int DisposeCount { get; private set; }
        public DocumentPresentationState Presentation { get; } = new("跨窗口布局");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public void Dispose() => DisposeCount++;
    }

    public sealed class ProbeView : UserControl, IDisposable
    {
        internal int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
        internal Action? BeforeMeasure { get; set; }
        protected override Size MeasureOverride(Size availableSize)
        {
            var callback = BeforeMeasure;
            BeforeMeasure = null;
            callback?.Invoke();
            return base.MeasureOverride(availableSize);
        }
        internal TextBox Editor { get; } = new() { Text = "编辑内容" };
        public ProbeView() => Content = new Border
        {
            Padding = new Thickness(8),
            Child = new StackPanel { Children = { new TextBlock { Text = "跨窗口正文" }, Editor } },
        };
    }
}
