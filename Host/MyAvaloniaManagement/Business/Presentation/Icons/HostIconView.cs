using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Lifecycle;

namespace MyAvaloniaManagement.Business.Presentation.Icons;

/// <summary>一个位置一个控件，按声明画布等比居中绘制，并使用当前位置的主题画刷。</summary>
/// <remarks>
/// 直接在逻辑画布上绘制，避免 PathIcon 按几何包围盒缩放而丢失留白与非正方形画布。
/// 缓存只复用只读使用的 Geometry；背景、前景和尺寸都由各个视图独立决定。
/// </remarks>
internal sealed class HostIconView : Control
{
    public static readonly StyledProperty<HostIconRequest?> SourceProperty =
        AvaloniaProperty.Register<HostIconView, HostIconRequest?>(nameof(Source));
    public static readonly StyledProperty<HostIconRenderer?> RendererProperty =
        AvaloniaProperty.Register<HostIconView, HostIconRenderer?>(nameof(Renderer));
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<HostIconView, IBrush?>(nameof(Foreground), Brushes.Black);
    private HostIconRenderer? _subscribed;
    private bool _attached;

    static HostIconView() => AffectsRender<HostIconView>(SourceProperty, RendererProperty, ForegroundProperty);

    public HostIconRequest? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public HostIconRenderer? Renderer { get => GetValue(RendererProperty); set => SetValue(RendererProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Renderer is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var (geometry, definition) = Renderer.Resolve(Source);
        var scale = Math.Min(Bounds.Width / definition.ViewBoxWidth, Bounds.Height / definition.ViewBoxHeight);
        var transform = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(
            (Bounds.Width - definition.ViewBoxWidth * scale) / 2,
            (Bounds.Height - definition.ViewBoxHeight * scale) / 2);
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushTransform(transform))
        using (context.PushClip(new Rect(0, 0, definition.ViewBoxWidth, definition.ViewBoxHeight)))
            context.DrawGeometry(Foreground, null, geometry);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateSubscription();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        UpdateSubscription();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RendererProperty) UpdateSubscription();
    }

    private void UpdateSubscription()
    {
        if (_subscribed is not null) _subscribed.Changed -= OnAvailabilityChanged;
        _subscribed = _attached ? Renderer : null;
        if (_subscribed is not null) _subscribed.Changed += OnAvailabilityChanged;
    }

    private void OnAvailabilityChanged(object? sender, PluginAvailabilityChangedEventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) InvalidateVisual();
        else Dispatcher.UIThread.Post(InvalidateVisual);
    }
}
