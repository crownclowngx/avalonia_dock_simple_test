using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Navigation;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.FunctionCenter;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagement.Views.FunctionCenter;
using MyAvaloniaManagement.Views.Tools;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>加载生产 XAML、主题和创建用例，验证 V6 两种目录及模态选择窗口的完整交互。</summary>
public sealed class PluginNavigationUiTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.navigation-ui");

    [AvaloniaFact]
    public async Task 原Tool双模式模板绑定独立展开并可以直接创建Host文档()
    {
        using var context = CreateContext();
        var model = context.Provider.GetRequiredService<PlugGroupMenuViewModel>();
        var view = new PlugGroupMenuView { DataContext = model };
        var window = new Window { Width = 330, Height = 720, Content = view };
        window.Show();
        try
        {
            var selector = view.FindControl<ComboBox>("PluginMenuModeSelector")!;
            Assert.True(view.FindControl<ScrollViewer>("PluginMenuScrollViewer")!.IsVisible);
            Assert.Contains(model.CategoryNodes, node => node.CategoryName == "闲才业务工具/文本检测");
            model.CategoryNodes.Single(node => node.CategoryName == "闲才业务工具/文本检测").IsExpanded = true;
            await Render(window, "tool-legacy-light");
            selector.SelectedItem = model.Modes.Single(mode => mode.Mode == PluginMenuMode.Tree);
            await Flush();
            Assert.True(model.IsTree);
            var tree = view.FindControl<TreeView>("PluginNavigationTree")!;
            Assert.True(tree.IsVisible);
            foreach (var node in NavigationTreeNode.Flatten(model.TreeNodes).Where(node => node.IsCategory)) node.IsExpanded = true;
            await Flush();
            Assert.NotEmpty(tree.GetVisualDescendants().OfType<TreeViewItem>());
            Assert.All(tree.GetVisualDescendants().OfType<HostIconView>().Where(icon => icon.DataContext is NavigationTreeNode), icon => Assert.NotNull(icon.Renderer!.Resolve(icon.Source).Geometry));
            await Render(window, "tool-tree-light");
            window.RequestedThemeVariant = ThemeVariant.Dark;
            await Render(window, "tool-tree-dark");
            Assert.Contains(model.TreeNodes, node => node.DisplayName == "大唐-会计");
            var leafButton = Assert.Single(tree.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("host-tool-row") &&
                button.DataContext is NavigationTreeNode { Item: { } item } && item.Entry.DocumentTypeId == HostExtensionIds.WelcomeDocument);
            Assert.NotNull(leafButton.Command);
            Click(window, leafButton);
            await model.ActivateTreeNodeCommand.ExecutionTask!;
            Assert.False(context.Provider.GetRequiredService<DocumentOperationState>().HasError);
            selector.SelectedItem = model.Modes.Single(mode => mode.Mode == PluginMenuMode.Legacy);
            Assert.True(model.CategoryNodes.Single(node => node.CategoryName == "闲才业务工具/文本检测").IsExpanded);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task 功能中心单窗口搜索分类实际图标和创建成功闭环()
    {
        using var context = CreateContext();
        var owner = new Window { Width = 1000, Height = 760 };
        owner.Show();
        var service = context.Provider.GetRequiredService<FunctionCenterWindowService>();
        service.Attach(owner);
        try
        {
            var presentation = context.Provider.GetRequiredService<WorkbenchCommandPresentation>();
            var menu = presentation.Menu.GetItems(WorkbenchMenuLocations.FileShared).OfType<WorkbenchMenuCommandProjectionEntry>().ToArray();
            Assert.Equal(HostWorkbenchCommandIds.NewDocument, menu[0].CommandId);
            Assert.True(menu[0].Command.IsEnabled);
            menu[0].Command.Execute(null);
            await Flush();
            var window = Assert.IsType<FunctionCenterWindow>(service.CurrentWindow);
            var model = Assert.IsType<FunctionCenterViewModel>(window.DataContext);
            Assert.Same(owner, window.Owner);
            Assert.True(window.IsDialog);
            service.ShowOrActivate();
            Assert.Same(window, service.CurrentWindow);
            var tree = window.FindControl<TreeView>("FunctionCategoryTree")!;
            tree.SelectedItem = model.Categories.Single(node => node.DisplayName == "闲才业务工具");
            foreach (var node in NavigationTreeNode.Flatten(model.Categories)) node.IsExpanded = true;
            await Flush();
            Assert.Equal(6, model.VisibleItems.Count);
            var search = window.FindControl<TextBox>("FunctionSearchBox")!;
            search.Text = "会计";
            Assert.Equal("全部分类中的搜索结果", model.ResultsTitle);
            Assert.Single(model.VisibleItems);
            search.Text = "";
            Assert.Equal(6, model.VisibleItems.Count);
            await Render(window, "function-center-light");
            window.RequestedThemeVariant = ThemeVariant.Dark;
            await Render(window, "function-center-dark");
            var icons = window.GetVisualDescendants().OfType<HostIconView>().Where(icon => icon.DataContext is DocumentCreationItem).ToArray();
            Assert.NotEmpty(icons);
            Assert.All(icons, icon => Assert.NotNull(icon.Renderer!.Resolve(icon.Source).Geometry));
            var exclusiveIcon = Assert.Single(icons, icon => icon.Source?.Reference?.EndsWith("/analysis", StringComparison.Ordinal) == true);
            Assert.Equal(Owner, exclusiveIcon.Source!.OwnerId);
            Assert.Equal(32, exclusiveIcon.Renderer!.Resolve(exclusiveIcon.Source).Definition.ViewBoxWidth);
            search.Text = "欢迎主程序";
            var list = window.FindControl<ListBox>("FunctionItemsList")!;
            list.SelectedItem = Assert.Single(model.VisibleItems);
            await Flush();
            Click(window, window.FindControl<Button>("CreateDocumentButton")!);
            await model.CreateCommand.ExecutionTask!;
            Assert.Null(service.CurrentWindow);
            Assert.True(owner.IsEnabled);
            Assert.False(context.Provider.GetRequiredService<DocumentOperationState>().HasError);
        }
        finally { service.Dispose(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task 创建中重复输入及窗口关闭不会遗失在途会话()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = CreateContext((services, _) =>
        {
            services.AddSingleton<IHostDockableFactory>(provider => new BlockingFactory(
                new HostDockAdapterFactory(provider.GetRequiredService<WorkspaceCatalog>(),
                    provider.GetRequiredService<HostWorkspaceActivator>(), provider.GetRequiredService<PluginContributionActivator>(),
                    provider.GetRequiredService<ViewLocator>()), release.Task));
        });
        var owner = new Window(); owner.Show();
        var service = context.Provider.GetRequiredService<FunctionCenterWindowService>(); service.Attach(owner);
        service.ShowOrActivate();
        var window = service.CurrentWindow!;
        var model = (FunctionCenterViewModel)window.DataContext!;
        model.SelectedItem = model.VisibleItems.First(item => item.Entry.DocumentTypeId != HostExtensionIds.WelcomeDocument);
        await Flush();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        var pending = model.CreateCommand.ExecutionTask!;
        Assert.NotNull(pending);
        try
        {
            Assert.True(model.IsBusy);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await model.CreateAsync();
            Assert.False(window.FindControl<Button>("CreateDocumentButton")!.IsEnabled);
            window.Close(); owner.Close();
            Assert.True(window.IsVisible);
            Assert.True(owner.IsVisible);
            // 显式 Dispose 也必须等待当前任务返回再释放窗口引用，不能留下无主的模态窗口。
            service.Dispose();
            Assert.Same(window, service.CurrentWindow);
            release.SetResult();
            await pending;
            Assert.Null(service.CurrentWindow);
            Assert.False(window.IsVisible);
        }
        finally { release.TrySetResult(); await pending; service.Dispose(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task 取消不创建且重复打开关闭释放会话()
    {
        using var context = CreateContext();
        var owner = new Window(); owner.Show();
        var service = context.Provider.GetRequiredService<FunctionCenterWindowService>(); service.Attach(owner);
        for (var index = 0; index < 12; index++)
        {
            service.ShowOrActivate();
            var window = service.CurrentWindow!;
            var model = (FunctionCenterViewModel)window.DataContext!;
            Assert.False(model.CanCreate);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await Flush();
            Assert.Null(service.CurrentWindow);
            Assert.Null(window.DataContext);
            Assert.True(owner.IsEnabled);
        }
        owner.Close();
        service.ShowOrActivate();
        Assert.Null(service.CurrentWindow);
    }

    [AvaloniaFact]
    public async Task 可用性撤回同步更新两种Tool与功能中心且已关闭会话不再刷新()
    {
        using var context = CreateContext();
        var tool = context.Provider.GetRequiredService<PlugGroupMenuViewModel>();
        using var center = new FunctionCenterViewModel(context.Provider.GetRequiredService<DocumentCreationMenuQuery>(),
            context.Provider.GetRequiredService<DocumentPersistenceCoordinator>(), context.Provider.GetRequiredService<DocumentOperationState>(), context.Provider.GetRequiredService<HostIconRenderer>());
        center.SelectedItem = center.VisibleItems.First(item => item.Entry.DocumentTypeId != HostExtensionIds.WelcomeDocument);
        context.Provider.GetRequiredService<PluginLifecycleStateStore>().BeginShutdown();
        await Flush();
        Assert.Single(center.VisibleItems);
        Assert.Null(center.SelectedItem);
        Assert.False(center.CanCreate);
        Assert.Single(tool.CategoryNodes);
        Assert.Single(tool.TreeNodes);
        center.Dispose();
        tool.Dispose();
    }

    [AvaloniaFact]
    public void 所有内置图标均可解析且不同控件复用几何而不复用控件()
    {
        using var context = CreateContext();
        var renderer = context.Provider.GetRequiredService<HostIconRenderer>();
        foreach (var key in MyAvaloniaManagement.Icons.CommonIcons.All.Select(asset => asset.Key))
        {
            var geometry = renderer.Resolve(new(null, key)).Geometry;
            Assert.True(geometry.Bounds.Width > 0);
            var first = new HostIconView { Renderer = renderer, Source = new(null, key) };
            var second = new HostIconView { Renderer = renderer, Source = new(null, key) };
            _ = new StackPanel { Children = { first, second } };
            Assert.NotSame(first, second);
        }
        var items = context.Provider.GetRequiredService<DocumentCreationMenuQuery>().ReadDirectory().Items;
        Assert.Equal("builtin:text-check", items.Single(item => item.Entry.CreationIntentId?.Value == "normal").IconKey);
        Assert.Equal(HostIconCatalog.DefaultKey, context.Provider.GetRequiredService<HostIconCatalog>().Resolve(items.Single(item => item.Entry.CreationIntentId?.Value == "fast").IconRequest).Reference);
    }

    [AvaloniaTheory]
    [InlineData("帮助")]
    [InlineData("闲才业务工具")]
    public async Task 可用性刷新绑定不会被误认为用户切换分类而清空搜索(string category)
    {
        using var context = CreateContext();
        var owner = new Window(); owner.Show();
        var service = context.Provider.GetRequiredService<FunctionCenterWindowService>(); service.Attach(owner);
        try
        {
            service.ShowOrActivate();
            var window = service.CurrentWindow!;
            var model = (FunctionCenterViewModel)window.DataContext!;
            window.FindControl<TreeView>("FunctionCategoryTree")!.SelectedItem = model.Categories.Single(node => node.DisplayName == category);
            window.FindControl<TextBox>("FunctionSearchBox")!.Text = "欢迎";
            window.FindControl<ListBox>("FunctionItemsList")!.SelectedItem = Assert.Single(model.VisibleItems);
            context.Provider.GetRequiredService<PluginLifecycleStateStore>().BeginShutdown();
            await Flush();
            Assert.Equal("欢迎", model.SearchText);
            Assert.Equal("全部分类中的搜索结果", model.ResultsTitle);
            Assert.Equal(HostExtensionIds.WelcomeDocument, Assert.Single(model.VisibleItems).Entry.DocumentTypeId);
            Assert.NotNull(model.SelectedItem);
            Assert.Equal(category == "帮助" ? "帮助" : null, model.SelectedCategory?.DisplayName);
        }
        finally { service.Dispose(); owner.Close(); }
    }

    private static UiTestContext CreateContext(Action<IServiceCollection, PluginRegistryBuilder>? extra = null) => new((services, builder) =>
    {
        Register<Table>(services, builder, "table", "Excel 批量审核", "闲才业务工具/文本检测", "builtin:table");
        var exclusive = builder.AddIcon(Owner, "analysis", new("M1,1 H31 V15 H1 Z M3,3 V13 H29 V3 Z", 32, 16));
        Register<Analysis>(services, builder, "analysis", "审核结果分析", "闲才业务工具/文本检测", exclusive);
        Register<Text>(services, builder, "text", "文本智能审核", "闲才业务工具/文本检测", "builtin:text-check",
            [new(new("normal"), "文本智能审核"), new(new("fast"), "快速文本审核", iconPath: "unknown")]);
        Register<Image>(services, builder, "image", "图片审核", "闲才业务工具/图片检测", "builtin:image");
        Register<Home>(services, builder, "home", "业务首页", "闲才业务工具", "");
        Register<Accounting>(services, builder, "accounting", "会计报表", "大唐-会计", "builtin:chart");
        extra?.Invoke(services, builder);
    });

    private static void Register<T>(IServiceCollection services, PluginRegistryBuilder builder, string id, string title,
        string category, string icon, IEnumerable<DocumentCreationIntentDescriptor>? intents = null)
    {
        services.AddScoped<MetadataDocument<T>>();
        builder.AddDocument(Owner, new(new DocumentTypeId($"{Owner.Value}.document.{id}"), title,
                $"{title}：选择业务数据并查看处理结果。", category, icon, intents),
            typeof(MetadataDocument<T>), typeof(MetadataView<T>), static () => new MetadataView<T>(), false);
    }

    private static async Task Flush()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(30);
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
    }

    private static async Task Render(Window window, string name)
    {
        await Flush();
        var directory = Environment.GetEnvironmentVariable("MYAVALONIA_V6_RENDER_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private sealed class Table;
    private sealed class Analysis;
    private sealed class Text;
    private sealed class Image;
    private sealed class Home;
    private sealed class Accounting;
    private sealed class MetadataView<T> : UserControl;

    /// <summary>仅提供元数据的测试插件；若浏览阶段错误地执行工厂，初始化立即失败暴露违规。</summary>
    private sealed class MetadataDocument<T> : IPluginDocument
    {
        public DocumentPresentationState Presentation { get; } = new("测试功能");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("目录浏览不应初始化元数据测试插件。");
    }

    /// <summary>只替换异步插件初始化的时序，不复制生产注册、Scope 或文档发布实现。</summary>
    private sealed class BlockingFactory(IHostDockableFactory inner, Task release) : IHostDockableFactory
    {
        public Document CreateHostDocument(DocumentTypeId id, NewDocumentActivation activation) => inner.CreateHostDocument(id, activation);
        public Tool CreateTool(ToolTypeId id) => inner.CreateTool(id);
        public async ValueTask<Document> CreateDocumentAsync(DocumentTypeId id, DocumentActivation activation)
        {
            await release;
            throw new InvalidOperationException("模拟插件初始化失败，确认窗口解除忙碌后可以关闭。");
        }
    }
}
