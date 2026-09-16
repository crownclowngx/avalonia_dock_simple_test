using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Compatibility;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.ViewModels.PluginStatus;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>显式读取真实 ZIP 的已解压副本与工具报告，贯通生产查询、原子导入和真实窗口。</summary>
public sealed class ExternalPluginDashboardAcceptanceTests
{
    [AvaloniaFact]
    public async Task 真实产物报告导入看板并准确展示历史和当前证据()
    {
        var root = Environment.GetEnvironmentVariable("MYAVALONIA_EXTERNAL_CONTROLS")
            ?? throw new InvalidOperationException("需要真实产物 Controls 副本。");
        var path = Environment.GetEnvironmentVariable("MYAVALONIA_COMPATIBILITY_REPORT")
            ?? throw new InvalidOperationException("需要工具实际生成的报告。");
        var report = await CompatibilityReportStore.ReadFileAsync(path, default);
        var modules = PluginModuleCatalog.Discover(AssemblyLoaderHelper.Discover(root));
        using var context = new UiTestContext(modules: modules);
        var owner = new Window(); owner.Show();
        var windows = context.Provider.GetRequiredService<PluginStatusWindowService>();
        windows.Attach(owner); windows.ShowOrActivate();
        try
        {
            var window = windows.CurrentWindow!;
            var model = (PluginStatusWindowViewModel)window.DataContext!;
            model.SelectedItem = model.VisibleItems.Single(item => item.PluginId == report.PluginId);
            await model.ImportReportAsync(path);
            Assert.False(model.HasError, model.ErrorMessage);
            Assert.All(model.EvidenceRows, row => Assert.Contains("待检查", row.MatchText));
            await model.CheckArtifactsAsync();
            Assert.False(model.HasError, model.ErrorMessage);
            Assert.All(model.EvidenceRows, row => Assert.Equal("匹配当前产物与环境", row.MatchText));
            Assert.Contains(model.EvidenceRows, row => row.LevelText == "业务与真机" && row.ResultText == "未执行");
            Assert.Contains("Build", model.BuildText);
            Assert.Single(model.MatrixColumns);
            await model.ImportReportAsync(path);
            Assert.Equal(4, model.EvidenceRows.Count);
            // 人工构造的“其他环境”只用于验证 UI，不作为真实验收报告对外交付。
            var historical = report with { ReportId = Guid.NewGuid(), Host = report.Host with { Framework = ".NET 测试历史环境" } };
            var historyPath = Path.Combine(context.TempDirectory, "history.json");
            await File.WriteAllTextAsync(historyPath, CompatibilityReportJson.Serialize(historical));
            await model.ImportReportAsync(historyPath);
            await model.CheckArtifactsAsync();
            Assert.Single(model.MatrixColumns);
            model.ShowHistory = true;
            Assert.Equal(2, model.MatrixColumns.Count);
            Assert.Equal(CompatibilityDashboardProjection.HostKey(report.Host), model.MatrixColumns[0].Key);
            Assert.Contains(model.EvidenceRows, row => row.MatchText == "其他运行环境");
            var tabs = window.FindControl<TabControl>("DashboardTabs")!;
            tabs.SelectedIndex = 1;
            var filter = window.FindControl<ComboBox>("EvidenceFilterBox")!;
            filter.SelectedItem = "存在失败";
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Empty(model.MatrixRows);
            filter.SelectedItem = "有未执行";
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Single(model.MatrixRows);
            filter.SelectedItem = "全部证据";
            await Render(window, "plugin-dashboard-matrix-light");
            window.RequestedThemeVariant = ThemeVariant.Dark;
            window.Width = 680; window.Height = 520;
            await Render(window, "plugin-dashboard-matrix-compact-dark");
            var scroll = window.FindControl<ScrollViewer>("CompatibilityMatrixScroll")!;
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
            scroll.Offset = new Vector(300, 0);
            await Render(window, "plugin-dashboard-matrix-scroll");
            tabs.SelectedIndex = 0;
            window.Width = 1200; window.Height = 800;
            await Render(window, "plugin-dashboard-real-overview");
        }
        finally { windows.Dispose(); owner.Close(); }
    }

    private static async Task Render(Window window, string name)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(30);
        if (Environment.GetEnvironmentVariable("MYAVALONIA_PLUGIN_STATUS_RENDER_DIRECTORY") is not { Length: > 0 } path) return;
        Directory.CreateDirectory(path);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(path, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
