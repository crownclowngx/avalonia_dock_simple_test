using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.Search;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.Design;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>键盘会话使用可控只读投影验证边界，实例执行使用真实插件、生产投影和共享 Executor。</summary>
public sealed class CommandPaletteV18UiTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.v18-ui");
    private static readonly DocumentTypeId TypeId = new($"{Owner.Value}.document.test");
    private static readonly CommandId RunId = new($"{Owner.Value}.command.run");

    [AvaloniaFact]
    public async Task 默认跨禁用项选择可用结果且全部禁用和空查询结果不误执行()
    {
        var binding = new RecordingCommand();
        var disabled = Item("a", binding) with { IsEnabled = false, MatchRank = 0 };
        var enabled = Item("b", binding) with { MatchRank = 1 };
        var source = new MutableProjection(disabled, enabled);
        using var session = new PaletteSession(source);
        Assert.Equal(enabled.StableKey, session.Selected.StableKey);
        session.List.SelectedIndex = 0;
        session.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await session.View.CurrentExecution;
        Assert.Equal(0, binding.Executions);
        Assert.Contains("当前状态下不可用", session.Hint.Text);
        source.Set([disabled]);
        Assert.Equal(0, session.List.SelectedIndex);
        session.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await session.View.CurrentExecution;
        Assert.Equal(0, binding.Executions);
        session.Search.Text = "无匹配项";
        await Flush();
        Assert.Equal(-1, session.List.SelectedIndex);
        Assert.Equal(string.Empty, session.Hint.Text);
        Assert.True(session.View.FindControl<TextBlock>("EmptyState")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task 组标题不增加键盘步骤或执行入口且中文预编辑回车只交给输入控件()
    {
        var design = new WorkbenchCommandPresentationDesignData();
        using var session = new PaletteSession(new MutableProjection(design.Palette.GetItems("").ToArray()));
        await Flush();
        Assert.Equal(6, session.List.ItemCount);
        var headers = session.View.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("palette-group") && text.IsVisible).ToArray();
        Assert.Equal(4, headers.Length);
        Assert.All(headers, header => Assert.False(header.Focusable));
        session.List.SelectedIndex = 1;
        session.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.IsType<FunctionPaletteIdentity>(session.Selected.Identity);
        Assert.Equal(2, session.List.SelectedIndex);
        Assert.Contains("新开", session.Hint.Text);
        var closed = 0;
        session.View.CloseRequested += (_, _) => closed++;
        // 先选择可执行命令，再真实双击组标题，不能把双击转交给旧选择。
        session.List.SelectedIndex = 4;
        var point = headers[0].TranslatePoint(new Point(12, 5), session.Window)!.Value;
        session.Window.MouseDown(point, MouseButton.Left);
        session.Window.MouseUp(point, MouseButton.Left);
        session.Window.MouseDown(point, MouseButton.Left);
        session.Window.MouseUp(point, MouseButton.Left);
        Assert.Equal(0, closed);
        session.List.SelectedIndex = 2;
        session.Search.Focus();
        var presenter = Assert.Single(session.Search.GetVisualDescendants().OfType<TextPresenter>());
        presenter.PreeditText = "huan";
        session.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        session.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(0, closed);
        Assert.Equal(2, session.List.SelectedIndex);
        presenter.PreeditText = null;
        session.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.IsType<ToolPaletteIdentity>(session.Selected.Identity);
        session.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(1, closed);
    }

    [AvaloniaFact]
    public async Task 状态刷新保留身份顺序和滚动而查询更改采用新的默认项()
    {
        var binding = new RecordingCommand();
        var items = Enumerable.Range(0, 100).Select(index => Item($"item-{index:D3}", binding)).ToArray();
        var source = new MutableProjection(items);
        using var session = new PaletteSession(source);
        session.List.SelectedIndex = 80;
        session.List.ScrollIntoView(session.List.SelectedItem!);
        await Flush();
        var key = session.Selected.StableKey;
        var scroll = session.List.GetVisualDescendants().OfType<ScrollViewer>().First();
        var offset = scroll.Offset;
        Assert.True(offset.Y > 0);
        Assert.True(session.List.GetVisualDescendants().OfType<ListBoxItem>().Count() < 100);
        source.Set(items.Select(item => item with { IsEnabled = item.StableKey != key }).ToArray());
        await Flush();
        Assert.Equal(key, session.Selected.StableKey);
        Assert.Equal(80, session.List.SelectedIndex);
        Assert.Equal(offset, scroll.Offset);
        Assert.Contains("当前状态下不可用", session.Hint.Text);
        session.Search.Text = "item-";
        await Flush();
        Assert.Equal(0, session.List.SelectedIndex);
        Assert.True(session.Selected.IsEnabled);
    }

    [AvaloniaFact]
    public async Task 选中身份在执行前消失时本次回车不执行自动补选项且错误不被底部提示覆盖()
    {
        var binding = new RecordingCommand();
        var original = Item("a", binding);
        var replacement = Item("b", binding);
        var source = new MutableProjection(original, replacement);
        using var session = new PaletteSession(source);
        // 模拟执行与已排队刷新竞争：业务事实先消失，当前可见行尚未收到 Changed。
        source.Set([replacement], notify: false);
        session.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await session.View.CurrentExecution;
        Assert.Equal(0, binding.Executions);
        Assert.Equal(replacement.StableKey, session.Selected.StableKey);
        Assert.Contains("原目标已不可用", session.View.FindControl<TextBlock>("OperationStatus")!.Text);
        Assert.Contains("执行：b", session.Hint.Text);
        session.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await session.View.CurrentExecution;
        Assert.Equal(1, binding.Executions);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 页面命令使用显示的目标且关闭面板瞬间切页也不会误执行(bool switchDuringClose)
    {
        using var context = CreateContext();
        var window = new MainWindow { DataContext = context.ViewModel };
        window.Show();
        try
        {
            var first = await context.Workspace.CreateAndPublishDocumentAsync(TypeId, new NewDocumentActivation("同名页面"));
            var second = await context.Workspace.CreateAndPublishDocumentAsync(TypeId, new NewDocumentActivation("同名页面"));
            context.Workspace.TryActivatePage(first.PageId);
            window.OpenCommandPalette();
            var view = window.FindControl<CommandPaletteView>("CommandPaletteHost")!;
            view.FindControl<TextBox>("SearchBox")!.Text = "打开小说项目";
            await Flush();
            var list = view.FindControl<ListBox>("PaletteItems")!;
            var original = Assert.IsType<WorkbenchCommandPaletteProjectionEntry>(list.SelectedItem);
            Assert.Equal(first.PageId, original.ExpectedTarget?.PageId);
            Assert.Contains("同名页面", original.TargetText);
            Assert.Contains("页面 2", original.TargetText);
            Assert.Equal("Enter 执行：打开小说项目", view.FindControl<TextBlock>("SelectionHint")!.Text);
            if (switchDuringClose)
                view.CloseRequested += (_, _) => context.Workspace.TryActivatePage(second.PageId);
            else
                context.Workspace.TryActivatePage(second.PageId);
            // 不排空 Dispatcher，以旧行真实模拟尚未投影的新目标；该提交必须拒绝。
            await view.ExecuteSelectionAsync();
            Assert.Equal(0, Assert.IsType<TestDocument>(first.Model).Executions);
            Assert.Equal(0, Assert.IsType<TestDocument>(second.Model).Executions);
            if (!switchDuringClose)
            {
                Assert.True(window.FindControl<Border>("CommandPaletteLayer")!.IsVisible);
                Assert.Equal(second.PageId, Assert.IsType<WorkbenchCommandPaletteProjectionEntry>(list.SelectedItem).ExpectedTarget?.PageId);
                await view.ExecuteSelectionAsync();
                Assert.Equal(1, Assert.IsType<TestDocument>(second.Model).Executions);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task 真实保存携带页面约束而全局入口无页面约束并保留创建意图()
    {
        using var context = CreateContext();
        var window = new MainWindow { DataContext = context.ViewModel };
        window.Show();
        try
        {
            var page = await context.Workspace.CreateAndPublishDocumentAsync(TypeId, new NewDocumentActivation("项目A"));
            context.Workspace.TryActivatePage(page.PageId);
            var palette = context.ViewModel.WorkbenchCommands.Palette;
            var items = palette.GetItems("");
            var save = Assert.Single(items, item => item.CommandId == HostWorkbenchCommandIds.SaveDocument);
            Assert.Equal(page.PageId, save.ExpectedTarget?.PageId);
            Assert.Equal("当前页面不支持保存", save.DisabledText);
            Assert.Contains("项目A", save.TargetText);
            foreach (var id in new[] { HostWorkbenchCommandIds.OpenDocument, HostWorkbenchCommandIds.NewDocument,
                HostWorkbenchCommandIds.OpenHelp, HostWorkbenchCommandIds.OpenToolCenter, HostWorkbenchCommandIds.OpenPluginStatus })
                Assert.Null(Assert.Single(items, item => item.CommandId == id).ExpectedTarget);
            var intents = items.Where(item => item.Identity is FunctionPaletteIdentity { DocumentTypeId: var id } && id == TypeId).ToArray();
            Assert.Equal(2, intents.Length);
            Assert.Equal(2, intents.Select(item => item.StableKey).Distinct().Count());
            Assert.All(intents, item => Assert.Equal("＋", item.LeadingAction));
            var model = Assert.IsType<TestDocument>(page.Model);
            model.Enabled = false;
            var command = Assert.Single(palette.GetItems("打开小说项目"));
            Assert.Equal("当前状态下不可用", command.DisabledText);
            Assert.DoesNotContain("正在运行", command.ContextText);
            var builtinSources = palette.GetItems("主程序");
            Assert.Contains(builtinSources, item => item.Identity is PagePaletteIdentity);
            Assert.Contains(builtinSources, item => item.Identity is FunctionPaletteIdentity);
            Assert.Contains(builtinSources, item => item.Identity is ToolPaletteIdentity);
            Assert.Contains(builtinSources, item => item.Identity is CommandPaletteIdentity);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("link", "链接下载")]
    [InlineData("profile", "个人内容来源")]
    public async Task 同类型两个创建入口按原意图各自新建而不会复用同名已有页面(string intent, string name)
    {
        using var context = CreateContext();
        var window = new MainWindow { DataContext = context.ViewModel };
        window.Show();
        try
        {
            var existing = await context.Workspace.CreateAndPublishDocumentAsync(TypeId, new NewDocumentActivation(name));
            window.OpenCommandPalette();
            var view = window.FindControl<CommandPaletteView>("CommandPaletteHost")!;
            view.FindControl<TextBox>("SearchBox")!.Text = name;
            await Flush();
            var list = view.FindControl<ListBox>("PaletteItems")!;
            Assert.IsType<PagePaletteIdentity>(Assert.IsType<WorkbenchCommandPaletteProjectionEntry>(list.SelectedItem).Identity);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.IsType<FunctionPaletteIdentity>(Assert.IsType<WorkbenchCommandPaletteProjectionEntry>(list.SelectedItem).Identity);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await view.CurrentExecution;
            var created = Assert.Single(context.Workspace.GetDocuments(), page => page.PageId != existing.PageId && page.Model is TestDocument);
            Assert.Equal(new CreationIntentId(intent), Assert.IsType<TestDocument>(created.Model).Intent);
            Assert.NotEqual(existing.PageId, created.PageId);
            Assert.Null(Assert.IsType<TestDocument>(existing.Model).Intent);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 800)]
    [InlineData(true, 640)]
    public async Task 长名称多实例主题截图保留序号来源和完整可访问目标(bool dark, int width)
    {
        var design = new WorkbenchCommandPresentationDesignData();
        var entries = design.Palette.GetItems("").Select(item => item.Identity is PagePaletteIdentity
            ? item with { DisplayName = "用于验证长中文标题不会挤掉实例序号的欢迎页面", SourceText = "测试插件 myavalonia.plugin.long-source", ExecuteHint = "回到“用于验证长中文标题不会挤掉实例序号的欢迎页面”（" + item.InstanceText + "）" }
            : item).ToArray();
        using var session = new PaletteSession(new MutableProjection(entries), width);
        session.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        await Flush();
        var row = Assert.IsType<ListBoxItem>(session.List.ContainerFromIndex(0));
        var selected = Assert.Single(row.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("palette-row"));
        Assert.True(Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(selected.Background).Color.A > 0);
        var accessibleName = AutomationProperties.GetName(row);
        Assert.Contains("页面 1", accessibleName);
        Assert.Contains("myavalonia.plugin.long-source", accessibleName);
        Assert.Contains("长中文标题", accessibleName);
        Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "页面 1 · 当前" && text.Bounds.Width > 0);
        Assert.True(session.View.Bounds.Width <= session.Window.ClientSize.Width);
        Assert.True(session.Hint.Bounds.Bottom <= session.View.Bounds.Height);
        var directory = MyAvaloniaManagement.Testing.TestEvidenceDirectory.Optional("command-palette", "MYAVALONIA_V18_RENDER_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using var bitmap = session.Window.CaptureRenderedFrame();
            Assert.NotNull(bitmap);
            bitmap.Save(Path.Combine(directory, dark ? "palette-v18-dark.png" : "palette-v18-light.png"), PngBitmapEncoderOptions.Default);
        }
    }

    private static Task Flush() => UiTestWait.DrainAsync();
    private static WorkbenchCommandPaletteProjectionEntry Item(string name, RecordingCommand binding) =>
        new(new CommandPaletteIdentity(new($"myavalonia.test.command.{name}")), name, "说明", "", true, binding) { SourceText = "测试" };

    /// <summary>替身只控制候选快照与通知，不替换 View 的排序、选择、键盘和渲染逻辑。</summary>
    private sealed class MutableProjection(params WorkbenchCommandPaletteProjectionEntry[] entries) : IWorkbenchCommandPaletteProjection
    {
        public event EventHandler? Changed;
        public DocumentCreationTarget? CaptureCreationTarget(IRootDock? source) => null;
        public IReadOnlyList<WorkbenchCommandPaletteProjectionEntry> GetItems(string? query) => WorkbenchPaletteOrdering.Sort(
            entries.Where(item => WorkbenchTextMatch.Rank(item.SearchName, query?.Trim() ?? string.Empty) < int.MaxValue));
        internal void Set(WorkbenchCommandPaletteProjectionEntry[] next, bool notify = true)
        {
            entries = next;
            if (notify) Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Bindings(IWorkbenchCommandPaletteProjection palette) : IWorkbenchCommandPresentationBindings
    {
        private readonly WorkbenchCommandPresentationDesignData _design = new();
        public IWorkbenchMenuProjection Menu => _design.Menu;
        public IWorkbenchKeyBindingProjection KeyBindings => _design.KeyBindings;
        public IWorkbenchCommandPaletteProjection Palette => palette;
    }

    private sealed class PaletteSession : IDisposable
    {
        internal PaletteSession(MutableProjection source, int width = 800)
        {
            View = new() { DataContext = new Bindings(source), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            Window = new() { Content = View, Width = width, Height = 650 };
            Window.Show();
            View.BeginSession();
            Window.UpdateLayout();
            // 初始空文本通知和延迟焦点属于打开会话；在模拟用户选中/执行前先提交这批 UI 工作。
            Dispatcher.UIThread.RunJobs();
        }
        internal Window Window { get; }
        internal CommandPaletteView View { get; }
        internal ListBox List => View.FindControl<ListBox>("PaletteItems")!;
        internal TextBox Search => View.FindControl<TextBox>("SearchBox")!;
        internal TextBlock Hint => View.FindControl<TextBlock>("SelectionHint")!;
        internal WorkbenchCommandPaletteProjectionEntry Selected => Assert.IsType<WorkbenchCommandPaletteProjectionEntry>(List.SelectedItem);
        public void Dispose() => Window.Close();
    }

    private sealed class RecordingCommand : IWorkbenchPresentationCommandBinding
    {
        public bool IsEnabled => true;
        internal int Executions { get; private set; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Executions++;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    private static UiTestContext CreateContext() => new(modules: PluginModuleCatalog.CreateForTests([(Owner, (IPluginModule)new Module())]));

    private sealed class Module : IPluginModule
    {
        public void Configure(IPluginRegistration registration)
        {
            registration.AddDocument<TestDocument, UserControl>(new(TypeId, "项目", "项目入口", "测试", creationIntents:
                [new(new("link"), "链接下载"), new(new("profile"), "个人内容来源")]));
            registration.AddDocumentCommand(new(RunId, "打开小说项目", "对当前页面执行，不创建新标签"), TypeId);
            registration.AddMenuCommandContribution(new(new($"{Owner.Value}.command-placement.run"), RunId,
                WorkbenchMenuLocations.ToolsShared, "document", 0, MenuCommandTargetUnavailableBehavior.Hide));
        }
    }

    /// <summary>每个页面有独立计数；命令名称故意包含“打开”，检验 Host 不从动词推断新建语义。</summary>
    private sealed class TestDocument : IPluginDocument, IWorkbenchDocumentCommandTarget
    {
        public DocumentPresentationState Presentation { get; private set; } = new("项目");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public event EventHandler<WorkbenchCommandStateChangedEventArgs>? CommandStateChanged { add { } remove { } }
        internal bool Enabled { get; set; } = true;
        internal int Executions { get; private set; }
        internal CreationIntentId? Intent { get; private set; }
        public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
        {
            Presentation = new(activation is NewDocumentActivation created ? created.Title ?? "项目" : "项目");
            Intent = (activation as NewDocumentActivation)?.CreationIntentId;
            return ValueTask.CompletedTask;
        }
        public bool CanExecute(CommandId commandId) => Enabled && commandId == RunId;
        public ValueTask ExecuteAsync(CommandId commandId, CancellationToken cancellationToken)
        {
            Executions++;
            return ValueTask.CompletedTask;
        }
    }
}
