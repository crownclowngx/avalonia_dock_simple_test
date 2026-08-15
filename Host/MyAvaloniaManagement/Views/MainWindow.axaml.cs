using System;
using Avalonia.Controls;
using Avalonia.Threading;
using MyAvaloniaManagementCommon.Presentation;

namespace MyAvaloniaManagement.Views;

internal sealed partial class MainWindow : Window, IWindowContentFullscreenHost
{
    private object? _fullscreenOwner;
    private bool _windowCloseApproved;
    private bool _windowClosePending;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
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

    public bool TryPresent(Control content, object owner)
    {
        EnsureUiThread();
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(owner);

        if (_fullscreenOwner is not null &&
            !ReferenceEquals(_fullscreenOwner, owner))
        {
            return false;
        }

        if (ContentFullscreenHost.Content is Control current &&
            !ReferenceEquals(current, content))
        {
            return false;
        }

        _fullscreenOwner = owner;
        ContentFullscreenHost.Content = content;
        ContentFullscreenLayer.IsVisible = true;
        return true;
    }

    public bool TryRestore(object owner)
    {
        EnsureUiThread();
        ArgumentNullException.ThrowIfNull(owner);

        if (!ReferenceEquals(_fullscreenOwner, owner))
        {
            return false;
        }

        ContentFullscreenHost.Content = null;
        ContentFullscreenLayer.IsVisible = false;
        _fullscreenOwner = null;
        return true;
    }

    private void EnsureUiThread()
    {
        if (!Dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                "窗口内容区全屏接口只能在 Avalonia UI 线程调用。");
        }
    }
}
