using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Business.Presentation;

/// <summary>
/// 追踪一个原生窗口的正常位置、逻辑尺寸与最大化意图。最大化/最小化期间不覆盖正常尺寸，
/// 只向外发布已完成的位置变化；订阅由窗口登记者在 Closed 时释放。
/// </summary>
internal sealed class WindowPlacementTracker : IDisposable
{
    private readonly Window _window;
    private readonly Action _changed;
    private bool _applying;
    internal DockWindowBounds Bounds { get; private set; } = DockWindowBounds.Default;

    internal WindowPlacementTracker(Window window, Action changed)
    {
        _window = window;
        _changed = changed;
        window.PositionChanged += OnPositionChanged;
        window.SizeChanged += OnSizeChanged;
        window.PropertyChanged += OnPropertyChanged;
        window.Opened += OnOpened;
        window.Screens.Changed += OnScreensChanged;
    }

    internal void Apply(DockWindowBounds saved)
    {
        var adjusted = DockScreenPlacement.Normalize(saved, GetScreens(_window));
        _applying = true;
        try
        {
            _window.WindowState = WindowState.Normal;
            _window.MinWidth = Math.Min(_window.MinWidth, adjusted.Width);
            _window.MinHeight = Math.Min(_window.MinHeight, adjusted.Height);
            _window.Width = adjusted.Width;
            _window.Height = adjusted.Height;
            _window.Position = new PixelPoint(checked((int)Math.Round(adjusted.X)), checked((int)Math.Round(adjusted.Y)));
            Bounds = adjusted;
            if (adjusted.Maximized) _window.WindowState = WindowState.Maximized;
        }
        finally { _applying = false; }
        _changed();
    }

    internal static DockScreenBounds[] GetScreens(Window window) => window.Screens.All.Select(screen =>
        new DockScreenBounds(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height, screen.Scaling)).ToArray();

    private void Refresh()
    {
        if (_applying) return;
        var next = Bounds;
        if (_window.WindowState == WindowState.Normal)
        {
            var width = double.IsFinite(_window.Width) ? _window.Width : _window.Bounds.Width;
            var height = double.IsFinite(_window.Height) ? _window.Height : _window.Bounds.Height;
            if (width > 0 && height > 0)
            {
                var screen = _window.Screens.ScreenFromWindow(_window);
                next = new DockWindowBounds(_window.Position.X, _window.Position.Y, width, height, false,
                    screen is null ? null : new(screen.WorkingArea.X, screen.WorkingArea.Y,
                        screen.WorkingArea.Width, screen.WorkingArea.Height, screen.Scaling));
            }
        }
        else if (_window.WindowState == WindowState.Maximized) next = Bounds with { Maximized = true };
        if (Equals(next, Bounds)) return;
        Bounds = next;
        _changed();
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs args) => Refresh();
    private void OnSizeChanged(object? sender, SizeChangedEventArgs args) => Refresh();
    private void OnOpened(object? sender, EventArgs args) => Refresh();
    private void OnScreensChanged(object? sender, EventArgs args) => Apply(Bounds);
    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.WindowStateProperty) Refresh();
    }

    public void Dispose()
    {
        _window.PositionChanged -= OnPositionChanged;
        _window.SizeChanged -= OnSizeChanged;
        _window.PropertyChanged -= OnPropertyChanged;
        _window.Opened -= OnOpened;
        _window.Screens.Changed -= OnScreensChanged;
    }
}
