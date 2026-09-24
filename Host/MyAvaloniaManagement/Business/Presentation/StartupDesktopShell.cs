using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Startup;
using MyAvaloniaManagement.ViewModels.Startup;
using MyAvaloniaManagement.Views;

namespace MyAvaloniaManagement.Business.Presentation;

/// <summary>
/// 只负责启动过程的桌面适配：首帧、快照刷新和窗口交接。协调器决定任务终态，Runtime 决定资源所有权。
/// 所有窗口操作均在 UI 线程执行；异步失败被统一捕获，不把 async void 异常交给消息循环兜底。
/// </summary>
internal sealed class StartupDesktopShell(StartupCoordinator startup, HostDiagnosticSession diagnostics)
{
    internal Task Attach(App application, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalShutdownMode = desktop.ShutdownMode;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var model = new SplashViewModel();
        var splash = new SplashWindow(model);
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        refresh.Tick += (_, _) =>
        {
            try { model.Apply(startup.Progress.Snapshot); }
            catch (Exception exception) { refresh.Stop(); startup.Fail(exception); }
        };
        splash.CancellationRequested += Cancel;
        desktop.ShutdownRequested += OnShutdownRequested;
        desktop.MainWindow = splash;
        // 动画帧回调只安排后台优先级任务，让本次渲染先返回消息循环，再执行启动工厂。
        splash.Opened += (_, _) => splash.RequestAnimationFrame(frameTime =>
        {
            application.StartupFirstFrameElapsed = ProcessElapsed();
            Dispatcher.UIThread.Post(() => { _ = StartAsync(); }, DispatcherPriority.Background);
        });
        splash.Show();
        refresh.Start();
        return completion.Task;

        void Cancel()
        {
            if (!startup.RequestCancellation()) return;
            model.Stop();
            splash.StopAnimation();
        }

        void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs args)
        { args.Cancel = true; Cancel(); }

        async Task StartAsync()
        {
            try
            {
                await startup.StartAsync();
                if (startup.State == StartupState.Failed) throw startup.Failure!;
                if (startup.State is StartupState.Cancelled or StartupState.Cancelling)
                { await CancelAndExitAsync(); return; }

                startup.Progress.Report(new(StartupStage.Workbench));
                model.Apply(startup.Progress.Snapshot);
                // 留出一次 UI 调度机会，确保用户看得到“准备主界面”，随后才解析完整工作台对象图。
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (startup.State == StartupState.Cancelling) { await CancelAndExitAsync(); return; }
                startup.Runtime!.AttachWorkbench(application, desktop);
                var main = desktop.MainWindow ?? throw new InvalidOperationException("主窗口装配未交付窗口。");
                if (ReferenceEquals(main, splash)) throw new InvalidOperationException("主窗口未完成交接。");
                main.Show();
                // Show 会同步执行 Opened 与布局恢复；取消若在该提交点前被接受，则不交接成功。
                if (!main.IsVisible || !startup.TryComplete()) { await CancelAndExitAsync(); return; }
                await startup.Runtime.ConfirmInstallationStartupAsync();
                desktop.ShutdownRequested -= OnShutdownRequested;
                var failures = diagnostics.Snapshot.Count(item => item.Severity >= HostDiagnosticSeverity.Warning &&
                    item.Phase is HostDiagnosticPhase.PluginRootDiscovery or HostDiagnosticPhase.PluginManifestPreflight or
                        HostDiagnosticPhase.PluginAssemblyLoad or HostDiagnosticPhase.PluginTypePreflight or
                        HostDiagnosticPhase.PluginModuleDiscovery or HostDiagnosticPhase.PluginServiceRegistration or
                        HostDiagnosticPhase.ExtensionDiscovery or HostDiagnosticPhase.PluginLifecycle);
                if (failures > 0 && main is MainWindow window)
                    startup.Runtime.ShowStartupWarning(window, failures);
                splash.CompleteAndClose();
                desktop.ShutdownMode = originalShutdownMode;
                application.StartupReadyElapsed = ProcessElapsed();
                Console.WriteLine($"Startup firstFrameMs={application.StartupFirstFrameElapsed?.TotalMilliseconds:F0} readyMs={application.StartupReadyElapsed?.TotalMilliseconds:F0}");
            }
            catch (Exception exception)
            {
                try
                {
                    startup.Fail(exception);
                    splash.StopAnimation();
                    if (exception is MyAvaloniaManagement.Business.Composition.HostCompositionException composition)
                        MyAvaloniaManagement.Business.Composition.HostRuntime.ReportCompositionDiagnostics(
                            diagnostics, composition, HostDiagnosticPhase.ExtensionDiscovery);
                    // 已有阶段致命诊断优先；仅真正逃逸的异常使用启动兜底码。
                    if (!diagnostics.Snapshot.Any(item => item.Disposition == HostDiagnosticDisposition.AbortStartup))
                        diagnostics.Report(new HostDiagnosticDraft(HostDiagnosticCodes.HostStartupUnexpected,
                            HostDiagnosticPhase.HostBootstrap) { Exception = exception });
                    CloseUnpublishedWindows();
                    await CleanupAsync();
                    desktop.ShutdownRequested -= OnShutdownRequested;
                    application.InstallMinimalResources();
                    var failure = new StartupFailureWindow(new(diagnostics.Snapshot, diagnostics.LogPath));
                    failure.Closed += (_, _) => desktop.Shutdown(1);
                    desktop.MainWindow = failure;
                    failure.Show();
                    splash.CompleteAndClose();
                    if (Environment.GetEnvironmentVariable("MYAVALONIA_SMOKE_TEST") == "1")
                        Dispatcher.UIThread.Post(failure.Close, DispatcherPriority.Background);
                }
                catch (Exception presentationException)
                {
                    // 最小错误窗口自身也可能失败，必须结束消息循环并交还 Program 的最终收尾责任。
                    Console.Error.WriteLine($"Host errorCode=HOST_STARTUP_PRESENTATION_FAILED type={presentationException.GetType().Name}");
                    desktop.ShutdownRequested -= OnShutdownRequested;
                    desktop.Shutdown(1);
                }
            }
            finally
            {
                refresh.Stop();
                startup.Progress.Close();
                splash.CancellationRequested -= Cancel;
                completion.TrySetResult();
            }
        }

        static TimeSpan ProcessElapsed()
        {
            using var process = Process.GetCurrentProcess();
            return DateTime.UtcNow - process.StartTime.ToUniversalTime();
        }

        async Task CleanupAsync()
        {
            if (startup.Runtime is not { } runtime) return;
            try { await runtime.ShutdownAsync(); }
            catch (Exception exception)
            { diagnostics.Report(new HostDiagnosticDraft(HostDiagnosticCodes.HostStartupCleanupFailed,
                HostDiagnosticPhase.HostBootstrap) { Exception = exception }); }
        }

        async Task CancelAndExitAsync()
        {
            CloseUnpublishedWindows();
            await CleanupAsync();
            desktop.ShutdownRequested -= OnShutdownRequested;
            splash.CompleteAndClose();
            desktop.Shutdown(1);
        }

        void CloseUnpublishedWindows()
        {
            // 此时尚未交接用户会话。先撤下窗口绑定并关闭外壳，再由 Runtime 释放模型/Scope；
            // 不能仅 Hide 一个半装配主窗口，否则退出时可能再次进入已失效的文档关闭协议。
            foreach (var window in desktop.Windows.Where(window => !ReferenceEquals(window, splash)).ToArray())
            {
                window.Hide();
                window.DataContext = null;
                window.Close();
            }
        }
    }
}
