using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using BiliDownloader.Converters;
using BiliDownloader.Views;
using BiliDownloader.Views.BiliDownloader;
using BiliDownloader.Views.BiliScheduler;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

public sealed class BiliDownloaderDocumentVisualTests
{
    [AvaloniaFact]
    public void 下载文档在宽窄尺寸与双主题下均可布局()
    {
        using var context = new UiTestContext();
        var application = Assert.IsType<App>(Application.Current);
        var originalTheme = application.RequestedThemeVariant;

        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                application.RequestedThemeVariant = theme;
                var view = new BiliDownloaderView();

                Measure(view, new Size(1240, 760));
                Measure(view, new Size(760, 620));
                Measure(view, new Size(520, 520));

                Assert.Equal(520, view.Bounds.Width);
                Assert.Equal(520, view.Bounds.Height);
                Assert.IsType<Border>(view.Content);
                Assert.NotEmpty(view.GetLogicalDescendants().OfType<PathIcon>());
                Assert.False(Assert.IsType<Expander>(
                    view.FindControl<Expander>("DownloadSettingsExpander")).IsExpanded);
            }
        }
        finally
        {
            application.RequestedThemeVariant = originalTheme;
        }
    }

    [AvaloniaFact]
    public void 下载列表保持显式虚拟化并共享无状态转换器()
    {
        using var context = new UiTestContext();
        var view = new VideoListView();
        var list = view.FindControl<ListBox>("VideoItemsList");

        Assert.NotNull(list);
        Assert.NotNull(list.ItemsPanel);
        Assert.True(double.IsPositiveInfinity(list.MaxHeight));
        Assert.IsType<RenameDisplayConverter>(view.Resources["RenameDisplayConverter"]);
    }

    [AvaloniaFact]
    public void 下载列表占据剩余高度且底部操作栏保持可见()
    {
        using var context = new UiTestContext();
        foreach (var size in new[] { new Size(760, 420), new Size(480, 320) })
        {
            var view = new VideoListView();
            var window = new Window
            {
                Width = size.Width,
                Height = size.Height,
                Content = view,
            };
            try
            {
                window.Show();
                Measure(view, size);
                var list = Assert.IsType<ListBox>(view.FindControl<ListBox>("VideoItemsList"));
                var actionBar = Assert.IsType<Border>(view.FindControl<Border>("DownloadActionBar"));
                Assert.True(list.Bounds.Height >= 64,
                    $"列表高度应至少为 64，实际为 {list.Bounds.Height}，视图高度为 {view.Bounds.Height}。");
                Assert.True(actionBar.Bounds.Height > 0);
                Assert.True(actionBar.Bounds.Bottom <= view.Bounds.Bottom,
                    $"操作栏底部 {actionBar.Bounds.Bottom} 超出视图底部 {view.Bounds.Bottom}；"
                    + $"列表高度 {list.Bounds.Height}、MinHeight {list.MinHeight}、视图期望高度 {view.DesiredSize.Height}。");
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void 任务中心在关键断点与双主题下保持响应式和虚拟化()
    {
        using var context = new UiTestContext();
        var application = Assert.IsType<App>(Application.Current);
        var originalTheme = application.RequestedThemeVariant;
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var width in new[] { 320d, 479d, 480d, 640d, 700d })
            {
                application.RequestedThemeVariant = theme;
                var view = new SchedulerTaskListView();
                Measure(view, new Size(width, 700));
                Assert.Equal(width < 480, view.Classes.Contains("compact"));
                Assert.NotNull(view.FindControl<ListBox>("TaskList")?.ItemsPanel);
            }
        }
        finally
        {
            application.RequestedThemeVariant = originalTheme;
        }
    }

    [AvaloniaFact]
    public void 运行日志默认折叠且文档样式不影响调度工具()
    {
        using var context = new UiTestContext();
        var document = new BiliDownloaderView();
        var schedulerTool = new BiliSchedulerToolView();

        var log = document.FindControl<Expander>("DownloadLogExpander");
        Assert.NotNull(log);
        Assert.False(log.IsExpanded);

        var toolControls = schedulerTool.GetLogicalDescendants()
            .OfType<Control>()
            .Append(schedulerTool);
        Assert.DoesNotContain(
            toolControls,
            control => control.Classes.Any(item => item.StartsWith("bili-doc-", StringComparison.Ordinal)));
    }

    private static void Measure(Control control, Size size)
    {
        control.Measure(size);
        control.Arrange(new Rect(size));
    }
}
