using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Controls;
using MyAvaloniaManagement.ViewModels.Bindings;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Business.Presentation;

/// <summary>
/// 记录同一工作台的主窗、活动浮窗与唯一命令面板会话。只借用窗口和展示协作者，
/// 不建立业务模型目录，也不释放 Runtime。每次登记必须与窗口 Closed 成对解除。
/// </summary>
internal sealed class WorkbenchWindowContext : IDisposable
{
    private readonly Dictionary<Window, WindowPlacementTracker> _windows = [];
    private Window? _active;
    private readonly AsyncLocal<Window?> _interactionOwner = new();
    private WorkbenchWindowInteraction? _palette;
    internal Window? MainWindow { get; private set; }
    internal IWorkbenchCommandPresentationBindings? Commands =>
        (MainWindow?.DataContext as IMainWindowViewBindings)?.WorkbenchCommands;

    internal void Register(Window window, bool main)
    {
        if (_windows.ContainsKey(window)) return;
        _windows.Add(window, new WindowPlacementTracker(window, OnPlacementChanged));
        if (main) MainWindow = window;
        window.Activated += OnActivated;
        window.Closed += OnClosed;
    }

    /// <summary>窗口正常位置或尺寸提交后的通知；布局生命周期负责合并保存。</summary>
    internal event EventHandler? WindowPlacementChanged;

    internal DockWindowBounds CaptureBounds(Window? window) =>
        window is not null && _windows.TryGetValue(window, out var placement) ? placement.Bounds : DockWindowBounds.Default;

    internal void ApplyBounds(Window window, DockWindowBounds bounds)
    {
        if (_windows.TryGetValue(window, out var placement)) placement.Apply(bounds);
    }

    internal void RecoverOffscreenWindows()
    {
        foreach (var pair in _windows)
        {
            if (ReferenceEquals(pair.Key, MainWindow)) continue;
            if (!DockScreenPlacement.IsReachable(pair.Value.Bounds, WindowPlacementTracker.GetScreens(pair.Key)))
                pair.Value.Apply(pair.Value.Bounds);
        }
    }

    private void OnPlacementChanged()
    {
        foreach (EventHandler observer in WindowPlacementChanged?.GetInvocationList() ?? [])
        {
            try { observer(this, EventArgs.Empty); }
            catch { /* 观察者失败不能中断原生窗口的位置更新；保存协调器自行记录失败。 */ }
        }
    }

    /// <summary>明确有效目标优先，其次活动工作台窗口，最后主窗口；辅助窗口不能成为工作台 Owner。</summary>
    internal Window? SelectOwner(Window? preferred = null)
    {
        preferred ??= _interactionOwner.Value;
        if (preferred is not null && _windows.ContainsKey(preferred) && preferred.IsVisible) return preferred;
        if (_active is not null && _windows.ContainsKey(_active) && _active.IsVisible) return _active;
        return MainWindow?.IsVisible == true ? MainWindow : null;
    }

    /// <summary>
    /// 为一次有明确目标的异步交互固定 Owner。AsyncLocal 只随这条异步调用链传播，
    /// 不会把另一窗口独立发起的文件选择错误地路由到本次关闭目标；using 结束后恢复外层值。
    /// </summary>
    internal IDisposable UseOwner(Window? owner)
    {
        var previous = _interactionOwner.Value;
        _interactionOwner.Value = owner;
        return new OwnerScope(() => _interactionOwner.Value = previous);
    }

    /// <summary>面板运行操作期间保留原会话；其余情况先结束旧窗口会话，再由调用窗口接管。</summary>
    internal bool TryOpenPalette(WorkbenchWindowInteraction requester)
    {
        if (_palette is { } previous && !ReferenceEquals(previous, requester))
        {
            if (previous.IsBusy) { previous.FocusPalette(); return false; }
            previous.ClosePalette(restoreFocus: false);
        }
        _palette = requester;
        return true;
    }

    internal void ReleasePalette(WorkbenchWindowInteraction requester)
    {
        if (ReferenceEquals(_palette, requester)) _palette = null;
    }

    private void OnActivated(object? sender, EventArgs args) => _active = sender as Window;
    private void OnClosed(object? sender, EventArgs args)
    {
        if (sender is not Window window) return;
        window.Activated -= OnActivated;
        window.Closed -= OnClosed;
        if (_windows.Remove(window, out var placement)) placement.Dispose();
        if (ReferenceEquals(_active, window)) _active = null;
        if (ReferenceEquals(MainWindow, window)) MainWindow = null;
    }
    public void Dispose()
    {
        foreach (var pair in _windows)
        {
            pair.Key.Activated -= OnActivated;
            pair.Key.Closed -= OnClosed;
            pair.Value.Dispose();
        }
        _windows.Clear();
        _active = null;
        MainWindow = null;
        _palette = null;
    }

    private sealed class OwnerScope(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

}
