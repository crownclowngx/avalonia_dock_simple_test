using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.ToolCenter;
using MyAvaloniaManagement.Views;
using MyAvaloniaManagement.Views.ToolCenter;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

public sealed class ToolCenterUiTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.tool-center-test");
    private static readonly ToolTypeId ToolId = new($"{Owner.Value}.tool.sample-0");

    [AvaloniaFact]
    public async Task 菜单打开非模态单实例窗口且显隐收藏通过真实按钮提交()
    {
        using var context = new UiTestContext();
        var service = context.Provider.GetRequiredService<ToolCenterWindowService>();
        var owner = new MainWindow();
        service.Attach(owner);
        owner.DataContext = context.ViewModel;
        owner.Show();
        try
        {
            var presentation = context.Provider.GetRequiredService<WorkbenchCommandPresentation>();
            var menu = Assert.Single(presentation.Menu.GetItems(WorkbenchMenuLocations.ToolsShared).OfType<WorkbenchMenuCommandProjectionEntry>(),
                item => item.CommandId == HostWorkbenchCommandIds.OpenToolCenter);
            Assert.True(menu.Command.IsEnabled);
            menu.Command.Execute(null);
            await Flush();
            var window = Assert.IsType<ToolCenterWindow>(service.CurrentWindow);
            var vm = Assert.IsType<ToolCenterViewModel>(window.DataContext);
            Assert.Same(owner, window.Owner);
            Assert.False(window.IsDialog);
            Assert.True(owner.IsEnabled);
            Assert.True(window.FindControl<TextBox>("ToolSearchBox")!.IsFocused);
            Assert.All(vm.VisibleItems, item => Assert.False(item.IsVisible));
            Assert.False(vm.CanHideAll);
            window.WindowState = WindowState.Minimized;
            service.ShowOrActivate();
            Assert.Same(window, service.CurrentWindow);
            Assert.Equal(WindowState.Normal, window.WindowState);
            var id = HostExtensionIds.FileSystemTree.Value;
            var adapter = context.Workspace.CreatedTools[id];
            var prepared = ((ManagedToolDockable)adapter).PreparedView;
            Click(window, RowButton(window, id, "显示"));
            await Flush();
            Assert.True(vm.VisibleItems.Single(item => item.ToolId == id).IsVisible);
            Assert.True(vm.CanHideAll);
            Click(window, RowButton(window, id, "☆"));
            await Flush();
            Assert.True(vm.VisibleItems.Single(item => item.ToolId == id).IsFavorite);
            var more = RowButton(window, id, "⋯");
            Click(window, more);
            await Flush();
            var hide = Assert.IsType<MenuItem>(more.ContextMenu!.Items[0]);
            hide.Command!.Execute(hide.CommandParameter);
            more.ContextMenu?.Close();
            await Flush();
            Assert.False(vm.VisibleItems.Single(item => item.ToolId == id).IsVisible);
            Assert.True(vm.VisibleItems.Single(item => item.ToolId == id).IsFavorite);
            Assert.Same(adapter, context.Workspace.CreatedTools[id]);
            Assert.Same(prepared, ((ManagedToolDockable)adapter).PreparedView);
            await Render(window, "tool-center-light");
            window.RequestedThemeVariant = ThemeVariant.Dark;
            await Render(window, "tool-center-dark");
            window.Width = 720; window.Height = 600;
            await Render(window, "tool-center-compact");
            Assert.Single(context.Workspace.GetDocuments());
        }
        finally { service.Dispose(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task 标题栏关闭最后一个工具后可从工具中心反复显示且复用视图()
    {
        using var context = new UiTestContext();
        var service = context.Provider.GetRequiredService<ToolCenterWindowService>();
        var owner = new MainWindow { DataContext = context.ViewModel, Width = 1600, Height = 1000 };
        service.Attach(owner);
        owner.Show();
        try
        {
            service.ShowOrActivate();
            await Flush();
            var window = Assert.IsType<ToolCenterWindow>(service.CurrentWindow);
            var vm = Assert.IsType<ToolCenterViewModel>(window.DataContext);
            foreach (var id in new[] { HostExtensionIds.PluginMenu.Value, HostExtensionIds.FileSystemTree.Value })
            {
                var adapter = Assert.IsType<ManagedToolDockable>(context.Workspace.CreatedTools[id]);
                var prepared = Assert.IsAssignableFrom<Control>(adapter.PreparedView);
                var model = adapter.Model;
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    Click(window, RowButton(window, id, "显示"));
                    await Flush();
                    Assert.False(vm.HasError, vm.Error);
                    Assert.True(vm.VisibleItems.Single(item => item.ToolId == id).IsVisible);
                    Assert.Same(adapter, context.Workspace.CreatedTools[id]);
                    Assert.Same(model, adapter.Model);
                    Assert.Same(prepared, adapter.PreparedView);
                    Assert.Contains(prepared, owner.GetVisualDescendants());

                    var close = Assert.Single(owner.GetVisualDescendants().OfType<Button>(),
                        button => button.Name == "PART_CloseButton" && ReferenceEquals(button.CommandParameter, adapter));
                    Assert.True(close.IsEffectivelyVisible);
                    Assert.True(close.IsEnabled);
                    Assert.Equal("隐藏工具", ToolTip.GetTip(close));
                    Assert.Equal("隐藏工具", Avalonia.Automation.AutomationProperties.GetName(close));
                    Click(owner, close);
                    await Flush();
                    Assert.False(vm.VisibleItems.Single(item => item.ToolId == id).IsVisible);
                    Assert.False(vm.CanHideAll);
                    Assert.Same(prepared, adapter.PreparedView);
                    Assert.Single(context.Workspace.GetDocuments());
                }
            }
        }
        finally { service.Dispose(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task 分类编辑全局搜索与清空恢复并保持来源约束()
    {
        using var context = CreateTools(2, new());
        using var vm = CreateModel(context);
        var window = new ToolCenterWindow { DataContext = vm }; window.Show();
        try
        {
            Assert.NotNull(vm.SelectedItem);
            vm.CategoryName = "下载任务";
            vm.AddCategoryCommand.Execute(null);
            var category = vm.EditingCategory!;
            Assert.StartsWith("user:", category.Id);
            vm.SelectedItem = vm.VisibleItems.Single(item => item.ToolId == ToolId.Value);
            vm.AssignToolCategory(vm.SelectedItem, category);
            await Flush();
            vm.CategoryName = "媒体任务";
            vm.RenameCategoryCommand.Execute(null);
            await Flush();
            Assert.Equal(category.Id, vm.SelectedItem!.CategoryId);
            Assert.Equal("媒体任务", vm.SelectedItem.CategoryName);
            vm.SelectedNavigation = vm.NavigationItems.Single(item => item.Id == category.Id);
            Assert.Single(vm.VisibleItems);
            var search = window.FindControl<TextBox>("ToolSearchBox")!;
            search.Text = "文件系统";
            Assert.Equal(HostExtensionIds.FileSystemTree.Value, Assert.Single(vm.VisibleItems).ToolId);
            Assert.Equal("全部工具中的搜索结果", vm.ResultsTitle);
            vm.SelectedSource = vm.Sources.Single(item => item.Id == Owner.Value);
            Assert.True(vm.HasNoResults);
            search.Text = "";
            Assert.Equal(ToolId.Value, Assert.Single(vm.VisibleItems).ToolId);
            vm.ToggleFavoriteCommand.Execute(vm.SelectedItem);
            await Flush();
            Assert.True(vm.SelectedItem!.IsFavorite);
            vm.MoveToolFavorite(vm.SelectedItem!, -1); vm.MoveToolFavorite(vm.SelectedItem!, 1);
            vm.DeleteCategoryCommand.Execute(null);
            await Flush();
            Assert.Equal("all", vm.SelectedNavigation!.Id);
            Assert.All(vm.VisibleItems, item => Assert.Equal(ToolCenterPreferences.OtherCategoryId, item.CategoryId));
            vm.EditingCategory = vm.Categories.First();
            vm.DeleteCategoryCommand.Execute(null);
            Assert.True(vm.HasError);
            vm.CategoryName = " "; vm.AddCategoryCommand.Execute(null);
            Assert.True(vm.HasError);
            vm.SelectedNavigation = vm.NavigationItems.Single(item => item.Id == "recent");
            Assert.True(vm.HasNoResults);
            Assert.Contains("最近访问", vm.EmptyText);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task 一百个工具虚拟化搜索不重复创建视图且图标保持Owner()
    {
        var probe = new Probe();
        using var context = CreateTools(100, probe);
        Assert.Equal(100, probe.ModelsCreated);
        Assert.Equal(100, probe.ViewsCreated);
        using var vm = CreateModel(context);
        var window = new ToolCenterWindow { DataContext = vm }; window.Show();
        try
        {
            await Flush();
            Assert.Equal(102, vm.VisibleItems.Count);
            Assert.InRange(window.FindControl<ListBox>("ToolItemsList")!.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 30);
            vm.SearchText = "sample-99";
            var found = Assert.Single(vm.VisibleItems);
            Assert.Equal(Owner, found.IconRequest.OwnerId);
            Assert.NotNull(vm.Icons.Resolve(found.IconRequest).Geometry);
            vm.SearchText = "同名";
            Assert.Equal(100, vm.VisibleItems.Count);
            Assert.Equal(100, vm.VisibleItems.Select(item => item.ToolId).Distinct().Count());
            vm.SearchText = "找不到";
            Assert.True(vm.HasNoResults);
            Assert.Contains("关键词", vm.EmptyText);
            vm.SearchText = "";
            await Render(window, "tool-center-100-tools");
            Assert.Equal(100, probe.ModelsCreated);
            Assert.Equal(100, probe.ViewsCreated);
            Assert.Equal(0, probe.Disposed);
            vm.HideAllCommand.Execute(null);
            Assert.Equal(0, probe.Disposed);
            Assert.Equal(102, vm.VisibleItems.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task 可用性撤回保留解释禁用打开并且关闭会话不再订阅()
    {
        using var context = CreateTools(1, new());
        var vm = CreateModel(context);
        var original = vm.VisibleItems.Single(item => item.ToolId == ToolId.Value);
        vm.OpenToolCommand.Execute(original);
        Assert.True(vm.VisibleItems.Single(item => item.ToolId == ToolId.Value).IsVisible);
        vm.SearchText = "sample-0";
        await Task.Run(() => context.Provider.GetRequiredService<PluginLifecycleStateStore>().BeginShutdown());
        await Flush();
        var unavailable = Assert.Single(vm.VisibleItems);
        Assert.Equal("sample-0", vm.SearchText);
        Assert.False(unavailable.CanOpen);
        Assert.True(unavailable.CanHide);
        Assert.Contains("不可用", unavailable.StatusText);
        vm.OpenToolCommand.Execute(original); // 旧快照不能绕过操作边界。
        Assert.True(vm.HasError);
        vm.HideToolCommand.Execute(unavailable);
        Assert.False(Assert.Single(vm.VisibleItems).IsVisible);
        vm.Dispose();
        var snapshot = vm.VisibleItems;
        context.Workspace.ShowTool(HostExtensionIds.FileSystemTree);
        await Flush();
        Assert.Same(snapshot, vm.VisibleItems);
        vm.OpenToolCommand.Execute(original); vm.HideToolCommand.Execute(original); vm.HideAllCommand.Execute(null);
        vm.Refresh(); vm.Dispose();
        Assert.Same(snapshot, vm.VisibleItems);
    }

    [AvaloniaFact]
    public void 批量隐藏部分失败保留成功结果并显示对应工具错误()
    {
        using var context = new UiTestContext();
        using var vm = CreateModel(context);
        foreach (var id in context.Workspace.CreatedTools.Keys) context.Workspace.OpenTool(id);
        var blocked = context.Workspace.CreatedTools[HostExtensionIds.FileSystemTree.Value];
        blocked.CanClose = false; // 模拟 Dock 在提交时拒绝一个目标；不改变生产关闭政策。
        try
        {
            vm.SearchText = "插件分组菜单";
            vm.HideAllCommand.Execute(null);
            Assert.True(vm.HasError);
            Assert.Contains("1 个工具未能隐藏", vm.Error);
            Assert.Contains("文件系统浏览器", vm.Error);
            var states = context.Provider.GetRequiredService<ToolWorkspaceReadModel>().Capture();
            Assert.Equal(blocked.Id, Assert.Single(states, item => item.IsVisible).ToolId);
            Assert.Single(context.Workspace.GetDocuments());
        }
        finally { blocked.CanClose = true; }
    }

    [AvaloniaFact]
    public async Task 关闭重开释放窗口模型并由Owner退出关闭()
    {
        using var context = new UiTestContext();
        var service = context.Provider.GetRequiredService<ToolCenterWindowService>();
        Assert.False(service.CanShow);
        service.ShowOrActivate(); Assert.Null(service.CurrentWindow);
        var owner = new Window(); owner.Show(); service.Attach(owner); service.Attach(owner);
        Assert.Throws<InvalidOperationException>(() => service.Attach(new Window()));
        for (var index = 0; index < 8; index++)
        {
            service.ShowOrActivate();
            var window = service.CurrentWindow!;
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await Flush();
            Assert.Null(service.CurrentWindow);
            Assert.Null(window.DataContext);
            Assert.True(owner.IsEnabled);
        }
        service.ShowOrActivate();
        var last = service.CurrentWindow!;
        owner.Close(); await Flush();
        Assert.Null(service.CurrentWindow);
        Assert.Null(last.DataContext);
        Assert.False(service.CanShow);
        service.ShowOrActivate(); service.Dispose();
    }

    [AvaloniaFact]
    public async Task 命令面板工具使用独立身份打开隐藏或自动收起项并记录访问()
    {
        using var context = new UiTestContext();
        var owner = new MainWindow { DataContext = context.ViewModel }; owner.Show();
        try
        {
            var palette = context.Provider.GetRequiredService<WorkbenchCommandPresentation>().Palette;
            var preferences = context.Provider.GetRequiredService<ToolCenterPreferences>();
            var entry = Assert.Single(palette.GetItems("file-system-tree"));
            Assert.Null(entry.CommandId);
            Assert.Equal(HostExtensionIds.FileSystemTree, entry.ToolTypeId);
            Assert.StartsWith("tool:", entry.StableKey);
            entry.Command.Execute(null);
            await Flush();
            Assert.Equal([entry.ToolTypeId!.Value], preferences.Current.RecentToolIds);
            var tool = context.Workspace.CreatedTools[entry.ToolTypeId.Value];
            context.Workspace.DockFactory.PinDockable(tool);
            entry.Command.Execute(null);
            Assert.Same(tool, context.Workspace.DockFactory.FindRoot(tool, _ => true)!.PinnedDock?.ActiveDockable);
            Assert.True(entry.Command.CanExecute(null));
            context.Workspace.BeginShutdown();
            Assert.False(entry.Command.IsEnabled);
            entry.Command.Execute(null);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public void 旧布局定向迁移保留有效工具且新增工具默认隐藏并可全隐藏重启()
    {
        var old = new DockLayoutSnapshotV2
        {
            ActiveToolId = RetiredHostToolIds.PluginStatus,
            Tools = [new() { Id = RetiredHostToolIds.ToolManagement, DockId = DockLayoutIds.RightTools, Order = 0, IsVisible = true },
                new() { Id = HostExtensionIds.PluginMenu.Value, DockId = DockLayoutIds.RightTools, Order = 1, IsVisible = true, IsPinned = true },
                new() { Id = RetiredHostToolIds.PluginStatus, DockId = DockLayoutIds.RightTools, Order = 2, IsVisible = true, IsPinned = true }]
        };
        DockLayoutSnapshotV2 hidden;
        using (var context = new UiTestContext(initialLayout: old))
        {
            context.ViewModel.ApplyPendingLayout();
            var states = context.Provider.GetRequiredService<ToolWorkspaceReadModel>().Capture();
            Assert.True(states.Single(item => item.ToolId == HostExtensionIds.PluginMenu.Value).IsVisible);
            Assert.False(states.Single(item => item.ToolId == HostExtensionIds.FileSystemTree.Value).IsVisible);
            Assert.DoesNotContain(RetiredHostToolIds.ToolManagement, context.Workspace.CreatedTools.Keys);
            Assert.DoesNotContain(RetiredHostToolIds.PluginStatus, context.Workspace.CreatedTools.Keys);
            context.Workspace.HideAllTools();
            context.Provider.GetRequiredService<DockLayoutLifecycle>().Save(context.Workspace);
            Assert.Single(Directory.GetFiles(context.TempDirectory, "*.pre-tool-retirement.bak"));
            hidden = new DockLayoutStore(context.LayoutPath).Load()!;
        }
        using var restarted = new UiTestContext(initialLayout: hidden);
        restarted.ViewModel.ApplyPendingLayout();
        Assert.All(restarted.Provider.GetRequiredService<ToolWorkspaceReadModel>().Capture(), item => Assert.False(item.IsVisible));
        Assert.True(restarted.Workspace.OpenTool(HostExtensionIds.PluginMenu.Value).Succeeded);
    }

    private static UiTestContext CreateTools(int count, Probe probe) => new(modules:
        PluginModuleCatalog.CreateForTests([(Owner, (IPluginModule)new ExampleModule(count, probe))]));

    private sealed class ExampleModule(int count, Probe probe) : IPluginModule
    {
        public void Configure(IPluginRegistration registration)
        {
            registration.Services.AddSingleton(probe);
            var icon = registration.AddIcon("tool", new("M1,1 H15 V15 H1 Z", 16, 16));
            var marker = typeof(int);
            var add = typeof(IPluginRegistration).GetMethod("AddTool")!;
            for (var index = 0; index < count; index++)
            {
                marker = typeof(List<>).MakeGenericType(marker);
                add.MakeGenericMethod(typeof(ExampleTool<>).MakeGenericType(marker), typeof(ExampleView<>).MakeGenericType(marker))
                    .Invoke(registration, [new ToolDescriptor(new($"{Owner.Value}.tool.sample-{index}"), "同名工具：较长的业务任务与内容处理",
                        "用于验证业务工具状态保持和目录扩展。", ToolDockSide.Right, ToolCloseBehavior.Prevent, icon)]);
            }
        }
    }

    private static ToolCenterViewModel CreateModel(UiTestContext context) => new(
        context.Provider.GetRequiredService<ToolCenterQuery>(), context.Provider.GetRequiredService<ToolCenterPreferences>(),
        context.Provider.GetRequiredService<ToolCenterActions>(), context.Workspace,
        context.Provider.GetRequiredService<PluginAvailabilityReadModel>(), context.Provider.GetRequiredService<HostIconRenderer>());
    private static Button RowButton(Window window, string id, string content) => Assert.Single(window.GetVisualDescendants().OfType<Button>(),
        button => button.DataContext is ToolCenterItem item && item.ToolId == id && Equals(button.Content, content));
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static async Task Flush() { await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); await Task.Delay(30); }
    private static async Task Render(Window window, string name)
    {
        await Flush();
        var directory = Environment.GetEnvironmentVariable("MYAVALONIA_V7_RENDER_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    public sealed class Probe { public int ModelsCreated; public int ViewsCreated; public int Disposed; }
    public sealed class ExampleTool<T> : IDisposable
    {
        public Probe Probe { get; }
        public ExampleTool(Probe probe) { Probe = probe; probe.ModelsCreated++; }
        public void Dispose() => Probe.Disposed++;
    }
    public sealed class ExampleView<T> : UserControl
    {
        public ExampleView() { DataContextChanged += (_, _) => { if (DataContext is ExampleTool<T> tool) tool.Probe.ViewsCreated++; }; }
    }
}
