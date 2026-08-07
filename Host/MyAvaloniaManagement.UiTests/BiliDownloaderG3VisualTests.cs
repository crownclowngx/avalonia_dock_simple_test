using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.ViewModels.BiliDownloader;
using BiliDownloader.Views.BiliDownloader;
using Xunit;
using ContentPageModel = BiliDownloader.Models.ContentSources.ContentPage;

namespace MyAvaloniaManagement.UiTests;

public sealed class BiliDownloaderG3VisualTests
{
    [AvaloniaFact]
    public async Task 千项来源首屏只实现可视容器且选择提示可见()
    {
        using var context = new UiTestContext();
        var browser = await CreateBrowserAsync(1000);
        var view = new ContentSourceBrowserView { DataContext = browser };
        var window = new Window { Width = 760, Height = 700, Content = view };
        try
        {
            window.Show();
            Measure(view, new Size(760, 700));
            browser.SelectLoadedCommand.Execute(null);
            Measure(view, new Size(760, 700));

            var list = Assert.IsType<ListBox>(view.FindControl<ListBox>("ContentSourceItemsList"));
            var realizedRows = list.GetVisualDescendants().OfType<ListBoxItem>().Count();

            Assert.Equal(ContentPageRequest.DefaultPageSize, browser.Items.Count);
            Assert.InRange(realizedRows, 1, browser.Items.Count - 1);
            Assert.True(view.FindControl<Border>("AllMatchingPrompt")!.IsVisible);
            Assert.Equal(true, browser.LoadedSelectionState);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task 筛选选择区域在三种宽度和双主题下不产生横向溢出()
    {
        using var context = new UiTestContext();
        var application = Assert.IsType<App>(Application.Current);
        var originalTheme = application.RequestedThemeVariant;
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var size in new[] { new Size(1240, 760), new Size(760, 700), new Size(520, 700) })
            {
                application.RequestedThemeVariant = theme;
                var browser = await CreateBrowserAsync(100);
                var view = new ContentSourceBrowserView { DataContext = browser };
                var window = new Window { Width = size.Width, Height = size.Height, Content = view };
                try
                {
                    window.Show();
                    Measure(view, size);
                    foreach (var controlName in new[]
                             {
                                 "SearchFilterBox", "PublishedFromPicker", "PublishedToPicker",
                                 "TypeFilterButton", "SortFilterComboBox",
                             })
                    {
                        var control = view.FindControl<Control>(controlName)!;
                        var leftTop = control.TranslatePoint(default, view);
                        var rightBottom = control.TranslatePoint(
                            new Point(control.Bounds.Width, control.Bounds.Height), view);
                        Assert.NotNull(leftTop);
                        Assert.NotNull(rightBottom);
                        Assert.True(leftTop.Value.X >= -0.5 && rightBottom.Value.X <= view.Bounds.Width + 0.5,
                            $"{controlName} 在 {size.Width}px 宽度下发生横向溢出。"
                            + $"LeftTop={leftTop}, RightBottom={rightBottom}, View={view.Bounds}");
                    }

                    var fromPicker = view.FindControl<CalendarDatePicker>("PublishedFromPicker")!;
                    var toPicker = view.FindControl<CalendarDatePicker>("PublishedToPicker")!;
                    Assert.True(fromPicker.Bounds.Width >= 165,
                        $"开始日期在 {size.Width}px 宽度下被压缩：{fromPicker.Bounds}");
                    Assert.True(toPicker.Bounds.Width >= 165,
                        $"结束日期在 {size.Width}px 宽度下被压缩：{toPicker.Bounds}");

                    var filterPanel = view.FindControl<Border>("FilterPanel")!;
                    var list = view.FindControl<ListBox>("ContentSourceItemsList")!;
                    var filterBottom = filterPanel.TranslatePoint(
                        new Point(0, filterPanel.Bounds.Height), view)!.Value.Y;
                    var listTop = list.TranslatePoint(default, view)!.Value.Y;
                    Assert.True(filterBottom <= listTop + 0.5,
                        $"筛选提示与列表在 {size.Width}px 宽度下发生重叠。"
                        + $"FilterBottom={filterBottom}, ListTop={listTop}");

                    Assert.Equal(260, list.Height);
                    Assert.True(view.FindControl<Grid>("SelectionScopeBar")!.IsVisible);
                }
                finally
                {
                    window.Close();
                }
            }
        }
        finally
        {
            application.RequestedThemeVariant = originalTheme;
        }
    }

    private static async Task<ContentSourceBrowserViewModel> CreateBrowserAsync(int total)
    {
        var provider = new LargeFixtureProvider(total);
        var browser = new ContentSourceBrowserViewModel(
            new ContentSourceProviderRegistry([provider]),
            new VideoParseResultFactory(new NoProbe(), new NoCredentials()),
            _ => { });
        await browser.OpenAsync(await provider.NormalizeAsync("1", CancellationToken.None));
        return browser;
    }

    private static void Measure(Control control, Size size)
    {
        control.Measure(size);
        control.Arrange(new Rect(size));
    }

    private sealed class LargeFixtureProvider(int total) :
        IContentSourceProvider,
        IContentSourceResolutionProvider
    {
        public ContentSourceKind Kind => ContentSourceKind.Uploader;
        public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPaging;
        public int CapabilityVersion => 1;

        public ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ContentSourceDescriptor(Kind, $"uploader:{total}", $"{total} 项来源", null, 1));

        public Task<ContentPageModel> GetPageAsync(
            ContentSourceDescriptor descriptor,
            ContentPageRequest request,
            CancellationToken cancellationToken)
        {
            var offset = request.ContinuationToken is null ? 0 : int.Parse(request.ContinuationToken);
            var count = Math.Min(request.PageSize, total - offset);
            var items = Enumerable.Range(offset + 1, Math.Max(0, count))
                .Select(index => new ContentSourceItem(
                    new ContentItemKey(Kind, $"aid:{index}"),
                    $"视频 {index}",
                    ContentSourceItemType.Video,
                    "测试作者",
                    DateTimeOffset.UnixEpoch.AddDays(index),
                    aid: index,
                    bvid: $"BV{index:0000000000}"))
                .ToArray();
            var next = offset + items.Length;
            var hasMore = next < total;
            return Task.FromResult(new ContentPageModel(items, hasMore ? next.ToString() : null, hasMore));
        }

        public Task<BiliVideoCollection> ResolveItemAsync(
            ContentSourceDescriptor descriptor,
            ContentSourceItem item,
            CancellationToken cancellationToken) => Task.FromResult(new BiliVideoCollection());
    }

    private sealed class NoProbe : IBiliMediaProbe
    {
        public Task<BiliDashResult> GetDashResultAsync(
            long aid, long cid, int qualityId, string cookie,
            BiliMediaType mediaType = BiliMediaType.Video,
            long epId = 0, long seasonId = 0,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoCredentials : IBiliCredentialProvider
    {
        public string GetCookieHeader() => string.Empty;
        public bool IsLoggedIn => false;
    }
}
