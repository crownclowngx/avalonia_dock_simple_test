using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels.Bindings;
using MyAvaloniaManagement.Business.Restart;

namespace MyAvaloniaManagement.Views;

internal sealed partial class MainWindow : Window, IWindowContentFullscreenHost
{
    private readonly WorkbenchWindowInteraction _interaction;
    private bool _windowCloseApproved;
    private bool _windowClosePending;
    private int _closeGeneration;
    private readonly HostRestartCoordinator? _restart;

    public MainWindow() : this(new WorkbenchWindowContext()) { }

    internal MainWindow(WorkbenchWindowContext windows, HostRestartCoordinator? restart = null)
    {
        InitializeComponent();
        _restart = restart;
        _restart?.Attach(Close, () => IsVisible && !_windowClosePending && !_windowCloseApproved && !CommandPaletteHost.IsBusy);
        _interaction = new WorkbenchWindowInteraction(this, windows, CommandPaletteLayer,
            CommandPaletteHost, ContentFullscreenLayer, ContentFullscreenHost);
        windows.Register(this, main: true);
        Opened += OnWindowOpened;
        Closed += (_, _) =>
        {
            _restart?.WindowClosed();
            if (DataContext is ViewModels.MainWindowViewModel viewModel) viewModel.CloseFloatingWindowsForExit();
            _interaction.Dispose();
        };
        DataContextChanged += (_, _) => _interaction.SetBindings((DataContext as IMainWindowViewBindings)?.WorkbenchCommands);
    }

    private void OnWindowOpened(object? sender, EventArgs args)
    {
        _restart?.RefreshAvailability();
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
                _closeGeneration++;
                viewModel.CancelWindowClose();
                _restart?.Cancel();
            }
            return;
        }
        if (e.Cancel)
        {
            _closeGeneration++;
            viewModel.CancelWindowClose();
            _restart?.Cancel();
            return;
        }
        e.Cancel = true;
        if (_windowClosePending || CommandPaletteHost.IsBusy) return;
        _windowClosePending = true;
        var generation = _closeGeneration;
        try
        {
            var preparation = PrepareCloseAsync(viewModel);
            if (preparation.IsCompletedSuccessfully && preparation.Result)
            {
                _windowCloseApproved = true;
                e.Cancel = false;
                return;
            }
            // 等待确认期间可能出现另一轮原生否决；旧 continuation 不能把已撤销请求当成普通退出提交。
            if (!await preparation || generation != _closeGeneration)
            { viewModel.CancelWindowClose(); _restart?.Cancel(); return; }
            _windowCloseApproved = true;
            Dispatcher.UIThread.Post(Close, DispatcherPriority.Background);
        }
        catch (Exception exception)
        {
            viewModel.CancelWindowClose();
            _restart?.Cancel();
            Console.Error.WriteLine($"Window errorCode=MAIN_CLOSE_FAILED type={exception.GetType().Name}");
        }
        finally { _windowClosePending = false; _restart?.RefreshAvailability(); }
    }

    /// <summary>保留普通关闭协议；重启仅在两侧增加设置冻结和助手就绪，任何失败都撤销原许可。</summary>
    private async Task<bool> PrepareCloseAsync(ViewModels.MainWindowViewModel viewModel)
    {
        if (_restart is not null && !await _restart.BeginPreparationAsync()) return false;
        if (!await viewModel.PrepareWindowCloseAsync()) return false;
        return _restart is null || await _restart.PrepareHandoffAsync();
    }

    IDisposable? IWindowContentFullscreenHost.TryPresent(Control content) =>
        _interaction.TryPresent(content);
}
