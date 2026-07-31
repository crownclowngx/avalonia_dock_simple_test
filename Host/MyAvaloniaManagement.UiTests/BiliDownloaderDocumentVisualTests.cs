using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using BiliDownloader.Converters;
using BiliDownloader.Views;
using BiliDownloader.Views.BiliDownloader;
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

                Assert.Equal(760, view.Bounds.Width);
                Assert.Equal(620, view.Bounds.Height);
                Assert.NotEmpty(view.GetLogicalDescendants().OfType<PathIcon>());
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
        Assert.IsType<RenameDisplayConverter>(view.Resources["RenameDisplayConverter"]);
    }

    [AvaloniaFact]
    public void 运行日志默认展开且文档样式不影响调度工具()
    {
        using var context = new UiTestContext();
        var document = new BiliDownloaderView();
        var schedulerTool = new BiliSchedulerToolView();

        var log = document.FindControl<Expander>("DownloadLogExpander");
        Assert.NotNull(log);
        Assert.True(log.IsExpanded);

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
