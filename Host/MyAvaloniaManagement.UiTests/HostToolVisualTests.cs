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
using Microsoft.Extensions.DependencyInjection;
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
    public void 两个宿主工具在窄面板中可布局并使用统一样式()
    {
        using var context = new UiTestContext();
        var views = new UserControl[]
        {
            new FileSystemTreeView(),
            new PlugGroupMenuView()
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

    }

    [AvaloniaFact]
    public void 工具隐藏恢复继续由正文回收器复用唯一PreparedView()
    {
        using var context = new UiTestContext();
        var tool = Assert.IsType<ManagedToolDockable>(
            context.Workspace.CreatedTools[HostExtensionIds.PluginMenu.Value]);
        var prepared = Assert.IsAssignableFrom<Control>(tool.PreparedView);
        var recycling = context.Provider.GetRequiredService<DocumentControlRecycling>();
        Assert.True(context.Workspace.ShowTool(HostExtensionIds.PluginMenu));

        Assert.Same(prepared, recycling.Build(tool, null, null));
        Assert.True(context.Workspace.TrySetToolVisibility(tool.Id, false));
        Assert.Same(prepared, tool.PreparedView);
        Assert.True(context.Workspace.TrySetToolVisibility(tool.Id, true));
        Assert.Same(prepared, recycling.Build(tool, null, null));
        Assert.Same(prepared, tool.PreparedView);
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
