using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.Views;

namespace MyAvaloniaManagement;

/// <summary>
/// Avalonia 应用入口，负责加载生产资源并创建桌面主窗口。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 加载 App.axaml 中声明的主题和全局资源。
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
    /// <summary>
    /// 在 Avalonia 框架初始化完成后解析主 ViewModel 并建立主窗口。
    /// </summary>
    /// <remarks>
    /// 当设置 <c>MYAVALONIA_SMOKE_TEST=1</c> 时，窗口仍会真实打开，
    /// 但在 Opened 后通过 Dispatcher 正常关闭，从而验证 Closing、布局保存和应用退出，
    /// 同时避免测试进程依赖外部自动化强制终止。
    /// </remarks>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ServiceProvider.GetRequiredService<ApplicationThemeService>()
                .Initialize(this);

            var mainWindow = new MainWindow
            {
                DataContext = ServiceProvider.GetRequiredService<MainWindowViewModel>(),
            };
            desktop.MainWindow = mainWindow;

            if (string.Equals(
                Environment.GetEnvironmentVariable(
                    "MYAVALONIA_SMOKE_TEST"),
                "1",
                StringComparison.Ordinal))
            {
                // 冒烟模式仍会完整创建并打开真实窗口；在 Opened 之后排队关闭，
                // 让 Closing、布局保存和插件反向关闭走生产生命周期。
                mainWindow.Opened += (_, _) =>
                    Dispatcher.UIThread.Post(
                        mainWindow.Close,
                        DispatcherPriority.Background);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
