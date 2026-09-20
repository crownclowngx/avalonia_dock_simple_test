using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Presentation.Commands;
using MyAvaloniaManagement.Views;

namespace MyAvaloniaManagement.Business.Presentation;

/// <summary>
/// 为一个工作台窗口绑定快捷键、命令面板和内容全屏。命令投影由 Runtime 共享，
/// KeyBinding、焦点引用和全屏租约由本窗口独占；关闭时成对退订，迟到通知不能复活窗口。
/// </summary>
internal sealed class WorkbenchWindowInteraction : IDisposable
{
    private readonly Window _window;
    private readonly WorkbenchWindowContext _context;
    private readonly WindowContentFullscreenSession _fullscreenSession;
    private readonly Border CommandPaletteLayer;
    private readonly CommandPaletteView CommandPaletteHost;
    private readonly List<KeyBinding> _generatedKeyBindings = [];
    private IWorkbenchCommandPresentationBindings? _bindings;
    private IWorkbenchKeyBindingProjection? _keyBindingProjection;
    private IInputElement? _palettePreviousFocus;
    private bool _paletteOpen;
    private bool _closed;

    internal WorkbenchWindowInteraction(Window window, WorkbenchWindowContext context,
        Border paletteLayer, CommandPaletteView palette, Border fullscreenLayer, ContentControl fullscreenContent)
    {
        _window = window;
        _context = context;
        CommandPaletteLayer = paletteLayer;
        CommandPaletteHost = palette;
        _fullscreenSession = new WindowContentFullscreenSession(fullscreenLayer, fullscreenContent);
        palette.CloseRequested += OnCommandPaletteCloseRequested;
        window.AddHandler(Window.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    internal bool IsBusy => CommandPaletteHost.IsBusy;
    internal bool HasFullscreenContent => _fullscreenSession.HasActiveLease;
    internal IDisposable? TryPresent(Control content) => _fullscreenSession.TryPresent(content);
    internal void FocusPalette() { _window.Activate(); CommandPaletteHost.RefocusSearchBox(); }

    internal void SetBindings(IWorkbenchCommandPresentationBindings? bindings)
    {
        if (_closed) return;
        _bindings = bindings;
        CommandPaletteHost.DataContext = bindings;
        AttachKeyBindingProjection();
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        ClosePalette(restoreFocus: false);
        DetachKeyBindingProjection();
        _window.RemoveHandler(Window.KeyDownEvent, OnPreviewKeyDown);
        CommandPaletteHost.CloseRequested -= OnCommandPaletteCloseRequested;
        CommandPaletteHost.DataContext = null;
        _bindings = null;
        _fullscreenSession.Dispose();
    }

    private void AttachKeyBindingProjection()
    {
        if (_closed)
        {
            DetachKeyBindingProjection();
            return;
        }

        var next = _bindings?.KeyBindings;
        if (ReferenceEquals(next, _keyBindingProjection))
        {
            RebuildKeyBindings();
            return;
        }

        DetachKeyBindingProjection();
        _keyBindingProjection = next;
        if (_keyBindingProjection is not null)
        {
            _keyBindingProjection.Changed += OnKeyBindingProjectionChanged;
            RebuildKeyBindings();
        }
    }

    private void DetachKeyBindingProjection()
    {
        if (_keyBindingProjection is not null)
        {
            _keyBindingProjection.Changed -= OnKeyBindingProjectionChanged;
            _keyBindingProjection = null;
        }
        RemoveGeneratedKeyBindings();
    }

    private void OnKeyBindingProjectionChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RebuildKeyBindings();
        }
        else
        {
            Dispatcher.UIThread.Post(RebuildKeyBindings);
        }
    }

    private void RebuildKeyBindings()
    {
        RemoveGeneratedKeyBindings();
        if (_closed || _paletteOpen || _keyBindingProjection is null)
        {
            return;
        }
        foreach (var entry in _keyBindingProjection.Items)
        {
            var binding = new KeyBinding
            {
                Gesture = new KeyGesture(entry.Key, entry.Modifiers),
                Command = entry.Command,
            };
            _window.KeyBindings.Add(binding);
            _generatedKeyBindings.Add(binding);
        }
    }

    private void RemoveGeneratedKeyBindings()
    {
        foreach (var binding in _generatedKeyBindings)
        {
            _ = _window.KeyBindings.Remove(binding);
            binding.Command = null!;
        }
        _generatedKeyBindings.Clear();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs args)
    {
        var paletteGesture = HostWorkbenchKeyGestures.CommandPalette;
        if (args.Key != paletteGesture.Key || args.KeyModifiers != paletteGesture.Modifiers)
        {
            return;
        }

        args.Handled = true;
        OpenCommandPalette();
    }

    /// <summary>打开或重新聚焦当前窗口唯一的 Command Palette 会话。</summary>
    internal void OpenCommandPalette()
    {
        if (_closed)
        {
            return;
        }
        if (_paletteOpen)
        {
            CommandPaletteHost.RefocusSearchBox();
            return;
        }

        if (!_context.TryOpenPalette(this)) return;
        // 必须在显示遮罩前捕获。IsVisible 也可能触发布局/选择通知，先显示再读取
        // 最近组会把模板初始化期间的选择误认为用户本次操作的来源。
        var creationTarget = _bindings?.Palette.CaptureCreationTarget(_context.GetLayout(_window));
        _palettePreviousFocus = _window.FocusManager?.GetFocusedElement();
        _paletteOpen = true;
        CommandPaletteLayer.IsVisible = true;
        // Palette 是模态窗口层。会话期间移除工作台生成快捷键，防止搜索框中的按键
        // 同时触发保存或插件命令；关闭后从同一投影重建，不保存第二份绑定状态。
        RemoveGeneratedKeyBindings();
        // 首次显示时隐藏面板可能尚未挂到视觉树，必须通过已绑定的窗口投影捕获目标，
        // 不能依赖 View 的 AttachedToVisualTree 已经建立查询订阅。
        CommandPaletteHost.BeginSession(creationTarget, _context);
    }

    private void OnCommandPaletteCloseRequested(object? sender, PaletteCloseRequestedEventArgs args) =>
        ClosePalette(restoreFocus: args.RestorePreviousFocus);

    internal void ClosePalette(bool restoreFocus)
    {
        if (!_paletteOpen)
        {
            return;
        }

        _paletteOpen = false;
        _context.ReleasePalette(this);
        CommandPaletteLayer.IsVisible = false;
        CommandPaletteHost.EndSession();
        if (!_closed)
        {
            RebuildKeyBindings();
        }

        var previous = _palettePreviousFocus;
        _palettePreviousFocus = null;
        if (!restoreFocus || _closed)
        {
            return;
        }

        // Focus() 自身会在元素已经卸载、隐藏或禁用时返回 false；直接使用返回值比读取
        // 具体 Control 状态更符合窄 IInputElement 边界，也覆盖 Popup 等非 Control 焦点目标。
        if (previous?.Focus() != true)
        {
            _ = _window.Focus();
        }
    }

}
