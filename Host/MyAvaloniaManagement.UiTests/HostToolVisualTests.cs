using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Converter;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagement.Views.Tools;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

public sealed class HostToolVisualTests
{
    [AvaloniaFact]
    public void 文件和目录使用不同的缓存矢量图标()
    {
        var converter = FileSystemIconConverter.Instance;
        var folder = Assert.IsType<StreamGeometry>(converter.Convert(
            true,
            typeof(Geometry),
            null,
            CultureInfo.InvariantCulture));
        var file = Assert.IsType<StreamGeometry>(converter.Convert(
            false,
            typeof(Geometry),
            null,
            CultureInfo.InvariantCulture));

        Assert.NotSame(folder, file);
        Assert.Same(folder, converter.Convert(
            true,
            typeof(Geometry),
            null,
            CultureInfo.InvariantCulture));
        Assert.Same(file, converter.Convert(
            null,
            typeof(Geometry),
            null,
            CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void 四个宿主工具在窄面板中可布局并使用统一样式()
    {
        using var context = new UiTestContext();
        var views = new UserControl[]
        {
            new FileSystemTreeView(),
            new PlugGroupMenuView(),
            new ToolManagementView(),
            new PluginStatusView()
        };

        foreach (var view in views)
        {
            view.Measure(new Size(240, 420));
            view.Arrange(new Rect(0, 0, 240, 420));

            Assert.Contains("host-tool-surface", view.Classes);
            Assert.True(view.Bounds.Width <= 240);
        }

        var fileSystemView = Assert.IsType<FileSystemTreeView>(views[0]);
        var pathText = fileSystemView.FindControl<TextBlock>(
            "SelectedFolderPathText");
        Assert.NotNull(pathText);
        Assert.Equal(TextTrimming.CharacterEllipsis, pathText.TextTrimming);
        Assert.NotEmpty(fileSystemView.GetLogicalDescendants()
            .OfType<PathIcon>());

        Assert.IsNotType<ListBox>(views[2].Content);
        Assert.NotNull(views[2].FindControl<ItemsControl>("ToolItemsControl"));
        Assert.NotNull(views[3].FindControl<ItemsControl>("PluginStatusItemsControl"));
    }

    [AvaloniaFact]
    public void 工具管理复选框点击与Dock隐藏集合保持一致()
    {
        using var context = new UiTestContext();
        var managerAdapter = Assert.IsType<ManagedToolDockable>(
            context.Factory.CreatedTools[HostExtensionIds.V2ToolManagement.Value]);
        var manager = Assert.IsType<ToolManagementViewModel>(managerAdapter.Model);
        var closableItem = manager.ToolItems.Single(item =>
            item.ToolId == HostExtensionIds.V2PluginStatus.Value);
        var closableTool = context.Factory.CreatedTools[closableItem.ToolId];
        var owningRoot = context.Factory.FindRoot(closableTool, _ => true)!;
        var fixedItem = manager.ToolItems.Single(item =>
            item.ToolId == HostExtensionIds.V2FileSystemTree.Value);
        var view = new ToolManagementView
        {
            DataContext = manager
        };
        var window = new Window
        {
            Width = 320,
            Height = 500,
            Content = view
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var closableCheckBox = FindCheckBox(view, closableItem);
            var fixedCheckBox = FindCheckBox(view, fixedItem);
            Assert.True(closableCheckBox.IsChecked);
            Assert.True(closableItem.IsVisible);
            Assert.False(fixedCheckBox.IsEnabled);
            Assert.True(fixedCheckBox.IsChecked);

            Click(window, closableCheckBox);

            Assert.False(closableCheckBox.IsChecked);
            Assert.False(closableItem.IsVisible);
            Assert.Contains(closableTool, owningRoot.HiddenDockables ?? []);
            Assert.Null(DockTreeNavigator.FindToolDock(owningRoot, closableTool));

            Click(window, closableCheckBox);

            Assert.True(closableCheckBox.IsChecked);
            Assert.True(closableItem.IsVisible);
            Assert.DoesNotContain(closableTool, owningRoot.HiddenDockables ?? []);
            Assert.NotNull(DockTreeNavigator.FindToolDock(owningRoot, closableTool));

            Click(window, fixedCheckBox);

            Assert.True(fixedCheckBox.IsChecked);
            Assert.True(fixedItem.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void 浅色和深色主题均提供VsCodeDock与工具选中画刷()
    {
        var application = Assert.IsType<App>(Application.Current);

        AssertThemeBrush(application, ThemeVariant.Light);
        AssertThemeBrush(application, ThemeVariant.Dark);

        Assert.True(application.TryGetResource(
            "AppToolSelectedBrush",
            ThemeVariant.Light,
            out var lightSelection));
        Assert.True(application.TryGetResource(
            "AppToolSelectedBrush",
            ThemeVariant.Dark,
            out var darkSelection));
        Assert.NotEqual(
            Assert.IsType<SolidColorBrush>(lightSelection).Color,
            Assert.IsType<SolidColorBrush>(darkSelection).Color);
    }

    private static void AssertThemeBrush(
        Application application,
        ThemeVariant themeVariant)
    {
        Assert.True(application.TryGetResource(
            "DockSurfaceHeaderActiveBrush",
            themeVariant,
            out var value));
        Assert.IsType<SolidColorBrush>(value);
    }

    private static CheckBox FindCheckBox(
        ToolManagementView view,
        ToolManagementItem item) =>
        view.GetLogicalDescendants()
            .OfType<CheckBox>()
            .Single(checkBox => ReferenceEquals(checkBox.DataContext, item));

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            window) ?? throw new InvalidOperationException("无法定位工具管理复选框。");
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }
}
