using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Help;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Views.Help;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

public sealed class HelpWindowTests
{
    [AvaloniaFact]
    public async Task ModelessHelpIsSinglePerRuntimeAndMainCloseCancellationKeepsItOpen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "help-ui-" + Guid.NewGuid().ToString("N"));
        var readers = new List<FakeReader>();
        using var service = new HelpWindowService(new(), new(Path.Combine(directory, "state.json")), () =>
        { var reader = new FakeReader(); readers.Add(reader); return reader; });
        var main = new Window { Content = new TextBox() };
        var cancel = true;
        main.Closing += (_, args) => args.Cancel = cancel;
        main.Show(); service.Attach(main);
        try
        {
            service.ShowOrActivate();
            var help = Assert.IsType<HelpWindow>(service.CurrentWindow);
            await help.CurrentLoad;
            Assert.True(main.IsEnabled);
            Assert.True(help.ShowInTaskbar);
            service.ShowOrActivate(); Assert.Same(help, service.CurrentWindow);
            main.Close(); Assert.True(help.IsVisible); Assert.False(readers[0].Disposed);
            help.Navigate("attention", "Scalable Fabric"); await help.CurrentLoad;
            Assert.Equal("Scalable Fabric", readers[0].Query);
            help.Close(); Assert.True(readers[0].Disposed); Assert.Null(service.CurrentWindow);
            service.ShowOrActivate(); await service.CurrentWindow!.CurrentLoad;
            Assert.Equal("attention", service.CurrentWindow.ReadingState.ArticleId);
            cancel = false; main.Close();
            Assert.Null(service.CurrentWindow); Assert.All(readers, reader => Assert.True(reader.Disposed));
        }
        finally { cancel = false; main.Close(); service.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task ClosingAndFastNavigationIgnoreStaleRendererCompletion()
    {
        var reader = new FakeReader { Delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var window = new HelpWindow(new(), new(), reader); window.Show();
        window.Navigate("activity");
        window.Navigate("forkable");
        await Task.Delay(40);
        reader.Delay.SetResult(); await window.CurrentLoad;
        Assert.Equal("forkable", window.ReadingState.ArticleId);
        Assert.Equal("forkable", reader.Page?.Id);
        window.RequestedThemeVariant = ThemeVariant.Dark;
        Assert.True(reader.Presentation?.Dark);
        window.WindowState = WindowState.Minimized; Assert.True(reader.Suspended);
        window.Close(); Assert.True(reader.Disposed);
    }

    [AvaloniaFact]
    public async Task TwentyCloseCyclesDisposeEveryReaderAndReleaseWindows()
    {
        var weak = new List<WeakReference>();
        var directory = Path.Combine(Path.GetTempPath(), "help-cycles-" + Guid.NewGuid().ToString("N"));
        using var service = new HelpWindowService(new(), new(Path.Combine(directory, "state.json")), () => new FakeReader());
        try
        {
            for (var i = 0; i < 20; i++) await OpenClose(service, weak);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            await Task.Delay(100);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.True(weak.All(reference => !reference.IsAlive), "Surviving window indexes: " + string.Join(",", weak.Select((r, i) => (r, i)).Where(p => p.r.IsAlive).Select(p => p.i)));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task OpenClose(HelpWindowService service, List<WeakReference> weak)
    {
        service.ShowOrActivate();
        var window = service.CurrentWindow!; await window.CurrentLoad;
        weak.Add(new WeakReference(window)); window.Close();
        Assert.Null(service.CurrentWindow);
    }

    [AvaloniaFact]
    public void MenuF1AndPaletteShareHelpCommandWithoutAnActiveDocument()
    {
        using var context = new UiTestContext();
        var projection = context.Provider.GetRequiredService<WorkbenchCommandPresentation>();
        var menu = Assert.Single(projection.Menu.GetItems(PluginSdk.UI.WorkbenchMenuLocations.HelpShared)
            .OfType<WorkbenchMenuCommandProjectionEntry>(), item => item.Header == "帮助中心");
        var key = Assert.Single(projection.KeyBindings.Items, item => item.Key == Avalonia.Input.Key.F1);
        Assert.Same(menu.Command, key.Command);
        var palette = Assert.Single(projection.Palette.GetItems("帮助中心"));
        Assert.Equal(HostWorkbenchCommandIds.OpenHelp, palette.CommandId);
        Assert.True(palette.IsEnabled);
    }

    private sealed class FakeReader : IHelpReader
    {
        public Control View { get; } = new TextBlock { Text = "Headless reader" };
        public event EventHandler<HelpReaderMessage>? Message { add { } remove { } }
        internal HelpPage? Page { get; private set; }
        internal HelpReaderPresentation? Presentation { get; private set; }
        internal string Query { get; private set; } = "";
        internal bool Disposed { get; private set; }
        internal bool Suspended { get; private set; }
        internal TaskCompletionSource? Delay { get; init; }
        public async Task DisplayAsync(HelpPage page, HelpPagePosition position, string query, string anchor, HelpReaderPresentation presentation, CancellationToken cancellationToken)
        {
            if (Delay is not null) await Delay.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); Page = page; Query = query; Presentation = presentation;
        }
        public Task PresentAsync(HelpReaderPresentation presentation) { Presentation = presentation; return Task.CompletedTask; }
        public Task SetSuspendedAsync(bool suspended) { Suspended = suspended; return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }
}
