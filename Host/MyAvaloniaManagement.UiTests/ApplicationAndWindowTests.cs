using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Dock.Avalonia.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.ViewModels.Hello;
using MyAvaloniaManagement.Views;
using MyAvaloniaManagement.Views.Hello;
using MyAvaloniaManagement.Views.Tools;
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
        Assert.IsType<App>(Application.Current);
        Assert.True(
            Application.Current!.Resources.ContainsKey(
                "ControlRecyclingKey"));
        Assert.NotEmpty(Application.Current.Styles);
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
    public void 主窗体内容全屏遵守所有者和内容互斥规则()
    {
        var window = new MainWindow();
        var firstOwner = new object();
        var secondOwner = new object();
        var firstContent = new Border();
        var secondContent = new Border();
        var layer = window.FindControl<Border>("ContentFullscreenLayer")!;
        var host = window.FindControl<ContentControl>("ContentFullscreenHost")!;

        Assert.True(window.TryPresent(firstContent, firstOwner));
        Assert.True(window.TryPresent(firstContent, firstOwner));
        Assert.False(window.TryPresent(secondContent, firstOwner));
        Assert.False(window.TryPresent(firstContent, secondOwner));
        Assert.True(layer.IsVisible);
        Assert.Same(firstContent, host.Content);
        Assert.False(window.TryRestore(secondOwner));
        Assert.True(window.TryRestore(firstOwner));
        Assert.False(layer.IsVisible);
        Assert.Null(host.Content);
        Assert.False(window.TryRestore(firstOwner));
    }

    [AvaloniaFact]
    public void 全屏接口拒绝空参数()
    {
        var window = new MainWindow();

        Assert.Throws<ArgumentNullException>(() =>
            window.TryPresent(null!, new object()));
        Assert.Throws<ArgumentNullException>(() =>
            window.TryPresent(new Border(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            window.TryRestore(null!));
    }

    [AvaloniaFact]
    public void ViewLocator创建已知视图并为未知Dockable返回占位视图()
    {
        var locator = new ViewLocator();
        var known = locator.Build(new WelcomeViewModel
        {
            Title = "欢迎",
            Text = "正文"
        });
        var fallback = locator.Build(new Dock.Model.Mvvm.Controls.Tool
        {
            Title = "未知工具"
        });

        Assert.IsType<WelcomeView>(known);
        Assert.IsType<TextBlock>(fallback);
        Assert.True(locator.Match(new WelcomeViewModel()));
        Assert.False(locator.Match(new object()));
        Assert.Null(locator.Build(null));
        Assert.Throws<Exception>(() => locator.Build(new object()));
    }
}
