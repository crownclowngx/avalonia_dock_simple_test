using System;
using Avalonia.Controls;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.PluginStatus;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.ViewModels.PluginStatus;
using MyAvaloniaManagement.Views.PluginStatus;

namespace MyAvaloniaManagement.Business.Presentation;

/// <summary>由一个 HostRuntime 拥有的插件状态窗口服务，负责单实例、Owner 和关闭清理。</summary>
/// <remarks>
/// 采用与工具中心相同的简单窗口所有权，不引入通用窗口框架。
/// 窗口按需创建；它既不是 Tool 也不是 Document，因此不参与 Dock 或插件模型生命周期。
/// 主窗口真正关闭才释放窗口，取消主窗口关闭不会破坏后续使用。
/// </remarks>
internal sealed class PluginStatusWindowService(IPluginStatusQuery query, WorkspaceSession workspace, TimeProvider time) : IDisposable
{
    private Window? _owner;
    private PluginStatusWindow? _window;
    private bool _disposed;
    internal PluginStatusWindow? CurrentWindow => _window;
    internal bool CanShow => !_disposed && _owner is not null && workspace.CanOperateTools;

    internal void Attach(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_owner, owner)) return;
        if (_owner is not null) throw new InvalidOperationException("插件状态已绑定主窗口。");
        _owner = owner;
        owner.Closed += OwnerClosed;
    }

    internal void ShowOrActivate()
    {
        if (!CanShow) return;
        if (_window is { } existing)
        {
            // 菜单再次请求时也刷新；窗口已激活时 Avalonia 不一定再次发出 Activated。
            Refresh();
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var model = new PluginStatusWindowViewModel(query, time);
        model.Refresh();
        var window = new PluginStatusWindow { DataContext = model };
        _window = window;
        try
        {
            var screen = _owner!.Screens.ScreenFromWindow(_owner);
            if (screen is not null)
            {
                var width = Math.Max(320, screen.WorkingArea.Width / screen.Scaling - 48);
                var height = Math.Max(240, screen.WorkingArea.Height / screen.Scaling - 48);
                window.MinWidth = Math.Min(window.MinWidth, width);
                window.MinHeight = Math.Min(window.MinHeight, height);
                window.Width = Math.Min(window.Width, width);
                window.Height = Math.Min(window.Height, height);
            }
            window.Activated += WindowActivated;
            window.Closed += WindowClosed;
            window.Show(_owner);
        }
        catch
        {
            ReleaseWindow();
            window.Close();
            throw;
        }
    }

    private void Refresh()
    {
        if (CanShow && _window?.DataContext is PluginStatusWindowViewModel model) model.Refresh();
    }
    private void WindowActivated(object? sender, EventArgs args) => Refresh();
    private void OwnerClosed(object? sender, EventArgs args) => Dispose();
    private void WindowClosed(object? sender, EventArgs args) => ReleaseWindow();

    private void ReleaseWindow()
    {
        if (_window is not { } window) return;
        _window = null;
        window.Activated -= WindowActivated;
        window.Closed -= WindowClosed;
        window.DataContext = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        void Close()
        {
            if (_owner is not null) _owner.Closed -= OwnerClosed;
            _owner = null;
            _window?.Close();
            ReleaseWindow();
        }
        // Runtime 释放不保证发生在 UI 线程；所有控件访问统一回到 Dispatcher。
        if (Dispatcher.UIThread.CheckAccess()) Close(); else Dispatcher.UIThread.Post(Close);
    }
}
