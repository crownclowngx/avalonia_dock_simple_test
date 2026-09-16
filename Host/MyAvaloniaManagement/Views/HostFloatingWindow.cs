using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Dock.Avalonia.Behaviors;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Controls.Overlays;
using Dock.Model.Controls;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Views;

/// <summary>
/// 使用 Dock 原生窗口的宿主适配，只增加工作台交互覆盖层和关闭取消保护。
/// 窗口外框、拖动、停靠与 Root 协议继续由 HostWindow 处理，不重建 Document 或 Tool。
/// </summary>
internal sealed class HostFloatingWindow : HostWindow, IWindowContentFullscreenHost
{
    private readonly WorkbenchWindowContext _windows;
    private WorkbenchWindowInteraction? _interaction;
    private WindowClosingEventArgs? _closingArgs;

    internal HostFloatingWindow(WorkbenchWindowContext windows)
    {
        _windows = windows;
        windows.Register(this, main: false);
        // 保留框架 Window ControlTheme，仅组合内容层。OverlayHost 及其生命周期行为与
        // 当前锁定 Dock 原生模板一致，工作台覆盖层与 Dock 拖动覆盖层各自维护所有权。
        ContentTemplate = new FuncDataTemplate<IRootDock>((root, _) => BuildContent(root));
    }

    internal bool IsCloseCancelled => _closingArgs?.Cancel == true;
    internal bool HasFullscreenContent => _interaction?.HasFullscreenContent == true;
    internal void OpenCommandPalette() => _interaction?.OpenCommandPalette();

    private Control BuildContent(IRootDock? root)
    {
        _interaction?.Dispose();
        var dock = new DockControl { Layout = root };
        var overlay = new OverlayHost { Content = dock };
        VisualTreeLifecycleBehavior.SetIsEnabled(overlay, true);
        var fullscreenContent = new ContentControl
        {
            Name = "ContentFullscreenHost",
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        var fullscreenLayer = new Border
        {
            Name = "ContentFullscreenLayer", IsVisible = false, Background = Brushes.Black,
            ZIndex = 10000, Child = fullscreenContent,
        };
        var palette = new CommandPaletteView
        {
            Name = "CommandPaletteHost", VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(24, 72, 24, 24),
        };
        var paletteLayer = new Border
        {
            Name = "CommandPaletteLayer", IsVisible = false, Background = Brush.Parse("#99000000"),
            ZIndex = 20000, Child = palette,
        };
        var content = new Grid { Children = { overlay, fullscreenLayer, paletteLayer } };
        content.Bind(MarginProperty, new Binding(nameof(OffScreenMargin)) { Source = this });
        _interaction = new WorkbenchWindowInteraction(this, _windows, paletteLayer, palette,
            fullscreenLayer, fullscreenContent);
        _interaction.SetBindings(_windows.Commands);
        return content;
    }

    protected override void OnClosing(WindowClosingEventArgs args)
    {
        _closingArgs = args;
        if (_interaction?.IsBusy == true) args.Cancel = true;
        try
        {
            // Dock 先触发 Window.Closing，再咨询 Factory。Factory 必须读取同一 args 的最终
            // Cancel 值；否则框架可能在其他监听者取消窗口后仍执行 Root.Close，先拆掉页面。
            base.OnClosing(args);
        }
        finally { _closingArgs = null; }
    }

    protected override void OnClosed(EventArgs args)
    {
        try { base.OnClosed(args); }
        finally { _interaction?.Dispose(); _interaction = null; }
    }

    IDisposable? IWindowContentFullscreenHost.TryPresent(Control content) => _interaction?.TryPresent(content);
}
