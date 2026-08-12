using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Views;

namespace MyAvaloniaManagement;

/// <summary>
/// 只承载启动失败窗口的最小 Avalonia 应用。
/// </summary>
/// <remarks>
/// 设计意图：独立于生产 App.axaml，避免加载 ViewLocator、Dock 主题和插件视图资源，
/// 从结构上保证错误展示不会再次进入已经失败的插件发现与主工作台组合路径。
/// </remarks>
internal sealed partial class StartupFailureApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            HostStartupFailureContext.Current is { } context)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new StartupFailureWindow(context);
            window.Closed += (_, _) => desktop.Shutdown(1);
            desktop.MainWindow = window;

            if (string.Equals(
                    Environment.GetEnvironmentVariable("MYAVALONIA_SMOKE_TEST"),
                    "1",
                    StringComparison.Ordinal))
            {
                // 自动化失败场景同样创建真实窗口，再从 Dispatcher 正常关闭并验证退出码 1。
                window.Opened += (_, _) => Dispatcher.UIThread.Post(
                    window.Close,
                    DispatcherPriority.Background);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
