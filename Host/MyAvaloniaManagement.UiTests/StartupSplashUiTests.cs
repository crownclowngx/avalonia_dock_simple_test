using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Startup;
using MyAvaloniaManagement.ViewModels.Startup;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>用真实 XAML 和软件渲染核对羽毛首屏、长标识、主题及关闭意图，不把 Headless 当作原生动画验收。</summary>
public sealed class StartupSplashUiTests
{
    [AvaloniaFact]
    public async Task V19工作台执行绑定在UI线程且后台附接先于服务解析被拒绝()
    {
        var creations = 0;
        using (var context = new UiTestContext(configureContributions: (services, _) =>
        {
            var factory = services.Last(item => item.ServiceType == typeof(HostWorkbenchCommandBindings)).ImplementationFactory!;
            services.AddSingleton(provider =>
            {
                Assert.True(Dispatcher.UIThread.CheckAccess());
                creations++;
                return (HostWorkbenchCommandBindings)factory(provider);
            });
        }))
        {
            Assert.Equal(1, creations);
            Assert.Same(context.Provider.GetRequiredService<HostWorkbenchCommandBindings>(),
                context.Provider.GetRequiredService<HostWorkbenchCommandBindings>());
        }
        var provider = new ServiceCollection().BuildServiceProvider();
        using var runtime = new HostRuntime(provider, new HostRuntimeShutdown(new EmptyOwner(), provider,
            () => { }, new HostShutdownParticipants(), new HostResourceRetention(), null));
        // 参数刻意不提供；若线程检查被移到服务/资源解析之后，测试将收到其他类型的失败。
        var error = await Task.Run(() => Record.Exception(() => runtime.AttachWorkbench(null!, null!)));
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("thread", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class EmptyOwner : IDisposable { public void Dispose() { } }

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
                var directory = MyAvaloniaManagement.Testing.TestEvidenceDirectory.Create("startup-splash");
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
