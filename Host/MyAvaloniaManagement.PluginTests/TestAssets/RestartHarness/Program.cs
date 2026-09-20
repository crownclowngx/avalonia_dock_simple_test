using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.Restart;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.ViewModels.FunctionCenter;
using MyAvaloniaManagement.ViewModels.PluginStatus;
using MyAvaloniaManagement.Views.PluginStatus;
using MyAvaloniaManagement.Views.FunctionCenter;
using MyAvaloniaManagement.Views;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.RestartHarness;

/// <summary>测试侧可执行入口：只替换 UI 平台和交互驱动，完整复用生产 Program、组合、正常关闭及助手。</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var root = args[^1];
        var helper = RestartHelperRunner.IsHelper(args);
        var mode = helper ? args[6] : args[0];
        var stageFile = Path.Combine(root, "stage.txt");
        var stage = File.Exists(stageFile) ? int.Parse(File.ReadAllText(stageFile)) : 0;
        if (!helper) File.WriteAllText(stageFile, (stage + 1).ToString());
        var identity = helper ? $"helper-{Environment.ProcessId}" : $"host-{stage}";
        File.WriteAllText(Path.Combine(root, identity + ".start.json"), JsonSerializer.Serialize(new
        { pid = Environment.ProcessId, startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
            helper, stage, at = DateTimeOffset.UtcNow, args = helper ? args[6..] : args,
            workingDirectory = Environment.CurrentDirectory, dataRoot = Environment.GetEnvironmentVariable("MYAVALONIA_DATA_DIRECTORY") }));
        var failure = false;
        var result = MyAvaloniaManagement.Program.Run(args, (startupBuilder, arguments) =>
        {
            var builder = startupBuilder.UseHeadless(new AvaloniaHeadlessPlatformOptions());
            builder.AfterSetup(_ => Dispatcher.UIThread.Post(async () =>
            {
                var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
                try
                {
                    var application = (MyAvaloniaManagement.App)Application.Current!;
                    if (mode is "startup-slow" or "startup-cancel-loading")
                    {
                        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        using var watcher = new FileSystemWatcher(root, "startup-entered.json");
                        watcher.Created += (_, _) => entered.TrySetResult();
                        watcher.EnableRaisingEvents = true;
                        if (File.Exists(Path.Combine(root, "startup-entered.json"))) entered.TrySetResult();
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                        var splash = desktop.MainWindow as SplashWindow ?? throw new InvalidOperationException("慢插件初始化前未显示首屏。");
                        if (!splash.IsVisible || application.StartupFirstFrameElapsed is null || application.StartupCompletion.IsCompleted)
                            throw new InvalidOperationException("插件同步阻塞期间的首帧与启动任务状态不符。");
                        // 在插件仍同步等待时，再取得一帧，证明 UI 消息循环确实持续工作。
                        var nextFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        splash.RequestAnimationFrame(_ => nextFrame.TrySetResult());
                        await nextFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        File.WriteAllText(Path.Combine(root, "startup-responsive.json"), JsonSerializer.Serialize(new
                        { visible = splash.IsVisible, frameDuringBlockedPlugin = true, uiThread = Environment.CurrentManagedThreadId }));
                        if (mode == "startup-cancel-loading")
                        {
                            splash.Close();
                            await application.StartupCompletion;
                            File.WriteAllText(Path.Combine(root, "startup.json"), JsonSerializer.Serialize(new
                            { mainCreated = desktop.Windows.OfType<MainWindow>().Any(), cancelled = true }));
                            return;
                        }
                        File.WriteAllText(Path.Combine(root, "startup-release.json"), "{}");
                    }
                    if (mode == "startup-cancel")
                    {
                        var splash = desktop.MainWindow as SplashWindow ?? throw new InvalidOperationException("启动首屏缺失。");
                        splash.Close();
                        await application.StartupCompletion;
                        File.WriteAllText(Path.Combine(root, "startup.json"), JsonSerializer.Serialize(new
                        { mainCreated = desktop.Windows.OfType<MainWindow>().Any(), cancelled = true }));
                        return;
                    }
                    await application.StartupCompletion;
                    if (mode == "startup-failure")
                    {
                        var failureWindow = desktop.MainWindow as StartupFailureWindow ?? throw new InvalidOperationException("未显示最小启动失败界面。");
                        File.WriteAllText(Path.Combine(root, "startup.json"), JsonSerializer.Serialize(new
                        { mainCreated = desktop.Windows.OfType<MainWindow>().Any(), failureVisible = failureWindow.IsVisible }));
                        failureWindow.Close();
                        return;
                    }
                    if (application.StartupFirstFrameElapsed is not { } firstFrame || application.StartupReadyElapsed is not { } ready || ready < firstFrame)
                        throw new InvalidOperationException("首帧与工作台交接的诊断时序缺失。");
                    if (desktop.Windows.OfType<SplashWindow>().Any()) throw new InvalidOperationException("交接后启动窗口仍然存活。");
                    var window = desktop.MainWindow!;
                    var model = (MainWindowViewModel)window.DataContext!;
                    // 新 Host 在生产组合时已尝试获取布局锁；测试另一个 Writer 必须失败，证明其确实拥有锁。
                    var lockPath = Path.Combine(root, "data", "layout-v3.lock");
                    if (!File.Exists(lockPath)) throw new InvalidOperationException("布局锁文件尚未建立，不能把文件缺失当成锁占用。");
                    var locked = false;
                    try { using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                    catch (IOException) { locked = true; }
                    if (!locked) throw new InvalidOperationException("真实 Host 未持有布局锁。");
                    File.WriteAllText(Path.Combine(root, $"startup-{stage}.json"), JsonSerializer.Serialize(new
                    { firstFrameMs = firstFrame.TotalMilliseconds, readyMs = ready.TotalMilliseconds,
                        mainVisible = window.IsVisible, splashClosed = !desktop.Windows.OfType<SplashWindow>().Any() }));
                    if (mode is "startup-empty" or "startup-disabled" or "startup-warning" or "startup-slow")
                    {
                        var banner = window.FindControl<Border>("StartupWarningBanner")!;
                        if (banner.IsVisible != (mode == "startup-warning")) throw new InvalidOperationException("失败摘要不符合真实启动事实。");
                        File.WriteAllText(Path.Combine(root, "startup.json"), JsonSerializer.Serialize(new
                        { mainCreated = true, warning = banner.IsVisible }));
                        window.Close();
                        return;
                    }
                    if (mode == "roundtrip")
                    {
                        var layout = model.Layout ?? throw new InvalidOperationException("工作区尚未初始化。");
                        var initialDocuments = DockTreeNavigator.EnumerateWorkspace(layout).OfType<ManagedDocumentDockable>().Count();
                        if (initialDocuments != 1 || DockTreeNavigator.EnumerateWorkspace(layout).OfType<ManagedDocumentDockable>()
                                .Single().Registration.Descriptor.DocumentTypeId != HostExtensionIds.WelcomeDocument)
                            throw new InvalidOperationException("新 Host 应仅有默认欢迎页，不应重开上次额外创建的页面。");
                        if (stage == 0)
                        {
                            // 通过真实功能中心创建一个页面，证明后继空工作区不是空输入造成的假阳性。
                            var create = model.WorkbenchCommands.Menu.GetItems(WorkbenchMenuLocations.FileShared)
                                .OfType<WorkbenchMenuCommandProjectionEntry>().Single(item => item.CommandId == HostWorkbenchCommandIds.NewDocument);
                            create.Command.Execute(null);
                            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                            var center = (FunctionCenterViewModel)desktop.Windows.OfType<FunctionCenterWindow>().Single().DataContext!;
                            center.SelectedItem = center.VisibleItems.Single(item => item.Entry.DocumentTypeId == HostExtensionIds.WelcomeDocument);
                            await center.CreateAsync();
                            if (DockTreeNavigator.EnumerateWorkspace(layout).OfType<ManagedDocumentDockable>().Count() != 2)
                                throw new InvalidOperationException("测试页面未成功创建。");
                        }
                        var open = model.WorkbenchCommands.Menu.GetItems(WorkbenchMenuLocations.ToolsShared)
                            .OfType<WorkbenchMenuCommandProjectionEntry>().Single(item => item.CommandId == HostWorkbenchCommandIds.OpenPluginStatus);
                        open.Command.Execute(null);
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                        var dashboard = desktop.Windows.OfType<PluginStatusWindow>().Single();
                        var status = (PluginStatusWindowViewModel)dashboard.DataContext!;
                        var selected = status.VisibleItems.Single(item => item.PluginId == "myavalonia.plugin.my-plug-test");
                        status.SelectedItem = selected;
                        if (selected.IsDisabled != (stage == 1)) throw new InvalidOperationException("插件启动策略不符合阶段。");
                        File.WriteAllText(Path.Combine(root, $"state-{stage}.json"), JsonSerializer.Serialize(new
                        { pid = Environment.ProcessId, disabled = selected.IsDisabled, locked, initialDocuments,
                            documentsBeforeClose = DockTreeNavigator.EnumerateWorkspace(layout).OfType<ManagedDocumentDockable>().Count() }));
                        if (stage < 2)
                        {
                            await status.ToggleEnablementAsync();
                            if (!status.CanRestart) throw new InvalidOperationException("保存后不能请求重启。");
                            var restartButton = dashboard.FindControl<Button>("RestartHostButton")!;
                            if (!restartButton.IsVisible || !restartButton.IsEnabled || restartButton.Command is null)
                                throw new InvalidOperationException("看板重启按钮绑定未就绪。");
                            restartButton.Command.Execute(null);
                        }
                        else window.Close();
                    }
                    else if (mode == "ordinary" || mode == "delayed-exit" && stage > 0) window.Close();
                    else
                    {
                        if (mode is "cancel" or "crash" or "kill")
                        {
                            window.Closing += (_, e) =>
                            {
                                // 第一次事件用于异步准备；只在助手 Ready 后的最终关闭才注入失败。
                                if (e.Cancel || Directory.GetFiles(root, "helper-*.start.json").Length == 0) return;
                                if (mode == "crash") Environment.Exit(0);
                                if (mode == "kill")
                                {
                                    File.WriteAllText(Path.Combine(root, "parent-ready.json"), "{}");
                                    using var hold = new ManualResetEventSlim();
                                    hold.Wait(TimeSpan.FromSeconds(25)); // 仅夹具阻塞；父测试取得明确就绪信号后强杀其拥有的进程。
                                }
                                e.Cancel = true;
                            };
                        }
                        var command = model.WorkbenchCommands.Menu.GetItems(WorkbenchMenuLocations.FileShared)
                            .OfType<WorkbenchMenuCommandProjectionEntry>().Single(item => item.CommandId == HostWorkbenchCommandIds.Restart);
                        command.Command.Execute(null);
                        if (mode == "cancel")
                        {
                            var observed = false;
                            // 取消的观察由绑定通知驱动，不能用 sleep 假定异步关窗已经结束。
                            model.PropertyChanged += (_, e) =>
                            {
                                if (e.PropertyName != nameof(model.HasRestartMessage) || model.HasRestartMessage || observed) return;
                                observed = true;
                                File.WriteAllText(Path.Combine(root, "cancelled.json"), JsonSerializer.Serialize(new { visible = window.IsVisible }));
                                Dispatcher.UIThread.Post(() => desktop.Shutdown(0));
                            };
                        }
                    }
                }
                catch (Exception exception)
                {
                    failure = true;
                    File.WriteAllText(Path.Combine(root, identity + ".error"), exception.ToString());
                    desktop.Shutdown(1);
                }
            }, DispatcherPriority.Background));
            var code = builder.StartWithClassicDesktopLifetime(arguments);
            return mode == "bad-exit" || failure ? 1 : code;
        }, exception =>
        {
            File.WriteAllText(Path.Combine(root, identity + ".error"), exception.ToString());
            return 1;
        });
        File.WriteAllText(Path.Combine(root, identity + ".exit.json"), JsonSerializer.Serialize(new
        { pid = Environment.ProcessId, code = result, at = DateTimeOffset.UtcNow }));
        if (!helper && mode == "delayed-exit" && stage == 0)
        {
            // Program 已发送并收到 CleanExit 确认，但 OS 进程尚在。由父测试的文件信号放行，验证助手等待真实退出。
            using var release = new ManualResetEventSlim();
            using var watcher = new FileSystemWatcher(root, "release.json");
            watcher.Created += (_, _) => release.Set();
            watcher.EnableRaisingEvents = true;
            if (!File.Exists(Path.Combine(root, "release.json")) && !release.Wait(TimeSpan.FromSeconds(25))) return 1;
        }
        return result;
    }
}
