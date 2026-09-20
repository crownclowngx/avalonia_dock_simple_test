using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MyAvaloniaManagement.Business.Startup;
using MyAvaloniaManagement.ViewModels.Startup;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>用真实 XAML 和软件渲染核对羽毛首屏、长标识、主题及关闭意图，不把 Headless 当作原生动画验收。</summary>
public sealed class StartupSplashUiTests
{
    [AvaloniaFact]
    public async Task 浅深主题与真实进度绑定_保存羽毛首屏截图()
    {
        var model = new SplashViewModel();
        var buffer = new StartupProgressBuffer();
        buffer.Report(new(StartupStage.Initializing, "myavalonia.plugin.example-a", 3, 12, StartupOutcome.Succeeded));
        buffer.Report(new(StartupStage.Initializing, "myavalonia.plugin.example-b", 4, 12, StartupOutcome.Failed));
        buffer.Report(new(StartupStage.Initializing, "myavalonia.plugin.a-very-long-plugin-name-for-startup-layout-validation", 4, 12));
        model.Apply(buffer.Snapshot);
        var window = new SplashWindow(model, animate: false);
        try
        {
            window.Show();
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var bar = window.GetVisualDescendants().OfType<ProgressBar>().Single();
                Assert.False(bar.IsIndeterminate);
                Assert.InRange(bar.Value, 33, 34);
                Assert.False(window.AnimationRunning);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "v15-ui");
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, theme == ThemeVariant.Light ? "splash-light.png" : "splash-dark.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.CompleteAndClose(); }
    }

    [AvaloniaFact]
    public void 用户关闭只提交取消意图_协调器允许后才真正关闭()
    {
        var model = new SplashViewModel();
        var window = new SplashWindow(model, animate: true);
        var cancelled = 0;
        window.CancellationRequested += () => { cancelled++; model.Stop(); window.StopAnimation(); };
        try
        {
            window.Show();
            Assert.True(window.AnimationRunning);
            window.Close();
            Assert.Equal(1, cancelled);
            Assert.True(window.IsVisible);
            Assert.False(window.AnimationRunning);
            Assert.Contains("停止", model.Title);
        }
        finally { window.CompleteAndClose(); }
        Assert.False(window.IsVisible);
    }
}
