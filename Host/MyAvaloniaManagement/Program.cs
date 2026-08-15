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
            return runtime!.BuildAvaloniaApp()
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
    /// 创建不包含 ViewLocator、Dock 和插件资源的最小错误应用。
    /// </summary>
    internal static AppBuilder BuildStartupFailureApp() =>
        AppBuilder.Configure<StartupFailureApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
