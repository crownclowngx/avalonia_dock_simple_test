using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Views;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.UiTests;

/// <summary>通过实际 HostWindow、Factory 初始化与现有 View 验证恢复，不直接把数据对象当恢复成功。</summary>
public sealed class DockLayoutV3UiTests
{
    [AvaloniaFact]
    public async Task 工具浮窗恢复复用原模型和View且关闭后按原组只显示目标工具()
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        var tool = Assert.IsType<ManagedToolDockable>(session.CreatedTools[HostExtensionIds.FileSystemTree.Value]);
        var other = session.CreatedTools[HostExtensionIds.PluginMenu.Value];
        var model = tool.Model;
        var view = tool.PreparedView;
        var document = session.GetDocuments().Single();
        var snapshot = Floating(visible: true);
        try
        {
            context.ViewModel.Layout = session.LayoutState.Apply(session, snapshot);
            await Flush();
            var first = Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            Assert.True(Assert.IsType<HostFloatingWindow>(first.Host).IsVisible);
            Assert.Same(model, tool.Model);
            Assert.Same(view, tool.PreparedView);
            Assert.Same(document, session.GetDocuments().Single());
            Assert.False(DockTreeNavigator.IsDockableAttached(session.RootDock!, other));
            Assert.Contains(tool, DockTreeNavigator.EnumerateWorkspace(session.RootDock!));
            var captured = session.LayoutState.Capture(session);
            Assert.Equal("saved-window", Assert.Single(captured.FloatingWindows).Id);
            Assert.Equal("saved-group", captured.FloatingWindows[0].Root.Id);

            Assert.IsType<HostFloatingWindow>(first.Host).Close();
            await Flush();
            Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            Assert.Equal("hidden", session.LayoutState.Capture(session).Tools.Single(item => item.Id == tool.Id).State);
            Assert.True(session.OpenTool(HostExtensionIds.FileSystemTree.Value).Succeeded);
            await Flush();
            var restored = session.LayoutState.Capture(session);
            Assert.Equal("saved-window", Assert.Single(restored.FloatingWindows).Id);
            Assert.Equal("saved-group", restored.FloatingWindows[0].Root.Id);
            Assert.Equal("hidden", restored.Tools.Single(item => item.Id == other.Id).State);
            Assert.Same(model, tool.Model);
            Assert.Same(view, tool.PreparedView);
        }
        finally { await CloseWindows(context, main); }
    }

    [AvaloniaFact]
    public async Task 全隐藏或插件缺失的浮窗不创建空窗口但保留恢复记录()
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var main = new MainWindow(session.DockFactory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        try
        {
            var snapshot = Floating(visible: false);
            snapshot = snapshot with
            {
                FloatingWindows = [.. snapshot.FloatingWindows, new("missing-window", DockWindowBounds.Default,
                    DockLayoutNode.Group("missing-group", ["missing.plugin.tool"]))],
                Tools = [.. snapshot.Tools, new("missing.plugin.tool", "visible", DockLayoutIds.RightTools, 0)],
            };
            context.ViewModel.Layout = session.LayoutState.Apply(session, snapshot);
            await Flush();
            Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            var captured = session.LayoutState.Capture(session);
            Assert.Equal(2, captured.FloatingWindows.Count);
            Assert.Equal("visible", captured.Tools.Single(tool => tool.Id == "missing.plugin.tool").State);
            Assert.True(session.OpenTool(HostExtensionIds.FileSystemTree.Value).Succeeded);
            await Flush();
            Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
        }
        finally { await CloseWindows(context, main); }
    }

    [AvaloniaFact]
    public async Task 应用工具布局不重开文档且把已有浮动文档原实例安全回停()
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        var document = session.GetDocuments().Single();
        var view = document.PreparedView;
        factory.RemoveDockable(document, collapse: false);
        var group = new DocumentDock { VisibleDockables = factory.CreateList<IDockable>(document), ActiveDockable = document };
        var root = new RootDock { VisibleDockables = factory.CreateList<IDockable>(group), ActiveDockable = group };
        var window = new DockWindow { Layout = root, Width = 700, Height = 500 };
        root.Window = window;
        factory.AddWindow(session.RootDock!, window);
        window.Present(false);
        var host = Assert.IsType<HostFloatingWindow>(window.Host);
        try
        {
            Assert.Empty(session.LayoutState.Capture(session).FloatingWindows);
            context.ViewModel.Layout = session.LayoutState.Apply(session, Floating(visible: false));
            await Flush();
            Assert.False(host.IsVisible);
            Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            Assert.Same(document, session.GetDocuments().Single());
            Assert.Same(view, document.PreparedView);
            Assert.False(document.ClosingToken.IsCancellationRequested);
            Assert.NotNull(DockTreeNavigator.FindDocumentDock(session.RootDock!, document));
        }
        finally { await CloseWindows(context, main); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task 四个浮动入口走真实窗口且回停不重建工具(bool group, bool options)
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        try
        {
            session.OpenTool(HostExtensionIds.FileSystemTree.Value);
            session.OpenTool(HostExtensionIds.PluginMenu.Value);
            var tool = (ManagedToolDockable)session.CreatedTools[HostExtensionIds.FileSystemTree.Value];
            var other = session.CreatedTools[HostExtensionIds.PluginMenu.Value];
            var originalDock = (IDock)tool.Owner!;
            if (!ReferenceEquals(other.Owner, originalDock)) factory.MoveDockable((IDock)other.Owner!, originalDock, other, null);
            var view = tool.PreparedView;
            await Flush();
            if (group)
            {
                if (options) factory.FloatAllDockables(tool, new DockWindowOptions());
                else factory.FloatAllDockables(tool);
            }
            else
            {
                if (options) factory.FloatDockable(tool, new DockWindowOptions());
                else factory.FloatDockable(tool);
            }
            await Flush();
            var window = Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            var native = Assert.IsType<HostFloatingWindow>(window.Host);
            Assert.True(native.IsVisible);
            Assert.False(tool.CanPin);
            factory.PinDockable(tool);
            Assert.Same(window, DockTreeNavigator.FindWindow(session.RootDock!, tool));
            if (group) Assert.Same(window, DockTreeNavigator.FindWindow(session.RootDock!, other));
            Assert.Same(view, tool.PreparedView);
            var target = session.EnsureToolDock(session.RootDock!, Alignment.Left);
            factory.MoveDockable((IDock)tool.Owner!, target, tool, null);
            await Flush();
            Assert.Null(DockTreeNavigator.FindWindow(session.RootDock!, tool));
            Assert.True(tool.CanPin);
            Assert.Same(view, tool.PreparedView);
            factory.FloatDockable(session.RootDock!);
            factory.FloatDockable(factory.GetDockable<Dock.Model.Controls.IDocumentDock>(DockLayoutIds.Documents)!);
            Assert.NotNull(factory.GetDockable<Dock.Model.Controls.IDocumentDock>(DockLayoutIds.Documents));
        }
        finally { await CloseWindows(context, main); }
    }

    [AvaloniaFact]
    public async Task 主窗口退出先保存可见浮窗再拆除且重启不恢复文档()
    {
        DockLayoutSnapshotV3 saved;
        using (var context = new UiTestContext())
        {
            var session = context.Workspace;
            var factory = session.DockFactory;
            var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
            main.Show();
            session.OpenTool(HostExtensionIds.FileSystemTree.Value);
            factory.FloatDockable(session.CreatedTools[HostExtensionIds.FileSystemTree.Value]);
            var document = session.GetDocuments().Single();
            var view = document.PreparedView;
            factory.FloatDockable(document);
            await Flush();
            Assert.Equal(2, DockTreeNavigator.EnumerateWindows(session.RootDock!).Count());
            Assert.Same(view, document.PreparedView);
            main.Close();
            Assert.False(main.IsVisible);
            Assert.True(document.ClosingToken.IsCancellationRequested);
            using var reader = new DockLayoutV3Store(context.TempDirectory);
            saved = reader.Load()!;
            Assert.Single(saved.FloatingWindows);
            Assert.Equal("visible", saved.Tools.Single(tool => tool.Id == HostExtensionIds.FileSystemTree.Value).State);
            await Flush();
            Assert.Equal("visible", reader.Load()!.Tools.Single(tool => tool.Id == HostExtensionIds.FileSystemTree.Value).State);
        }
        using var restart = new UiTestContext(initialLayoutV3: saved);
        var reopened = new MainWindow(restart.Workspace.DockFactory.WindowContext) { DataContext = restart.ViewModel };
        reopened.Show();
        await Flush();
        Assert.Single(restart.Workspace.GetDocuments()); // 只创建默认欢迎页，布局没有任何文档恢复入口。
        Assert.Single(DockTreeNavigator.EnumerateWindows(restart.Workspace.RootDock!));
        reopened.Close();
    }

    [AvaloniaFact]
    public async Task 主窗等待干净文档命令后被原生取消会恢复命令与创建入口()
    {
        using var context = new UiTestContext();
        var factory = context.Workspace.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        var document = context.Workspace.GetDocuments().Single();
        var leases = context.Provider.GetRequiredService<WorkbenchDocumentCommandLeaseStore>();
        Assert.True(leases.TryAcquire(document, out var pending));
        var calls = 0;
        EventHandler<WindowClosingEventArgs> cancelRetry = (_, args) => { if (++calls == 2) args.Cancel = true; };
        main.Closing += cancelRetry;
        main.Close();
        Assert.True(main.IsVisible);
        Assert.False(document.ClosingToken.IsCancellationRequested);
        Assert.False(context.Workspace.CanCreateDocuments);
        pending!.Dispose();
        await UiTestWait.UntilAsync(() => calls >= 2, "等待在途命令结束后的第二次关闭回调");
        Assert.Equal(2, calls);
        Assert.True(main.IsVisible);
        Assert.True(context.Workspace.CanCreateDocuments);
        Assert.True(leases.TryAcquire(document, out var resumed));
        resumed!.Dispose();
        main.Closing -= cancelRetry;
        main.Close();
        Assert.False(main.IsVisible);
    }

    [AvaloniaFact]
    public async Task 内容全屏阻止迁移且重置保留文档实例并关闭浮窗()
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        var document = session.GetDocuments().Single();
        var view = document.PreparedView;
        using (var fullscreen = ((IWindowContentFullscreenHost)main).TryPresent(new Border()))
        {
            Assert.NotNull(fullscreen);
            factory.FloatDockable(document);
            Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
            Assert.Throws<InvalidOperationException>(() => context.Provider.GetRequiredService<DockLayoutLifecycle>().Reset(session));
        }
        factory.FloatDockable(document);
        await Flush();
        Assert.Single(DockTreeNavigator.EnumerateWindows(session.RootDock!));
        context.ViewModel.Layout = context.Provider.GetRequiredService<DockLayoutLifecycle>().Reset(session);
        await Flush();
        Assert.Empty(DockTreeNavigator.EnumerateWindows(session.RootDock!));
        Assert.Same(document, session.GetDocuments().Single());
        Assert.Same(view, document.PreparedView);
        Assert.False(document.ClosingToken.IsCancellationRequested);
        Assert.All(session.LayoutState.Capture(session).Tools, tool => Assert.Equal("hidden", tool.State));
        main.Close();
    }

    [AvaloniaFact]
    public async Task 重置被原生窗口拒绝时回滚已关闭的其他窗口并保留全部原实例()
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var factory = session.DockFactory;
        var main = new MainWindow(factory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        session.OpenTool(HostExtensionIds.FileSystemTree.Value);
        var tool = session.CreatedTools[HostExtensionIds.FileSystemTree.Value];
        factory.FloatDockable(tool);
        var document = session.GetDocuments().Single();
        var view = document.PreparedView;
        factory.FloatDockable(document);
        await Flush();
        var rejected = (HostFloatingWindow)DockTreeNavigator.FindWindow(session.RootDock!, document)!.Host!;
        EventHandler<WindowClosingEventArgs> cancel = (_, args) => args.Cancel = true;
        rejected.Closing += cancel;
        try
        {
            Assert.Throws<InvalidOperationException>(() => context.Provider.GetRequiredService<DockLayoutLifecycle>().Reset(session));
            await Flush();
            Assert.Equal(2, DockTreeNavigator.EnumerateWindows(session.RootDock!).Count());
            Assert.Same(rejected, DockTreeNavigator.FindWindow(session.RootDock!, document)!.Host);
            Assert.NotNull(DockTreeNavigator.FindWindow(session.RootDock!, tool));
            Assert.Same(view, document.PreparedView);
            Assert.Same(document, session.GetDocuments().Single());
            Assert.False(document.ClosingToken.IsCancellationRequested);
        }
        finally { rejected.Closing -= cancel; main.Close(); }
    }

    [AvaloniaFact]
    public async Task 最终布局写入失败保留窗口与命令且重试成功后可以退出()
    {
        using var context = new UiTestContext();
        var session = context.Workspace;
        var main = new MainWindow(session.DockFactory.WindowContext) { DataContext = context.ViewModel };
        main.Show();
        await context.ViewModel.RetryLayoutSaveCommand.ExecuteAsync(null);
        var original = File.ReadAllBytes(context.LayoutPath);
        using (var occupied = new FileStream(context.LayoutPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            main.Close();
            Assert.True(main.IsVisible);
            Assert.True(session.CanCreateDocuments);
            Assert.True(context.ViewModel.HasLayoutMessage);
            var leases = context.Provider.GetRequiredService<WorkbenchDocumentCommandLeaseStore>();
            Assert.True(leases.TryAcquire(session.GetDocuments().Single(), out var lease));
            lease!.Dispose();
        }
        Assert.Equal(original, File.ReadAllBytes(context.LayoutPath));
        await context.ViewModel.RetryLayoutSaveCommand.ExecuteAsync(null);
        Assert.False(context.ViewModel.HasLayoutMessage);
        main.Close();
        Assert.False(main.IsVisible);
    }

    private static DockLayoutSnapshotV3 Floating(bool visible) => new(3,
        new("main", DockWindowBounds.Default, DockLayoutNode.Documents()),
        [new("saved-window", new(80, 60, 600, 400, false, null), DockLayoutNode.Group("saved-group",
            [HostExtensionIds.FileSystemTree.Value, HostExtensionIds.PluginMenu.Value]))],
        [new(HostExtensionIds.FileSystemTree.Value, visible ? "visible" : "hidden", DockLayoutIds.LeftTools, 0),
         new(HostExtensionIds.PluginMenu.Value, "hidden", DockLayoutIds.LeftTools, 1)]);

    private static Task Flush() => UiTestWait.DrainAsync();
    private static async Task CloseWindows(UiTestContext context, MainWindow main)
    {
        foreach (var window in DockTreeNavigator.EnumerateWindows(context.Workspace.RootDock!).ToArray())
            (window.Host as Window)?.Close();
        await Flush();
        main.Close();
    }
}
