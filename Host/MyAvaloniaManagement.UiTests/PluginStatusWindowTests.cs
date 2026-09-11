using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.PluginStatus;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.Models.Plugins;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.PluginStatus;
using MyAvaloniaManagement.Views.PluginStatus;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>通过生产命令投影、窗口服务和真实控件验证交互；使用无桌面的 Skia 渲染检查布局。</summary>
public sealed class PluginStatusWindowTests
{
    [AvaloniaFact]
    public async Task 菜单和命令面板打开同一非模态窗口且不产生工具或文档()
    {
        using var context = new UiTestContext();
        var service = context.Provider.GetRequiredService<PluginStatusWindowService>();
        var owner = new Window();
        service.Attach(owner);
        owner.Show();
        try
        {
            context.Workspace.HideAllTools();
            var documents = context.Workspace.GetDocuments().ToArray();
            var tools = context.Workspace.CreatedTools.Keys.ToArray();
            var presentation = context.Provider.GetRequiredService<WorkbenchCommandPresentation>();
            var menu = Assert.Single(presentation.Menu.GetItems(WorkbenchMenuLocations.ToolsShared)
                .OfType<WorkbenchMenuCommandProjectionEntry>(), item => item.CommandId == HostWorkbenchCommandIds.OpenPluginStatus);
            Assert.True(menu.Command.IsEnabled);
            menu.Command.Execute(null);
            await Flush();
            var window = Assert.IsType<PluginStatusWindow>(service.CurrentWindow);
            var model = Assert.IsType<PluginStatusWindowViewModel>(window.DataContext);
            Assert.Same(owner, window.Owner);
            Assert.False(window.IsDialog);
            Assert.True(owner.IsEnabled);
            Assert.True(model.HasNoResults);
            Assert.False(window.FindControl<Button>("CopyDiagnosticsButton")!.IsEnabled);
            Assert.True(window.FindControl<TextBox>("PluginSearchBox")!.IsFocused);
            window.WindowState = WindowState.Minimized;
            var entry = Assert.Single(presentation.Palette.GetItems("插件状态"));
            Assert.Equal(HostWorkbenchCommandIds.OpenPluginStatus, entry.CommandId);
            Assert.Null(entry.ToolTypeId);
            entry.Command.Execute(null);
            await Flush();
            Assert.Same(window, service.CurrentWindow);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(documents, context.Workspace.GetDocuments());
            Assert.Equal(tools, context.Workspace.CreatedTools.Keys);
            Assert.DoesNotContain(RetiredHostToolIds.PluginStatus, tools);
            Assert.DoesNotContain(context.Provider.GetRequiredService<ToolCenterQuery>().Capture(), item => item.ToolId == RetiredHostToolIds.PluginStatus);
            context.Workspace.BeginShutdown();
            Assert.False(service.CanShow);
            Assert.False(menu.Command.IsEnabled);
        }
        finally { service.Dispose(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task 搜索筛选刷新和复制通过真实控件工作并验证深浅主题与窄窗口()
    {
        var query = new MutableQuery { Items = SampleItems() };
        using var context = new UiTestContext((services, _) => services.AddSingleton<IPluginStatusQuery>(query));
        var service = context.Provider.GetRequiredService<PluginStatusWindowService>();
        var owner = new Window(); owner.Show(); service.Attach(owner);
        try
        {
            service.ShowOrActivate();
            await Flush();
            var window = service.CurrentWindow!;
            var model = Assert.IsType<PluginStatusWindowViewModel>(window.DataContext);
            var search = window.FindControl<TextBox>("PluginSearchBox")!;
            var filter = window.FindControl<ComboBox>("PluginFilterBox")!;
            var list = window.FindControl<ListBox>("PluginItemsList")!;
            Assert.Equal(2, list.ItemCount);
            await Render(window, "plugin-status-light");
            window.RequestedThemeVariant = ThemeVariant.Dark;
            await Render(window, "plugin-status-dark");
            window.Width = 680; window.Height = 520;
            await Render(window, "plugin-status-compact");
            Assert.True(list.Bounds.Width > 170);
            Assert.True(window.FindControl<ScrollViewer>("PluginDetailsScroll")!.Bounds.Width > 240);
            Assert.True(window.FindControl<Button>("CopyDiagnosticsButton")!.IsEffectivelyVisible);
            search.Text = "  LOAD_FAILED  ";
            await Flush();
            Assert.Equal(1, list.ItemCount);
            Assert.Equal("failed", model.SelectedItem!.Key);
            filter.SelectedItem = "可用";
            await Flush();
            Assert.Equal(0, list.ItemCount);
            Assert.False(model.HasSelection);
            filter.SelectedItem = "异常";
            await Flush();
            query.Items = [query.Items[0] with { VersionText = "2.0.0" }, query.Items[1]];
            Click(window, window.FindControl<Button>("RefreshButton")!);
            await Flush();
            Assert.Equal("2.0.0", model.SelectedItem!.VersionText);
            Assert.Equal("  LOAD_FAILED  ", model.SearchText);
            Click(window, window.FindControl<Button>("CopyDiagnosticsButton")!);
            await Flush();
            Assert.Equal(model.CreateDiagnosticText(), await window.Clipboard!.TryGetTextAsync());
            Assert.Contains("已复制", model.CopyFeedback);
            Assert.Contains("LOAD_FAILED", model.CreateDiagnosticText());
            window.Width = 1040; window.Height = 720;
            window.RequestedThemeVariant = ThemeVariant.Light;
            window.FindControl<ScrollViewer>("PluginDetailsScroll")!.Offset = new Vector(0, 800);
            await Render(window, "plugin-status-diagnostics");
            Assert.True(window.FindControl<ScrollViewer>("PluginDetailsScroll")!.Extent.Height > 500);
        }
        finally { service.Dispose(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task 重开取消Owner关闭和Runtime释放均遵循窗口所有权且各Runtime隔离()
    {
        using var first = new UiTestContext();
        using var second = new UiTestContext();
        var service = first.Provider.GetRequiredService<PluginStatusWindowService>();
        var other = second.Provider.GetRequiredService<PluginStatusWindowService>();
        Assert.False(service.CanShow);
        service.ShowOrActivate(); Assert.Null(service.CurrentWindow);
        var owner = new Window(); owner.Show(); service.Attach(owner); service.Attach(owner);
        var otherOwner = new Window(); otherOwner.Show(); other.Attach(otherOwner);
        try
        {
            Assert.Throws<InvalidOperationException>(() => service.Attach(otherOwner));
            for (var index = 0; index < 3; index++)
            {
                service.ShowOrActivate();
                var window = service.CurrentWindow!;
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                await Flush();
                Assert.Null(service.CurrentWindow);
                Assert.Null(window.DataContext);
            }
            service.ShowOrActivate(); other.ShowOrActivate();
            Assert.NotSame(service.CurrentWindow, other.CurrentWindow);
            EventHandler<WindowClosingEventArgs> cancel = (_, args) => args.Cancel = true;
            owner.Closing += cancel;
            owner.Close();
            Assert.True(service.CanShow);
            Assert.NotNull(service.CurrentWindow);
            owner.Closing -= cancel;
            var last = service.CurrentWindow!;
            owner.Close(); await Flush();
            Assert.Null(service.CurrentWindow);
            Assert.Null(last.DataContext);
            Assert.True(other.CurrentWindow!.IsVisible);
            Assert.False(service.CanShow);
            other.Dispose();
            Assert.Null(other.CurrentWindow);
            Assert.False(other.CanShow);
        }
        finally { service.Dispose(); other.Dispose(); owner.Close(); otherOwner.Close(); }
    }

    [AvaloniaFact]
    public async Task 大目录使用虚拟化且重激活读取最新数据并尊重命令取消()
    {
        var query = new MutableQuery { Items = Enumerable.Range(0, 100).Select(index => SampleItems()[1] with
            { Key = "plugin:" + index, PluginId = $"myavalonia.plugin.sample-{index}" }).ToArray() };
        using var context = new UiTestContext((services, _) => services.AddSingleton<IPluginStatusQuery>(query));
        var service = context.Provider.GetRequiredService<PluginStatusWindowService>();
        var owner = new Window(); owner.Show(); service.Attach(owner);
        try
        {
            var handler = context.Provider.GetRequiredService<HostOpenPluginStatusCommandHandler>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.ExecuteAsync(new CancellationToken(true)).AsTask());
            Assert.Null(service.CurrentWindow);
            await handler.ExecuteAsync(CancellationToken.None);
            await Flush();
            var window = service.CurrentWindow!;
            var model = (PluginStatusWindowViewModel)window.DataContext!;
            var list = window.FindControl<ListBox>("PluginItemsList")!;
            Assert.Equal(100, list.ItemCount);
            Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 30);
            model.SelectedItem = model.VisibleItems[4];
            query.Items = query.Items.Select(item => item with { VersionText = "3.0.0" }).ToArray();
            service.ShowOrActivate();
            Assert.Equal("plugin:4", model.SelectedItem!.Key);
            Assert.Equal("3.0.0", model.SelectedItem.VersionText);
            Assert.Same(window, service.CurrentWindow);
        }
        finally { service.Dispose(); owner.Close(); }
    }

    private static IReadOnlyList<PluginStatusItem> SampleItems() =>
    [
        new("myavalonia.plugin.document-analyzer", "DocumentAnalyzer.Plugin", "生命周期初始化失败", "152 ms", "已隔离",
            "插件初始化没有完成，相关贡献尚未开放。请根据下方诊断记录检查插件配置和兼容性。")
        {
            Key = "failed", VersionText = "1.8.2", HasProblem = true, CompatibilityText = "Plugin SDK [3.0.0, 4.0.0)",
            Contributions = [new("文档", "文档分析", "myavalonia.plugin.document-analyzer.document.main", "已声明 · 插件当前不可用")],
            Diagnostics = [new("2026-09-11 10:28:35.124", "错误", "生命周期", "LOAD_FAILED",
                string.Concat(Enumerable.Repeat("插件依赖检查未完成，请检查配置后重新启动宿主。", 8)), "stage=Initialization; durationMs=152")]
        },
        new("myavalonia.plugin.workspace-notes", "WorkspaceNotes.Plugin", "已加载 · 无需后台生命周期", "—", "可用", "插件模块已完成服务注册。")
        { Key = "ready", VersionText = "1.0.0", IsAvailable = true, CompatibilityText = "Plugin SDK [3.0.0, 4.0.0)" }
    ];

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static async Task Flush()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(30);
    }
    private static async Task Render(Window window, string name)
    {
        await Flush();
        var directory = Environment.GetEnvironmentVariable("MYAVALONIA_PLUGIN_STATUS_RENDER_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private sealed class MutableQuery : IPluginStatusQuery
    {
        public IReadOnlyList<PluginStatusItem> Items { get; set; } = [];
        public IReadOnlyList<PluginStatusItem> Capture() => Items;
    }
}
