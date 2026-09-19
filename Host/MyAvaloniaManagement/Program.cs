using System;
using System.Linq;
using System.Text;
using Avalonia;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Restart;

namespace MyAvaloniaManagement;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args) => Run(args,
        (runtime, arguments) => runtime.BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments),
        RestartFailureApp.Show);

    /// <summary>唯一进程启动与收尾链；桌面适配委托使测试可使用 Headless，生产仍走相同 Runtime 与助手协议。</summary>
    internal static int Run(string[] args, Func<HostRuntime, string[], int> runDesktop, Func<Exception, int> showRestartFailure)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // 助手分流必须早于日志、数据根锁、插件发现以及 Avalonia 初始化。
        if (RestartHelperRunner.IsHelper(args))
        {
            try { RestartHelperRunner.RunAsync(args).GetAwaiter().GetResult(); return 0; }
            catch (Exception exception) { return showRestartFailure(exception); }
        }
        using var restart = new RestartHandoffSession(() => HostLaunchSpecification.Capture(args));
        HostStartupFailureContext.Clear();
        using var diagnostics = HostDiagnosticSession.Start();
        HostRuntime? runtime = null;
        try
        {
            runtime = HostRuntime.Create(diagnostics, restart);
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

        var exitCode = 1;
        try { exitCode = runDesktop(runtime!, args); }
        catch (Exception exception)
        { Console.Error.WriteLine($"Host errorCode=HOST_DESKTOP_FAILED type={exception.GetType().Name}"); }
        try
        {
            var result = runtime!.Shutdown();
            if (result.ResourcesRetained || result.Failures.Count != 0) exitCode = 1;
        }
        catch (Exception exception)
        { exitCode = 1; Console.Error.WriteLine($"Host errorCode=HOST_SHUTDOWN_FAILED type={exception.GetType().Name}"); }
        // 日志释放可能把 I/O 错误转为诊断，因此同时检查新增记录，不能只看 Dispose 是否抛出。
        var diagnosticCount = diagnostics.Snapshot.Count;
        diagnostics.Dispose();
        if (diagnostics.Snapshot.Count != diagnosticCount) exitCode = 1;
        try { restart.CompleteAsync(exitCode == 0).GetAwaiter().GetResult(); }
        catch (Exception exception)
        { exitCode = 1; Console.Error.WriteLine($"Host errorCode=HOST_RESTART_HANDOFF_FAILED type={exception.GetType().Name}"); }
        return exitCode;
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
