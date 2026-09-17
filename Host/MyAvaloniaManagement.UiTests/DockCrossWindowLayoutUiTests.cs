using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task 主窗文档合并到已有浮窗后旧布局任务不会越过窗口边界(bool keepAnotherDocument)
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
                view.BeforeMeasure = main.UpdateLayout;
            }
            try
            {
                view.InvalidateArrange();
                view.Editor.InvalidateArrange();
                Assert.True(new DockManager(new DockService()).ValidateDockable(moved, group, DragAction.Move, DockOperation.Fill, true));
                target.UpdateLayout();
                await Flush();
                Dispatcher.UIThread.RunJobs();
                main.UpdateLayout();
                target.UpdateLayout();
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

    public sealed class ProbeView : UserControl
    {
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
