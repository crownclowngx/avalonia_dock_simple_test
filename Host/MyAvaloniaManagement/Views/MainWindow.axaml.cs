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
        Closing += OnWindowClosing;
        Closed += (_, _) => _interaction.Dispose();
        DataContextChanged += (_, _) => _interaction.SetBindings((DataContext as IMainWindowViewBindings)?.WorkbenchCommands);
    }

    private void OnWindowOpened(object? sender, EventArgs args)
    {
        _interaction.SetBindings((DataContext as IMainWindowViewBindings)?.WorkbenchCommands);
        if (DataContext is ViewModels.MainWindowViewModel viewModel) viewModel.ApplyPendingLayout();
    }

    internal void OpenCommandPalette() => _interaction.OpenCommandPalette();
    internal bool HasFullscreenContent => _interaction.HasFullscreenContent;

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // 与功能中心一致：初始化在途时不把窗口关闭误认为取消，等待真实操作完成后再允许退出。
        if (CommandPaletteHost.IsBusy) { e.Cancel = true; return; }
        if (_windowCloseApproved)
        {
            if (DataContext is ViewModels.MainWindowViewModel approvedViewModel)
            {
                approvedViewModel.SaveLayout();
            }
            return;
        }

        if (DataContext is ViewModels.MainWindowViewModel cleanViewModel &&
            !cleanViewModel.HasDirtyDocuments())
        {
            cleanViewModel.SaveLayout();
            return;
        }

        // Avalonia Closing 是同步可取消事件。首次请求必须立即取消，再异步汇总保存；只有
        // 用户完成决策后才重新 Close。这样窗口不会在文件选择器显示期间提前释放 Scope。
        e.Cancel = true;
        if (_windowClosePending ||
            DataContext is not ViewModels.MainWindowViewModel viewModel)
        {
            return;
        }

        _windowClosePending = true;
        try
        {
            if (!await viewModel.ConfirmWindowCloseAsync())
            {
                return;
            }

            _windowCloseApproved = true;
            Dispatcher.UIThread.Post(Close, DispatcherPriority.Background);
        }
        finally
        {
            _windowClosePending = false;
        }
    }

    IDisposable? IWindowContentFullscreenHost.TryPresent(Control content) =>
        _interaction.TryPresent(content);
}
