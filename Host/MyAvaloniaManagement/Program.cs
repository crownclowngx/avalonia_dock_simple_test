using System;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        HostStartupFailureContext.Clear();
        using var diagnostics = HostDiagnosticSession.Start();
        HostRuntime? runtime = null;
        try
        {
            runtime = HostRuntime.Create(diagnostics);

            // public 无参 ViewModel 和外部测试宿主仍依赖历史服务定位器；
            // 实际容器所有权归 HostRuntime，避免兼容入口重新承担组合根职责。
            Business.Helpers.ServiceProvider.Initialize(runtime.Services);
            runtime.InitializePlugins();
        }
        catch (Exception exception)
        {
            // 已由阶段边界记录的失败不再包装为未知错误；真正逃逸的异常才使用兜底错误码。
            if (!diagnostics.Snapshot.Any(item =>
                    item.Disposition == HostDiagnosticDisposition.AbortStartup))
            {
                diagnostics.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.HostStartupUnexpected,
                    HostDiagnosticPhase.HostBootstrap,
                    "宿主启动发生未分类异常，主工作台没有启动。")
                {
                    Exception = exception,
                });
            }

            try
            {
                runtime?.Dispose();
            }
            catch (Exception cleanupException)
            {
                diagnostics.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.HostStartupCleanupFailed,
                    HostDiagnosticPhase.HostBootstrap,
                    "启动失败后的资源清理发生异常，应用仍将显示已有诊断并退出。")
                {
                    Exception = cleanupException,
                });
            }
            HostStartupFailureContext.Set(diagnostics.Snapshot, diagnostics.LogPath);
            return BuildStartupFailureApp()
                .StartWithClassicDesktopLifetime(args);
        }

        try
        {
            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            runtime?.Dispose();
        }
    }

    /// <summary>
    /// 在 Avalonia 消息循环结束后反向关闭插件。
    /// 保留该入口是为了兼容既有测试，同时由 HostRuntime 控制调用时机。
    /// </summary>
    internal static void ShutdownPlugins(PluginLifecycleManager lifecycleManager)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        lifecycleManager.ShutdownAllAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 创建生产启动与兼容测试宿主共用的 Avalonia 应用构建器。
    /// 维持单一构建路径可防止两种启动方式产生不同的平台配置。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// 创建不包含 ViewLocator、Dock 和插件资源的最小错误应用。
    /// </summary>
    internal static AppBuilder BuildStartupFailureApp() =>
        AppBuilder.Configure<StartupFailureApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
