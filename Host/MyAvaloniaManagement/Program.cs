using System;
using System.Linq;
using System.Text;
using Avalonia;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;

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
        }
        catch (Exception exception)
        {
            // 已由阶段边界记录的失败不再包装为未知错误；真正逃逸的异常才使用兜底错误码。
            if (!diagnostics.Snapshot.Any(item =>
                    item.Disposition == HostDiagnosticDisposition.AbortStartup))
            {
                diagnostics.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.HostStartupUnexpected,
                    HostDiagnosticPhase.HostBootstrap)
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
                    HostDiagnosticPhase.HostBootstrap)
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
    /// 创建不包含 ViewLocator、Dock 和插件资源的最小错误应用。
    /// </summary>
    internal static AppBuilder BuildStartupFailureApp() =>
        AppBuilder.Configure<StartupFailureApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
