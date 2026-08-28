using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Recycling;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Dock.Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.ViewModels.Welcome;
using MyAvaloniaManagement.ViewModels.Bindings;
using MyAvaloniaManagement.ViewModels.Design;
using MyAvaloniaManagement.Views;
using MyAvaloniaManagement.Views.Welcome;
using MyAvaloniaManagement.Views.Tools;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 使用生产 XAML 验证应用资源、主窗口、命令绑定和 ViewLocator。
/// </summary>
public sealed class ApplicationAndWindowTests
{
    [AvaloniaFact]
    public void 生产应用资源和主题可以在无头平台加载()
    {
        var application = Assert.IsType<App>(Application.Current);
        Assert.True(application.Resources.TryGetResource(
            DocumentControlRecycling.ResourceKey,
            null,
            out var resource));
        Assert.Same(TestAppBuilder.ControlRecycling, resource);
        Assert.Same(application.ControlRecycling, resource);
        Assert.NotEmpty(application.Styles);

        var dock = new DockControl();
        var window = new Window { Content = dock };
        window.Show();
        Assert.Same(
            resource,
            ControlRecyclingDataTemplate.GetControlRecycling(dock));
        window.Close();
    }

    [AvaloniaFact]
    public void 主窗体和全部宿主视图可以实例化并完成布局绑定()
    {
        using var context = new UiTestContext();
        var window = new MainWindow
        {
            DataContext = context.ViewModel
        };

        window.Show();

        var dock = window.GetLogicalDescendants()
            .OfType<DockControl>()
            .Single();
        Assert.Same(context.ViewModel.Layout, dock.Layout);
        Assert.Single(window.KeyBindings);
        Assert.Equal(
            new KeyGesture(Key.S, KeyModifiers.Control),
            window.KeyBindings[0].Gesture);
        Assert.NotNull(window.KeyBindings[0].Command);
        Assert.IsType<MainView>(window.Content is Grid grid
            ? grid.Children[0]
            : null);
        _ = new MenuView();
        _ = new FileSystemTreeView();
        _ = new PlugGroupMenuView();
        _ = new ToolManagementView();

        window.Close();
        Assert.True(File.Exists(context.LayoutPath));
    }

    [AvaloniaFact]
    public void 文件树视图不覆盖策略注入的运行时DataContext()
    {
        var view = new FileSystemTreeView();
        Assert.Null(view.DataContext);

        var sentinel = new object();
        view.DataContext = sentinel;
        Assert.Same(sentinel, view.DataContext);
    }

    [AvaloniaFact]
    public void 设计时数据实现窄绑定端口且提供纯内存样例()
    {
        IMainWindowViewBindings main = new MainWindowDesignData();
        IFileSystemTreeViewBindings files = new FileSystemTreeDesignData();

        Assert.NotNull(main.Layout);
        Assert.True(main.HasDocumentOperationError);
        Assert.Equal(
            2,
            main.WorkbenchCommands.Menu
                .GetItems(WorkbenchMenuLocations.FileShared)
                .OfType<MyAvaloniaManagement.Business.Presentation.Commands
                    .WorkbenchMenuCommandProjectionEntry>()
                .Count());
        Assert.Single(main.WorkbenchCommands.KeyBindings.Items);
        Assert.NotEmpty(files.RootNodes);
        Assert.NotEmpty(files.RootNodes[0].Children);
        Assert.NotNull(files.SelectFolderCommand);
        Assert.NotNull(files.RefreshAllCommand);
    }

    [AvaloniaFact]
    public void 文档操作错误条_随宿主状态显示并可关闭()
    {
        using var context = new UiTestContext();
        var view = new MainView { DataContext = context.ViewModel };
        var window = new Window { Content = view };
        window.Show();
        var banner = view.FindControl<Border>("DocumentOperationErrorBanner")!;

        Assert.False(banner.IsVisible);
        context.Provider.GetRequiredService<DocumentOperationState>()
            .Apply(DocumentOperationResult.Failure("测试错误，原文件未修改。"));
        Assert.True(banner.IsVisible);
        context.ViewModel.DismissDocumentOperationErrorCommand.Execute(null);
        Assert.False(banner.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void 主窗体内容全屏租约排他且重复释放安全()
    {
        var window = new MainWindow();
        var fullscreen = (IWindowContentFullscreenHost)window;
        var firstContent = new Border();
        var secondContent = new Border();
        var layer = window.FindControl<Border>("ContentFullscreenLayer")!;
        var host = window.FindControl<ContentControl>("ContentFullscreenHost")!;

        var firstLease = Assert.IsAssignableFrom<IDisposable>(
            fullscreen.TryPresent(firstContent));
        Assert.Null(fullscreen.TryPresent(firstContent));
        Assert.Null(fullscreen.TryPresent(secondContent));
        Assert.True(layer.IsVisible);
        Assert.Same(firstContent, host.Content);

        firstLease.Dispose();
        firstLease.Dispose();
        Assert.False(layer.IsVisible);
        Assert.Null(host.Content);

        var secondLease = Assert.IsAssignableFrom<IDisposable>(
            fullscreen.TryPresent(secondContent));
        firstLease.Dispose();
        Assert.True(layer.IsVisible);
        Assert.Same(secondContent, host.Content);

        secondLease.Dispose();
        Assert.False(layer.IsVisible);
        Assert.Null(host.Content);
    }

    [AvaloniaFact]
    public void 全屏接口拒绝空内容且挂载失败后仍可再次展示()
    {
        using var context = new UiTestContext();
        var window = new MainWindow { DataContext = context.ViewModel };
        var fullscreen = (IWindowContentFullscreenHost)window;
        var failingContent = new Border();
        failingContent.AttachedToLogicalTree += static (_, _) =>
            throw new InvalidOperationException("测试注入的挂载失败。");
        var layer = window.FindControl<Border>("ContentFullscreenLayer")!;
        var host = window.FindControl<ContentControl>("ContentFullscreenHost")!;
        window.Show();

        Assert.Throws<ArgumentNullException>(() =>
            fullscreen.TryPresent(null!));
        Assert.Throws<InvalidOperationException>(() =>
            fullscreen.TryPresent(failingContent));
        Assert.False(layer.IsVisible);
        Assert.Null(host.Content);

        var lease = Assert.IsAssignableFrom<IDisposable>(
            fullscreen.TryPresent(new Border()));
        lease.Dispose();
        window.Close();
    }

    [AvaloniaFact]
    public async Task 有效全屏租约拒绝工作线程首次释放且不会被消耗()
    {
        var window = new MainWindow();
        var fullscreen = (IWindowContentFullscreenHost)window;
        var layer = window.FindControl<Border>("ContentFullscreenLayer")!;
        var lease = Assert.IsAssignableFrom<IDisposable>(
            fullscreen.TryPresent(new Border()));

        var exception = await Task.Run(() => Record.Exception(lease.Dispose));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.True(layer.IsVisible);
        lease.Dispose();
        Assert.False(layer.IsVisible);
    }

    [AvaloniaFact]
    public void 窗口关闭自动失效租约且旧令牌保持幂等()
    {
        using var context = new UiTestContext();
        var window = new MainWindow { DataContext = context.ViewModel };
        var fullscreen = (IWindowContentFullscreenHost)window;
        var layer = window.FindControl<Border>("ContentFullscreenLayer")!;
        var host = window.FindControl<ContentControl>("ContentFullscreenHost")!;
        window.Show();
        var lease = Assert.IsAssignableFrom<IDisposable>(
            fullscreen.TryPresent(new Border()));

        window.Close();

        Assert.False(layer.IsVisible);
        Assert.Null(host.Content);
        Assert.Null(fullscreen.TryPresent(new Border()));
        lease.Dispose();
    }

    [AvaloniaFact]
    public void 内容宿主脱离视觉树后自动恢复并永久拒绝新展示()
    {
        using var context = new UiTestContext();
        var window = new MainWindow { DataContext = context.ViewModel };
        var fullscreen = (IWindowContentFullscreenHost)window;
        var layer = window.FindControl<Border>("ContentFullscreenLayer")!;
        var host = window.FindControl<ContentControl>("ContentFullscreenHost")!;
        window.Show();
        var lease = Assert.IsAssignableFrom<IDisposable>(
            fullscreen.TryPresent(new Border()));

        window.Content = null;

        Assert.False(layer.IsVisible);
        Assert.Null(host.Content);
        Assert.Null(fullscreen.TryPresent(new Border()));
        lease.Dispose();
        window.Close();
    }

    [AvaloniaFact]
    public void ViewLocator隔离Managed正文并为未知Dockable返回占位视图()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddScoped<WelcomeViewModel>();
        services.AddDocumentScopeManagement();
        using var provider = services.BuildServiceProvider();
        var registration = new HostWorkspaceDocumentRegistration(
            new MyAvaloniaManagement.PluginSdk.UI.DocumentDescriptor(
                HostExtensionIds.WelcomeDocument,
                "欢迎",
                "欢迎",
                "帮助"),
            typeof(WelcomeViewModel),
            typeof(WelcomeView),
            static () => new WelcomeView(),
            () => provider.GetRequiredService<DocumentScopeManager>()
                .CreateDocument(typeof(WelcomeViewModel)),
            static (model, activation, token) =>
                ((WelcomeViewModel)model).InitializeHost(activation, token));
        var hostCatalog = new HostWorkspaceCatalog([registration], []);
        var locator = new ViewLocator(UiWorkspaceCatalogFactory.Create(
            new PluginRegistry([], []),
            hostCatalog));
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var lease = manager.CreateDocument(typeof(WelcomeViewModel));
        var model = Assert.IsType<WelcomeViewModel>(lease.Model);
        using var adapter = new MyAvaloniaManagement.Business.Docking.ManagedDocumentDockable(
            new ActivatedWorkspaceDocument(registration, lease),
            "欢迎");
        var prepared = locator.Prepare(adapter);
        var firstManagedFallback = locator.Build(adapter);
        var secondManagedFallback = locator.Build(adapter);
        var fallback = locator.Build(new Dock.Model.Mvvm.Controls.Tool
        {
            Title = "未知工具"
        });

        Assert.IsType<WelcomeView>(prepared);
        Assert.IsType<Border>(firstManagedFallback);
        Assert.IsType<Border>(secondManagedFallback);
        Assert.NotSame(firstManagedFallback, secondManagedFallback);
        Assert.False(firstManagedFallback!.IsVisible);
        Assert.False(secondManagedFallback!.IsVisible);
        Assert.Null(firstManagedFallback.DataContext);
        Assert.Null(secondManagedFallback.DataContext);
        Assert.IsType<TextBlock>(fallback);
        Assert.True(locator.Match(adapter));
        Assert.Same(model, prepared.DataContext);
        Assert.False(locator.Match(new object()));
        Assert.Null(locator.Build(null));
        Assert.Throws<InvalidOperationException>(() => locator.Build(new object()));
    }

    [AvaloniaFact]
    public void 全局主题命令切换应用变体且菜单保持单选()
    {
        using var context = new UiTestContext();
        var application = Assert.IsType<App>(Application.Current);
        context.ThemeService.Initialize(application);
        var menu = new MenuView
        {
            DataContext = context.ViewModel
        };
        var host = new Window { Content = menu };
        host.Show();

        try
        {
            context.ViewModel.SetThemeCommand.Execute("Dark");

            Assert.Equal(ThemeVariant.Dark, application.RequestedThemeVariant);
            Assert.True(context.ViewModel.IsDarkTheme);
            var themeItems = menu.GetLogicalDescendants()
                .OfType<MenuItem>()
                .Where(item => item.GroupName == "ApplicationTheme")
                .ToArray();
            Assert.Equal(3, themeItems.Length);
            Assert.Single(themeItems, item => item.IsChecked);
            Assert.Equal(
                "深色",
                themeItems.Single(item => item.IsChecked).Header);

            context.ViewModel.SetThemeCommand.Execute("System");
            Assert.Equal(
                ThemeVariant.Default,
                application.RequestedThemeVariant);
            Assert.True(context.ViewModel.IsSystemTheme);
        }
        finally
        {
            context.ThemeService.SetMode(
                MyAvaloniaManagement.Business.Appearance
                    .ApplicationThemeMode.System);
            host.Close();
        }
    }

    [AvaloniaFact]
    public void 浅色和深色语义资源解析为不同画刷()
    {
        var application = Assert.IsType<App>(Application.Current);

        Assert.True(application.TryGetResource(
            "AppPanelBrush",
            ThemeVariant.Light,
            out var lightValue));
        Assert.True(application.TryGetResource(
            "AppPanelBrush",
            ThemeVariant.Dark,
            out var darkValue));

        var light = Assert.IsType<SolidColorBrush>(lightValue);
        var dark = Assert.IsType<SolidColorBrush>(darkValue);
        Assert.NotEqual(light.Color, dark.Color);
    }

    [AvaloniaFact]
    public void 代表性插件视图在浅色和深色主题下均可加载()
    {
        using var context = new UiTestContext();
        var application = Assert.IsType<App>(Application.Current);
        context.ThemeService.Initialize(application);
        var views = new Control[]
        {
            new BiliDownloader.Views.BiliDownloaderView(),
            new MyPlugTest.Views.MyCustomToolView(),
            new MySmallTools.Views.SecretVideoPlayer.Playback
                .PlaybackDeploymentView()
        };
        var panel = new StackPanel();
        foreach (var view in views)
        {
            panel.Children.Add(view);
        }

        var host = new Window { Content = panel };
        host.Show();

        try
        {
            context.ThemeService.SetMode(
                MyAvaloniaManagement.Business.Appearance
                    .ApplicationThemeMode.Light);
            Assert.All(views, view =>
                Assert.Equal(ThemeVariant.Light, view.ActualThemeVariant));

            context.ThemeService.SetMode(
                MyAvaloniaManagement.Business.Appearance
                    .ApplicationThemeMode.Dark);
            Assert.All(views, view =>
                Assert.Equal(ThemeVariant.Dark, view.ActualThemeVariant));
        }
        finally
        {
            context.ThemeService.SetMode(
                MyAvaloniaManagement.Business.Appearance
                    .ApplicationThemeMode.System);
            host.Close();
        }
    }
}
