using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.Views;

namespace MyAvaloniaManagement.Business.Presentation;

/// <summary>
/// 定义 Avalonia 应用资源加载完成后，宿主桌面工作区需要执行的最小启动动作。
/// </summary>
/// <remarks>
/// 设计意图：<see cref="App"/> 只适配 Avalonia 生命周期，不负责解析依赖或了解主窗口的
/// 组合细节。生产 Shell 由 <c>HostRuntime</c> 的根容器构造；Headless 测试则可注入无副作用
/// 实现。该接口是 Host 内部端口，不属于 Plugin SDK。
/// </remarks>
internal interface IHostDesktopShell
{
    /// <summary>把已构造的宿主工作区附加到经典桌面生命周期。</summary>
    void Attach(App application, IClassicDesktopStyleApplicationLifetime desktop);
}

/// <summary>
/// 生产桌面 Shell，集中初始化主题、创建主窗口并绑定唯一的主窗口 ViewModel。
/// </summary>
/// <remarks>
/// 构造函数只声明实际依赖，不接收 <see cref="IServiceProvider"/>。因此窗口创建失败会在
/// Host 容器验证或启动边界暴露，也不会从进程全局状态取得另一个 Runtime 的对象。
/// </remarks>
internal sealed class HostDesktopShell(
    ApplicationThemeService themeService,
    MainWindowViewModel mainWindowViewModel) : IHostDesktopShell
{
    public void Attach(
        App application,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(desktop);

        themeService.Initialize(application);
        var mainWindow = new MainWindow
        {
            DataContext = mainWindowViewModel,
        };
        desktop.MainWindow = mainWindow;

        if (string.Equals(
                Environment.GetEnvironmentVariable("MYAVALONIA_SMOKE_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            // Smoke 仍走真实 Opened/Closing 路径。关闭动作属于桌面 Shell 的启动政策，
            // 不应泄漏到通用 App 生命周期适配器或 ViewModel 中。
            mainWindow.Opened += (_, _) => Dispatcher.UIThread.Post(
                mainWindow.Close,
                DispatcherPriority.Background);
        }
    }
}
