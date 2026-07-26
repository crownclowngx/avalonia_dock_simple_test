using System;
using Avalonia.Controls;
using MyAvaloniaManagementCommon.Presentation;

namespace MyAvaloniaManagement.Views;

public partial class MainWindow : Window, IWindowContentFullscreenHost
{
    private object? _fullscreenOwner;

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

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel viewModel)
        {
            viewModel.SaveLayout();
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
