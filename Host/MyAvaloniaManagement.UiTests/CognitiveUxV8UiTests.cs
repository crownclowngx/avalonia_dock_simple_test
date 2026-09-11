using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.Welcome;
using MyAvaloniaManagement.ViewModels.ToolCenter;
using MyAvaloniaManagement.Views;
using MyAvaloniaManagement.Views.ToolCenter;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>真实 XAML、输入和插件初始化闭环；不以截图代替行为断言。</summary>
public sealed class CognitiveUxV8UiTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.v8-ui");
    private static readonly DocumentTypeId TypeId = new($"{Owner.Value}.document.test");

    [AvaloniaFact]
    public async Task 欢迎开始使用与文件菜单打开同一功能中心且旧目录保持隐藏()
    {
        using var context = new UiTestContext();
        var owner = CreateWindow(context);
        var service = context.Provider.GetRequiredService<FunctionCenterWindowService>();
        service.Attach(owner);
        try
        {
            var welcome = Assert.IsType<WelcomeViewModel>(context.Workspace.GetDocuments().Single().Model);
            welcome.OpenFunctionCenterCommand.Execute(null);
            await Flush();
            var first = Assert.IsAssignableFrom<Window>(service.CurrentWindow);
            Assert.Equal("功能中心", first.Title);
            var menu = context.Provider.GetRequiredService<WorkbenchCommandPresentation>().Menu.GetItems(WorkbenchMenuLocations.FileShared)
                .OfType<WorkbenchMenuCommandProjectionEntry>().Single(item => item.CommandId == HostWorkbenchCommandIds.NewDocument);
            menu.Command.Execute(null);
            Assert.Same(first, service.CurrentWindow);
            Assert.Null(DockTreeNavigator.FindToolDock(context.Workspace.RootDock!, context.Workspace.CreatedTools[HostExtensionIds.PluginMenu.Value]));
            Assert.Single(context.Workspace.GetDocuments());
        }
        finally { service.Dispose(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task 搜索异步失败保留查询选择并阻止重复输入和窗口假取消且重试成功后交还焦点()
    {
        var probe = new Probe { Blocker = new(), Fail = true };
        using var context = CreateContext(probe);
        var window = CreateWindow(context);
        try
        {
            OpenPalette(window);
            var palette = window.FindControl<CommandPaletteView>("CommandPaletteHost")!;
            var search = palette.FindControl<TextBox>("SearchBox")!;
            var list = palette.FindControl<ListBox>("PaletteItems")!;
            search.Text = "V8 功能";
            await Flush();
            var selected = Assert.IsType<WorkbenchCommandPaletteProjectionEntry>(list.SelectedItem).StableKey;
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            var pending = palette.CurrentExecution;
            Assert.True(palette.IsBusy);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.Close();
            Assert.True(window.IsVisible);
            Assert.True(window.FindControl<Border>("CommandPaletteLayer")!.IsVisible);
            Assert.Same(pending, palette.CurrentExecution);
            Assert.Single(probe.Activations);
            Assert.Equal(selected, Assert.IsType<WorkbenchCommandPaletteProjectionEntry>(list.SelectedItem).StableKey);
            probe.Blocker.SetResult();
            await pending;
            await Flush();
            Assert.False(palette.IsBusy);
            Assert.Equal("V8 功能", search.Text);
            Assert.Equal(selected, Assert.IsType<WorkbenchCommandPaletteProjectionEntry>(list.SelectedItem).StableKey);
            Assert.Contains("未新增页面", palette.FindControl<TextBlock>("OperationStatus")!.Text);
            Assert.Single(context.Workspace.GetDocuments());
            Assert.Equal(1, probe.Disposed);
            await Render(window, "palette-failure");
            probe.Fail = false;
            search.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await palette.CurrentExecution;
            await Flush();
            Assert.False(window.FindControl<Border>("CommandPaletteLayer")!.IsVisible);
            Assert.Equal(2, context.Workspace.GetDocuments().Count);
            var page = context.Workspace.GetActiveDocument()!;
            Assert.Equal(TypeId, page.Registration.Descriptor.DocumentTypeId);
            await Render(window, "palette-success");
            var editor = Assert.IsType<EditorView>(page.PreparedView).Editor;
            Assert.True(editor.IsFocused, $"attached={TopLevel.GetTopLevel(editor) is not null}; visible={editor.IsEffectivelyVisible}; focused={window.FocusManager?.GetFocusedElement()}");
            Assert.All(probe.Activations, activation => Assert.Equal(new CreationIntentId("intent-0"), Assert.IsType<NewDocumentActivation>(activation).CreationIntentId));
        }
        finally { probe.Blocker.TrySetResult(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task 同名页面选择按实例切换并保留编辑内容且消失目标不新建()
    {
        var probe = new Probe();
        using var context = CreateContext(probe);
        var window = CreateWindow(context);
        try
        {
            var first = await context.Workspace.CreateAndPublishDocumentAsync(TypeId, new NewDocumentActivation("同名页面"));
            var second = await context.Workspace.CreateAndPublishDocumentAsync(TypeId, new NewDocumentActivation("同名页面"));
            var editor = Assert.IsType<EditorView>(first.PreparedView).Editor;
            editor.Text = "用户尚未处理的内容";
            OpenPalette(window);
            var palette = window.FindControl<CommandPaletteView>("CommandPaletteHost")!;
            var search = palette.FindControl<TextBox>("SearchBox")!;
            var list = palette.FindControl<ListBox>("PaletteItems")!;
            search.Text = "同名页面";
            await Flush();
            var items = list.Items.Cast<WorkbenchCommandPaletteProjectionEntry>().ToArray();
            Assert.Equal(2, items.Length);
            Assert.Equal(2, items.Select(item => item.Description).Distinct().Count());
            list.SelectedItem = items.Single(item => item.Identity == new PagePaletteIdentity(first.PageId));
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await palette.CurrentExecution;
            await Flush();
            Assert.Same(first, context.Workspace.GetActiveDocument());
            Assert.True(editor.IsFocused);
            Assert.Equal("用户尚未处理的内容", editor.Text);
            Assert.Equal(2, probe.Activations.Count);
            OpenPalette(window);
            search.Text = "同名页面";
            await Flush();
            list.SelectedItem = list.Items.Cast<WorkbenchCommandPaletteProjectionEntry>().Single(item => item.Identity == new PagePaletteIdentity(second.PageId));
            context.Workspace.DockFactory.CloseDockable(second);
            // 在投影排队刷新前提交旧绑定，验证执行复查，而非仅验证查询时过滤。
            await palette.ExecuteSelectionAsync();
            Assert.True(window.FindControl<Border>("CommandPaletteLayer")!.IsVisible);
            Assert.Contains("暂不可用", palette.FindControl<TextBlock>("OperationStatus")!.Text);
            Assert.Equal(2, probe.Activations.Count);
            Assert.Single(list.Items);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await Flush();
            Assert.True(editor.IsFocused);
            context.Provider.GetRequiredService<MyAvaloniaManagement.Business.Lifecycle.PluginLifecycleStateStore>().BeginShutdown();
            await Flush();
            Assert.False(context.Workspace.TryActivatePage(first.PageId));
            Assert.False(context.Workspace.GetOpenPages().Single(item => item.Id == first.PageId).CanActivate);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task 搜索定位分割区域页面后文档目标和输入焦点一致()
    {
        using var context = CreateContext(new());
        var window = CreateWindow(context);
        try
        {
            var first = await context.Workspace.CreateAndPublishDocumentAsync(TypeId, new NewDocumentActivation("左侧页面"));
            var second = await context.Workspace.CreateAndPublishDocumentAsync(TypeId, new NewDocumentActivation("分割页面"));
            var dock = Assert.IsAssignableFrom<Dock.Model.Controls.IDocumentDock>(second.Owner);
            Assert.True(new Dock.Model.DockService().SplitDockable(second, dock, dock, Dock.Model.Core.DockOperation.Right, true));
            await Flush();
            Assert.True(context.Workspace.TryActivatePage(first.PageId));
            OpenPalette(window);
            var palette = window.FindControl<CommandPaletteView>("CommandPaletteHost")!;
            palette.FindControl<TextBox>("SearchBox")!.Text = "分割页面";
            await Flush();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await palette.CurrentExecution;
            await Flush();
            Assert.Same(second, context.Workspace.GetActiveDocument());
            Assert.True(Assert.IsType<EditorView>(second.PreparedView).Editor.IsFocused);
            context.Workspace.DockFactory.CloseDockable(second);
            Assert.NotSame(second, context.Workspace.GetActiveDocument());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 百入口长名称与不同主题可滚动且查询不创建插件(bool dark)
    {
        var probe = new Probe();
        using var context = CreateContext(probe, 100);
        var window = CreateWindow(context);
        window.Width = 800; window.Height = 650;
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            OpenPalette(window);
            var palette = window.FindControl<CommandPaletteView>("CommandPaletteHost")!;
            var list = palette.FindControl<ListBox>("PaletteItems")!;
            palette.FindControl<TextBox>("SearchBox")!.Text = "V8";
            await Flush();
            Assert.Equal(100, list.ItemCount);
            var title = palette.GetVisualDescendants().OfType<TextBlock>().First(item => item.Text == "命令面板");
            var foreground = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(title.Foreground);
            Assert.True(dark ? foreground.Color.R >= 200 : foreground.Color.R < 100);
            Assert.Empty(probe.Activations);
            Assert.True(list.GetVisualDescendants().OfType<ListBoxItem>().Count() < 100);
            Assert.True(palette.Bounds.Width <= window.ClientSize.Width);
            list.SelectedIndex = 99;
            list.ScrollIntoView(list.SelectedItem!);
            await Render(window, dark ? "palette-long-dark" : "palette-long-light");
            palette.FindControl<TextBox>("SearchBox")!.Text = "不可能匹配的内容";
            await Flush();
            Assert.Empty(list.Items);
            Assert.True(palette.FindControl<TextBlock>("EmptyState")!.IsVisible);
            await Render(window, dark ? "palette-empty-dark" : "palette-empty-light");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task 工具更多菜单捕获原行且来源可清除并由整理菜单全局隐藏()
    {
        using var context = new UiTestContext();
        var owner = CreateWindow(context);
        var service = context.Provider.GetRequiredService<ToolCenterWindowService>();
        service.Attach(owner);
        try
        {
            service.ShowOrActivate();
            var window = Assert.IsType<ToolCenterWindow>(service.CurrentWindow);
            var vm = Assert.IsType<ToolCenterViewModel>(window.DataContext);
            context.Workspace.OpenTool(HostExtensionIds.FileSystemTree.Value);
            context.Workspace.OpenTool(HostExtensionIds.PluginMenu.Value);
            await Flush();
            var original = vm.VisibleItems.Single(item => item.ToolId == HostExtensionIds.FileSystemTree.Value);
            var more = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "⋯") && button.DataContext is ToolCenterItem item && item.ToolId == original.ToolId);
            more.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await Flush();
            Assert.NotNull(more.ContextMenu);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await Flush();
            Assert.True(window.IsVisible);
            more.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await Flush();
            var menu = more.ContextMenu!;
            vm.SelectedItem = vm.VisibleItems.Single(item => item.ToolId == HostExtensionIds.PluginMenu.Value);
            vm.SearchText = "插件";
            var hide = Assert.IsType<MenuItem>(menu.Items[0]);
            hide.Command!.Execute(hide.CommandParameter);
            menu.Close();
            await Flush();
            Assert.False(context.Provider.GetRequiredService<ToolWorkspaceReadModel>().Capture().Single(item => item.ToolId == original.ToolId).IsVisible);
            Assert.True(context.Provider.GetRequiredService<ToolWorkspaceReadModel>().Capture().Single(item => item.ToolId == HostExtensionIds.PluginMenu.Value).IsVisible);
            vm.SelectedSource = vm.Sources.Single(item => item.Id == "host");
            Assert.True(vm.HasSourceFilter);
            Assert.Contains("来源", vm.ResultsTitle);
            vm.ClearSourceCommand.Execute(null);
            Assert.False(vm.HasSourceFilter);
            Assert.Equal("插件", vm.SearchText);
            context.Workspace.OpenTool(original.ToolId);
            await Flush();
            var organize = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "整理工具"));
            organize.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var organizeMenu = organize.ContextMenu!;
            var hideAll = Assert.IsType<MenuItem>(organizeMenu.Items[1]);
            Assert.Equal("隐藏所有工具（整个工作区）", hideAll.Header);
            hideAll.Command!.Execute(null);
            organizeMenu.Close();
            Assert.All(context.Provider.GetRequiredService<ToolWorkspaceReadModel>().Capture(), item => Assert.False(item.IsVisible));
            Assert.Single(context.Workspace.GetDocuments());
        }
        finally { service.Dispose(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task 隐藏期间真实后台任务继续处理且恢复展示原状态()
    {
        using var worker = new BackgroundWork();
        using var context = new UiTestContext(modules: PluginModuleCatalog.CreateForTests([(Owner, (IPluginModule)new BackgroundModule(worker))]));
        var window = CreateWindow(context);
        var id = $"{Owner.Value}.tool.background";
        try
        {
            var adapter = Assert.IsType<MyAvaloniaManagement.Business.Docking.ManagedToolDockable>(context.Workspace.CreatedTools[id]);
            var view = adapter.PreparedView;
            context.Workspace.OpenTool(id);
            window.UpdateLayout();
            await worker.AdvanceAsync();
            Assert.Equal(1, worker.Completed);
            context.Workspace.SetToolVisibility(id, false);
            await worker.AdvanceAsync();
            await worker.AdvanceAsync();
            Assert.Equal(3, worker.Completed);
            Assert.False(worker.Stopped);
            context.Workspace.OpenTool(id);
            window.UpdateLayout();
            await Flush();
            Assert.Same(view, adapter.PreparedView);
            Assert.Same(worker, Assert.IsType<BackgroundTool>(adapter.Model).Worker);
            Assert.Equal("已处理 3 项", Assert.IsType<BackgroundView>(view).Counter.Text);
        }
        finally { window.Close(); }
    }

    private sealed class BackgroundModule(BackgroundWork worker) : IPluginModule
    {
        public void Configure(IPluginRegistration registration)
        {
            registration.Services.AddSingleton(worker);
            registration.AddTool<BackgroundTool, BackgroundView>(new(new($"{Owner.Value}.tool.background"), "后台任务", "验证隐藏期间持续工作", ToolDockSide.Right, ToolCloseBehavior.Hide));
        }
    }
    private sealed class BackgroundTool(BackgroundWork worker)
    {
        public BackgroundWork Worker { get; } = worker;
    }
    private sealed class BackgroundView : UserControl
    {
        internal TextBlock Counter { get; } = new();
        public BackgroundView()
        {
            Content = Counter;
            Counter.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding("Worker.Completed") { StringFormat = "已处理 {0} 项" });
        }
    }
    /// <summary>独立工作线程处理受控请求；等待完成点代替毫秒级计时断言。</summary>
    private sealed class BackgroundWork : IDisposable, System.ComponentModel.INotifyPropertyChanged
    {
        private readonly System.Threading.Channels.Channel<TaskCompletionSource> _requests = System.Threading.Channels.Channel.CreateUnbounded<TaskCompletionSource>();
        private readonly Task _runner;
        private int _completed;
        public int Completed => Volatile.Read(ref _completed);
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        internal bool Stopped;
        internal BackgroundWork() => _runner = Task.Run(async () =>
        {
            await foreach (var request in _requests.Reader.ReadAllAsync())
            {
                Interlocked.Increment(ref _completed);
                PropertyChanged?.Invoke(this, new(nameof(Completed)));
                request.SetResult();
            }
            Stopped = true;
        });
        internal Task AdvanceAsync()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(_requests.Writer.TryWrite(completion));
            return completion.Task;
        }
        public void Dispose() { _requests.Writer.TryComplete(); _runner.GetAwaiter().GetResult(); }
    }

    private static MainWindow CreateWindow(UiTestContext context)
    {
        var window = new MainWindow { DataContext = context.ViewModel, Width = 1100, Height = 800 };
        window.Show();
        return window;
    }

    private static void OpenPalette(Window window) => window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Shift);
    private static async Task Flush() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    private static async Task Render(Window window, string name)
    {
        await Flush();
        var path = Environment.GetEnvironmentVariable("MYAVALONIA_V8_RENDER_DIRECTORY");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(path, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static UiTestContext CreateContext(Probe probe, int intents = 1) => new(modules:
        PluginModuleCatalog.CreateForTests([(Owner, (IPluginModule)new TestModule(probe, intents))]));

    private sealed class TestModule(Probe probe, int intents) : IPluginModule
    {
        public void Configure(IPluginRegistration registration)
        {
            registration.Services.AddSingleton(probe);
            registration.AddDocument<TestDocument, EditorView>(new(TypeId, "V8 功能", "测试异步打开和页面定位", "业务/审核", "builtin:text-check",
                Enumerable.Range(0, intents).Select(index => new DocumentCreationIntentDescriptor(new($"intent-{index}"),
                    intents == 1 ? "V8 功能" : $"V8 长中文名称用于检查窗口缩小时搜索内容仍然可以识别和滚动的功能入口 {index:D3}"))));
        }
    }

    private sealed class Probe
    {
        internal TaskCompletionSource? Blocker;
        internal bool Fail;
        internal int Disposed;
        internal List<DocumentActivation> Activations { get; } = [];
    }

    private sealed class EditorView : UserControl
    {
        internal TextBox Editor { get; } = new() { Text = "页面内容", AcceptsReturn = true };
        public EditorView() => Content = Editor;
    }

    /// <summary>只控制外部初始化时序与失败，不替代 Host 的 Scope、发布或回滚。</summary>
    private sealed class TestDocument(Probe probe) : IPluginDocument, IDisposable
    {
        public DocumentPresentationState Presentation { get; private set; } = new("V8 页面");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public async ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
        {
            probe.Activations.Add(activation);
            Presentation = new(activation is NewDocumentActivation { Title.Length: > 0 } created ? created.Title : "V8 页面");
            if (probe.Blocker is not null) await probe.Blocker.Task.WaitAsync(cancellationToken);
            if (probe.Fail) throw new InvalidOperationException("测试初始化失败");
        }
        public void Dispose() => probe.Disposed++;
    }
}
