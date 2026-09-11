using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Commands.Context;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.ViewModels.ToolCenter;
using MyAvaloniaManagement.Views.ToolCenter;

namespace MyAvaloniaManagement.Business.Presentation;

/// <summary>Runtime 级非模态窗口所有者，不创建 Dock 工具或等待整个窗口存续期。</summary>
internal sealed class ToolCenterWindowService : IDisposable
{
    private readonly Func<ToolCenterViewModel> _createViewModel;
    private readonly ToolCenterActions _actions;
    private readonly WorkspaceSession _workspace;
    private Window? _owner;
    private ToolCenterWindow? _window;
    private bool _disposed;
    internal ToolCenterWindow? CurrentWindow => _window;
    internal bool CanShow => !_disposed && _owner is not null && _workspace.CanOperateTools;

    public ToolCenterWindowService(ToolCenterQuery query, ToolCenterPreferences preferences, ToolCenterActions actions,
        WorkspaceSession workspace, PluginAvailabilityReadModel availability, HostIconRenderer icons)
    {
        _createViewModel = () => new(query, preferences, actions, workspace, availability, icons);
        _actions = actions; _workspace = workspace;
        _actions.FocusRequested += FocusOwner;
    }

    internal void Attach(Window owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_owner, owner)) return;
        if (_owner is not null) throw new InvalidOperationException("工具中心已绑定主窗口");
        _owner = owner;
        owner.Closed += OwnerClosed;
    }

    internal void ShowOrActivate()
    {
        if (!CanShow) return;
        if (_window is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate(); return;
        }
        var viewModel = _createViewModel();
        try
        {
            var window = new ToolCenterWindow { DataContext = viewModel };
            _window = window;
            // 首次尺寸遵循 Owner 逻辑尺寸；屏幕很小时也允许收缩，避免最小尺寸越过可用区域。
            var screen = _owner!.Screens.ScreenFromWindow(_owner);
            if (screen is not null)
            {
                var width = Math.Max(320, screen.WorkingArea.Width / screen.Scaling - 48);
                var height = Math.Max(240, screen.WorkingArea.Height / screen.Scaling - 48);
                window.MinWidth = Math.Min(window.MinWidth, width); window.MinHeight = Math.Min(window.MinHeight, height);
                window.Width = Math.Min(window.Width, width); window.Height = Math.Min(window.Height, height);
            }
            window.Closed += WindowClosed;
            window.Show(_owner);
        }
        catch { ReleaseWindow(); viewModel.Dispose(); throw; }
    }

    private void FocusOwner(object? sender, EventArgs args) { if (!_disposed) _owner?.Activate(); }
    private void OwnerClosed(object? sender, EventArgs args) => Dispose();
    private void WindowClosed(object? sender, EventArgs args) => ReleaseWindow();
    private void ReleaseWindow()
    {
        if (_window is not { } window) return;
        _window = null;
        window.Closed -= WindowClosed;
        (window.DataContext as ToolCenterViewModel)?.Dispose();
        window.DataContext = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _actions.FocusRequested -= FocusOwner;
        void Close()
        {
            if (_owner is not null) _owner.Closed -= OwnerClosed;
            _owner = null;
            _window?.Close(); ReleaseWindow();
        }
        if (Dispatcher.UIThread.CheckAccess()) Close(); else Dispatcher.UIThread.Post(Close);
    }
}

internal sealed class HostOpenToolCenterCommandHandler(ToolCenterWindowService windows) : IHostWorkbenchCommandHandler
{
    public bool CanExecute(WorkbenchContextSnapshot context) => windows.CanShow;
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(windows.ShowOrActivate);
    }
}
