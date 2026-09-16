using System;
using Avalonia.Controls;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.Bindings;

namespace MyAvaloniaManagement.Views;

internal sealed partial class MainWindow : Window, IWindowContentFullscreenHost
{
    private readonly WorkbenchWindowInteraction _interaction;
    private bool _windowCloseApproved;
    private bool _windowClosePending;

    public MainWindow() : this(new WorkbenchWindowContext()) { }

    internal MainWindow(WorkbenchWindowContext windows)
    {
        InitializeComponent();
        _interaction = new WorkbenchWindowInteraction(this, windows, CommandPaletteLayer,
            CommandPaletteHost, ContentFullscreenLayer, ContentFullscreenHost);
        windows.Register(this, main: true);
        Opened += OnWindowOpened;
        Closed += (_, _) =>
        {
            if (DataContext is ViewModels.MainWindowViewModel viewModel) viewModel.CloseFloatingWindowsForExit();
            _interaction.Dispose();
        };
        DataContextChanged += (_, _) => _interaction.SetBindings((DataContext as IMainWindowViewBindings)?.WorkbenchCommands);
    }

    private void OnWindowOpened(object? sender, EventArgs args)
    {
        _interaction.SetBindings((DataContext as IMainWindowViewBindings)?.WorkbenchCommands);
        if (DataContext is ViewModels.MainWindowViewModel viewModel) viewModel.ApplyPendingLayout();
    }

    internal void OpenCommandPalette() => _interaction.OpenCommandPalette();
    internal bool HasFullscreenContent => _interaction.HasFullscreenContent;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        // 先让其他原生监听者决定是否取消；许可在最终拒绝时必须撤销，干净文档同样排空命令。
        base.OnClosing(e);
        if (DataContext is not ViewModels.MainWindowViewModel viewModel) return;
        if (_windowCloseApproved)
        {
            if (e.Cancel)
            {
                _windowCloseApproved = false;
                viewModel.CancelWindowClose();
            }
            return;
        }
        if (e.Cancel) return;
        e.Cancel = true;
        if (_windowClosePending || CommandPaletteHost.IsBusy) return;
        _windowClosePending = true;
        try
        {
            var preparation = viewModel.PrepareWindowCloseAsync();
            if (preparation.IsCompletedSuccessfully && preparation.Result)
            {
                _windowCloseApproved = true;
                e.Cancel = false;
                return;
            }
            if (!await preparation) return;
            _windowCloseApproved = true;
            Dispatcher.UIThread.Post(Close, DispatcherPriority.Background);
        }
        catch (Exception exception)
        {
            viewModel.CancelWindowClose();
            Console.Error.WriteLine($"Window errorCode=MAIN_CLOSE_FAILED type={exception.GetType().Name}");
        }
        finally { _windowClosePending = false; }
    }

    IDisposable? IWindowContentFullscreenHost.TryPresent(Control content) =>
        _interaction.TryPresent(content);
}
