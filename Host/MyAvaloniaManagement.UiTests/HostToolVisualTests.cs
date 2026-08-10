using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using MyAvaloniaManagement.Business.Converter;
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
}
