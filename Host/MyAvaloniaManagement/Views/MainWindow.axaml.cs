using System;
using Avalonia.Controls;
using Avalonia.Threading;
using MyAvaloniaManagementCommon.Presentation;

namespace MyAvaloniaManagement.Views;

public partial class MainWindow : Window, IWindowContentFullscreenHost
{
    private object? _fullscreenOwner;

    public MainWindow()
    {
        InitializeComponent();
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

    private static void EnsureUiThread()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException(
                "窗口内容区全屏接口只能在 Avalonia UI 线程调用。");
        }
    }
}
