using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.Restart;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>生产菜单、MainWindow Closing 和原布局准备的联合测试；进程副作用仅在该层使用替身。</summary>
public sealed class HostRestartUiTests
{
    [AvaloniaFact]
    public async Task 菜单重启释放自身命令租约_助手就绪后才关闭()
    {
        var handoff = new Handoff();
        using var context = new UiTestContext((services, _) => services.AddSingleton<IHostRestartHandoff>(handoff));
        var coordinator = context.Provider.GetRequiredService<HostRestartCoordinator>();
        var window = new MainWindow(context.Provider.GetRequiredService<WorkbenchWindowContext>(), coordinator) { DataContext = context.ViewModel };
        window.Show();
        var command = Assert.Single(context.ViewModel.WorkbenchCommands.Menu.GetItems(WorkbenchMenuLocations.FileShared)
            .OfType<WorkbenchMenuCommandProjectionEntry>(), entry => entry.CommandId == HostWorkbenchCommandIds.Restart);
        Assert.True(command.Command.IsEnabled);
        command.Command.Execute(null); command.Command.Execute(null);
        await handoff.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(window.IsVisible);
        Assert.True(coordinator.IsRequested);
        Assert.False(coordinator.CanRequest);
        var executor = context.Provider.GetRequiredService<WorkbenchCommandExecutor>();
        Assert.True(await executor.WaitForDrainAsync(TimeSpan.FromSeconds(1)));
        handoff.Ready.SetResult();
        await Flush();
        Assert.False(window.IsVisible);
        Assert.True(handoff.Closed);
        Assert.Equal(1, handoff.Prepares);
        Assert.True(File.Exists(context.LayoutPath));
    }

    [AvaloniaFact]
    public async Task 最终关窗拒绝撤销助手且原工作区可继续使用()
    {
        var handoff = new Handoff();
        using var context = new UiTestContext((services, _) => services.AddSingleton<IHostRestartHandoff>(handoff));
        var coordinator = context.Provider.GetRequiredService<HostRestartCoordinator>();
        var window = new MainWindow(context.Provider.GetRequiredService<WorkbenchWindowContext>(), coordinator) { DataContext = context.ViewModel };
        var deny = true;
        window.Closing += (_, args) => { if (handoff.Prepares > 0 && deny) args.Cancel = true; };
        window.Show();
        var created = await context.Provider.GetRequiredService<DocumentPersistenceCoordinator>()
            .CreateDocumentAsync(HostExtensionIds.WelcomeDocument);
        Assert.Empty(created.Error);
        Assert.Equal(2, context.Workspace.GetDocuments().Count);
        var layout = context.ViewModel.Layout;
        var documents = context.Workspace.GetDocuments().ToArray();
        coordinator.Request();
        await handoff.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handoff.Ready.SetResult();
        await Flush();
        Assert.True(window.IsVisible);
        Assert.False(coordinator.IsRequested);
        Assert.True(coordinator.CanRequest);
        Assert.False(handoff.Closed);
        Assert.Same(layout, context.ViewModel.Layout);
        Assert.Equal(documents, context.Workspace.GetDocuments());
        Assert.True(context.Workspace.CanOperateTools);
        deny = false; window.Close(); await Flush();
        Assert.False(handoff.Closed); // 取消后的普通退出不能消费旧请求。
    }

    [AvaloniaFact]
    public async Task 助手失败恢复窗口与布局调度并显示中文反馈()
    {
        var handoff = new Handoff();
        using var context = new UiTestContext((services, _) => services.AddSingleton<IHostRestartHandoff>(handoff));
        var coordinator = context.Provider.GetRequiredService<HostRestartCoordinator>();
        var window = new MainWindow(context.Provider.GetRequiredService<WorkbenchWindowContext>(), coordinator) { DataContext = context.ViewModel };
        window.Show(); coordinator.Request();
        await handoff.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handoff.Ready.SetException(new IOException());
        await Flush();
        Assert.True(window.IsVisible);
        Assert.True(context.Workspace.CanOperateTools);
        Assert.Contains("助手", context.ViewModel.DocumentOperationError);
        Assert.True(coordinator.CanRequest);
        window.Close(); await Flush();
    }

    private static async Task Flush()
    {
        for (var i = 0; i < 5; i++) await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    [AvaloniaFact]
    public async Task 等待助手期间另一轮原生否决不会被迟到继续关闭()
    {
        var handoff = new Handoff();
        using var context = new UiTestContext((services, _) => services.AddSingleton<IHostRestartHandoff>(handoff));
        var coordinator = context.Provider.GetRequiredService<HostRestartCoordinator>();
        var window = new MainWindow(context.Provider.GetRequiredService<WorkbenchWindowContext>(), coordinator) { DataContext = context.ViewModel };
        window.Show(); coordinator.Request();
        await handoff.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var deny = true;
        window.Closing += (_, e) => e.Cancel = deny;
        window.Close();
        handoff.Ready.SetResult();
        await Flush();
        Assert.True(window.IsVisible);
        Assert.False(coordinator.IsRequested);
        Assert.True(context.Workspace.CanOperateTools);
        deny = false; window.Close(); await Flush();
        Assert.False(handoff.Closed);
    }

    private sealed class Handoff : IHostRestartHandoff
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Prepares;
        internal bool Closed;
        public void Validate() { }
        public Task PrepareAsync() { Prepares++; Entered.TrySetResult(); return Ready.Task; }
        public void ConfirmWindowClosed() => Closed = true;
        public void Abort() { Closed = false; }
    }
}
