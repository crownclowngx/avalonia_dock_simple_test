using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// V16 使用真实插件注册、Scope 和视图，只替换保存选择及初始化等待等外部边界。
/// 每个夹具拥有自己的窗口与临时目录，生产类型不增加测试开关。
/// </summary>
internal sealed class DocumentWindowTestContext : IAsyncDisposable
{
    internal static readonly PluginId Plugin = new("myavalonia.plugin.v16-tests");
    internal static readonly DocumentTypeId Type = new($"{Plugin.Value}.document.editor");
    internal Probe State { get; } = new();
    internal UiTestContext Context { get; }
    internal MainWindow Main { get; }
    internal MyAvaloniaManagement.Business.Workspace.WorkspaceSession Workspace => Context.Workspace;

    internal DocumentWindowTestContext()
    {
        Context = new UiTestContext((services, _) => services.AddSingleton<IDocumentInteractionService>(State),
            modules: PluginModuleCatalog.CreateForTests([(Plugin, (IPluginModule)new Module(State))]));
        Main = new MainWindow(Workspace.DockFactory.WindowContext)
            { DataContext = Context.ViewModel, Width = 1000, Height = 700 };
        Main.Show();
    }

    internal async Task<ManagedDocumentDockable> Create(string title = "V16 页面") =>
        await Workspace.CreateAndPublishDocumentAsync(Type, new NewDocumentActivation(title));

    internal async Task<HostFloatingWindow> Float(ManagedDocumentDockable document)
    {
        Workspace.DockFactory.FloatDockable(document);
        await Flush();
        return Assert.IsType<HostFloatingWindow>(DockTreeNavigator.FindWindow(Workspace.RootDock!, document)!.Host);
    }

    internal static async Task Flush() =>
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

    internal static async Task<CommandPaletteView> OpenPalette(Window window)
    {
        window.Activate();
        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        await Flush();
        var palette = window.GetVisualDescendants().OfType<CommandPaletteView>().Single();
        palette.FindControl<TextBox>("SearchBox")!.Text = "V16 新建";
        await Flush();
        var list = palette.FindControl<ListBox>("PaletteItems")!;
        list.SelectedItem = Assert.Single(list.Items.OfType<WorkbenchCommandPaletteProjectionEntry>(),
            entry => entry.Identity is FunctionPaletteIdentity);
        return palette;
    }

    internal static async Task ClickClose(Window window, ManagedDocumentDockable document)
    {
        // 只等待按钮实际布局完成，动作仅发送一次；等待上限防止模板缺失造成测试挂起。
        Button? button = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            window.UpdateLayout();
            button = window.GetVisualDescendants().OfType<DocumentTabStripItem>()
                .SingleOrDefault(item => ReferenceEquals(item.DataContext, document))?
                .GetVisualDescendants().OfType<Button>().FirstOrDefault(item => ReferenceEquals(item.CommandParameter, document)
                    && item.IsEffectivelyVisible && item.Bounds.Width > 0 && item.Bounds.Height > 0);
            if (button is { Bounds.Width: > 0, Bounds.Height: > 0 }) break;
            await Task.Delay(10);
        }
        Assert.NotNull(button);
        Assert.True(button.Bounds.Width > 0 && button.IsEffectivelyVisible);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await Flush();
    }

    public async ValueTask DisposeAsync()
    {
        State.Blocker?.TrySetResult();
        State.PendingChoice?.TrySetResult(DocumentCloseChoice.Discard);
        State.Choice = DocumentCloseChoice.Discard;
        foreach (var model in State.Models) model.Clean();
        Main.Close();
        await Flush();
        // 失败回归可能留下已经脱离模型的原生空壳；只清理本夹具借用过的窗口。
        foreach (var window in Workspace.DockFactory.HostWindows.OfType<Window>().ToArray()) window.Close();
        await Flush();
        Context.Dispose();
    }

    private sealed class Module(Probe probe) : IPluginModule
    {
        public void Configure(IPluginRegistration registration)
        {
            registration.Services.AddSingleton(probe);
            registration.AddPersistableDocument<Model, EditorView>(new(Type, "V16 新建", "V16 窗口行为测试", "测试"));
        }
    }

    internal sealed class Probe : IDocumentInteractionService
    {
        internal TaskCompletionSource? Blocker { get; set; }
        internal bool FailInitialization { get; set; }
        internal List<Model> Models { get; } = [];
        internal DocumentCloseChoice Choice { get; set; } = DocumentCloseChoice.Discard;
        internal TaskCompletionSource<DocumentCloseChoice>? PendingChoice { get; set; }
        internal int Confirmations { get; private set; }
        internal List<string> Errors { get; } = [];
        public Task<DocumentCloseChoice> ConfirmCloseAsync(IReadOnlyList<string> names, bool isApplicationExit)
        {
            Confirmations++;
            return PendingChoice?.Task ?? Task.FromResult(Choice);
        }
        public Task<bool> ConfirmRecoveryAsync(string fileName) => Task.FromResult(false);
        public Task ShowErrorAsync(string message) { Errors.Add(message); return Task.CompletedTask; }
    }

    internal sealed class Model(Probe probe) : IPersistablePluginDocument, IDisposable
    {
        private long _revision;
        public DocumentPresentationState Presentation { get; private set; } = new("V16 页面");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public bool IsDirty { get; private set; }
        public event EventHandler? IsDirtyChanged;
        internal int DisposeCount { get; private set; }
        internal bool EditDuringSave { get; set; }
        public async ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken)
        {
            probe.Models.Add(this);
            Presentation = new(activation.Title.Length > 0 ? activation.Title : "V16 页面");
            if (probe.Blocker is { } blocker) await blocker.Task.WaitAsync(cancellationToken);
            if (probe.FailInitialization) throw new InvalidOperationException("V16 受控初始化失败");
        }
        internal void Edit() { _revision++; IsDirty = true; IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
        internal void Clean() { IsDirty = false; IsDirtyChanged?.Invoke(this, EventArgs.Empty); }
        public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken)
        {
            var snapshot = new DocumentSaveSnapshot(new(_revision), new(1, JsonSerializer.SerializeToElement("V16 内容")));
            if (EditDuringSave) Edit();
            return ValueTask.FromResult(snapshot);
        }
        public void AcceptChanges(DocumentRevision savedRevision) { if (savedRevision.Value == _revision) Clean(); }
        public void Dispose() => DisposeCount++;
    }

    internal sealed class EditorView : UserControl, IDisposable
    {
        internal TextBox Editor { get; } = new() { Text = "V16 编辑内容" };
        internal int DisposeCount { get; private set; }
        public EditorView() => Content = Editor;
        public void Dispose() => DisposeCount++;
    }
}
