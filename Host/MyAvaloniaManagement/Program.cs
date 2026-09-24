using System;
using System.Linq;
using System.Text;
using Avalonia;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Restart;
using MyAvaloniaManagement.Business.Startup;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Installation;
using MyAvaloniaManagement.Business.Constants;

namespace MyAvaloniaManagement;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args) => Run(args,
        (builder, arguments) => builder.StartWithClassicDesktopLifetime(arguments),
        RestartFailureApp.Show);

    /// <summary>唯一进程启动与收尾链；桌面适配委托使测试可使用 Headless，生产仍走相同 Runtime 与助手协议。</summary>
    internal static int Run(string[] args, Func<AppBuilder, string[], int> runDesktop, Func<Exception, int> showRestartFailure)
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
        PluginInstallationSession? installation = null;
        using var startup = new StartupCoordinator((progress, cancellation) =>
            StartupWorker.RunAsync(async () =>
            {
                // macOS 仍沿用实验启动，不消费 Windows ZIP 安装指令。
                if (OperatingSystem.IsWindows())
                {
                    installation = new PluginInstallationSession(PluginRootDirectoryPolicy.Resolve(
                        AppContext.BaseDirectory, PluginDeploymentConstants.PluginsSubdirectory, false));
                    await installation.PrepareAsync(cancellation).ConfigureAwait(false);
                    installation.RetainRuntimeUntilProcessExit();
                }
                return await HostRuntime.CreateAsync(diagnostics, restart, progress, cancellation,
                    OperatingSystem.IsWindows() ? installation : null).ConfigureAwait(false);
            }));
        var exitCode = 1;
        try { exitCode = runDesktop(HostAvaloniaBuilder.Build(startup, diagnostics), args); }
        catch (Exception exception)
        {
            // 桌面框架未能建立时，不尝试在同进程第二次初始化 Avalonia。已建立 UI 的启动失败由 Shell 展示。
            Console.Error.WriteLine($"Host errorCode=HOST_DESKTOP_FAILED type={exception.GetType().Name}");
            startup.RequestCancellation();
        }
        // 正常关窗已等待启动任务结束；若消息循环异常退出，这里仍必须等工厂交还所有权，再关闭日志。
        // 此处消息循环已结束，同步桥接不会冻结启动动画，也不能把仍在执行的插件当作已释放。
        if (startup.State != StartupState.Ready) startup.RequestCancellation();
        startup.StartAsync().GetAwaiter().GetResult();
        if (startup.State != StartupState.Ready) exitCode = 1;
        // 先排空安装用例，再进入插件关闭。已经提交的待办留给下次启动，未提交的检查允许取消。
        try { installation?.Dispose(); }
        catch (Exception exception)
        { exitCode = 1; Console.Error.WriteLine($"Host errorCode=PLUGIN_INSTALL_SHUTDOWN_FAILED type={exception.GetType().Name}"); }
        try
        {
            var result = startup.Runtime?.Shutdown();
            if (result is { } shutdown && (shutdown.ResourcesRetained || shutdown.Failures.Count != 0)) exitCode = 1;
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
