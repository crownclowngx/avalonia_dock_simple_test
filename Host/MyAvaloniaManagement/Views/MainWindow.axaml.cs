using System;
using Avalonia.Controls;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Views;

internal sealed partial class MainWindow : Window, IWindowContentFullscreenHost
{
    private readonly WindowContentFullscreenSession _fullscreenSession;
    private bool _windowCloseApproved;
    private bool _windowClosePending;

    public MainWindow()
    {
        InitializeComponent();
        _fullscreenSession = new WindowContentFullscreenSession(
            ContentFullscreenLayer,
            ContentFullscreenHost);
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // Closing 可能被 Document 保存确认取消，只有真正 Closed 才使全屏宿主永久失效。
        // ContentHost 的视觉树卸载也会触发同一幂等入口，两条框架时序不形成双重释放。
        _fullscreenSession.Dispose();
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.ApplyPendingLayout();
        }
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
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
        _fullscreenSession.TryPresent(content);
}
