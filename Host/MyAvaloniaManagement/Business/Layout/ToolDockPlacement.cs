using System;
using Dock.Model.Core;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 将当前插件描述符中的方向统一映射为 Dock 的四向布局语义。
/// </summary>
internal static class ToolDockPlacement
{
    internal static Alignment ToAlignment(
        MyAvaloniaManagement.PluginSdk.UI.ToolDockSide side) => side switch
    {
        MyAvaloniaManagement.PluginSdk.UI.ToolDockSide.Left => Alignment.Left,
        MyAvaloniaManagement.PluginSdk.UI.ToolDockSide.Right => Alignment.Right,
        MyAvaloniaManagement.PluginSdk.UI.ToolDockSide.Top => Alignment.Top,
        MyAvaloniaManagement.PluginSdk.UI.ToolDockSide.Bottom => Alignment.Bottom,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "未知的 Tool 停靠方向。"),
    };

    public static Alignment NormalizeAlignment(Alignment alignment) =>
        alignment is Alignment.Left
            or Alignment.Right
            or Alignment.Top
            or Alignment.Bottom
            ? alignment
            : Alignment.Left;

    public static string GetDockId(Alignment alignment) =>
        alignment switch
        {
            Alignment.Right => DockLayoutIds.RightTools,
            Alignment.Top => DockLayoutIds.TopTools,
            Alignment.Bottom => DockLayoutIds.BottomTools,
            _ => DockLayoutIds.LeftTools
        };

    public static string GetPaneId(Alignment alignment) =>
        NormalizeAlignment(alignment) switch
        {
            Alignment.Right => DockLayoutIds.RightPane,
            Alignment.Top => DockLayoutIds.TopPane,
            Alignment.Bottom => DockLayoutIds.BottomPane,
            _ => DockLayoutIds.LeftPane
        };

    public static double GetDefaultProportion(Alignment alignment) =>
        NormalizeAlignment(alignment) is Alignment.Top or Alignment.Bottom
            ? 0.20
            : 0.15;

    public static bool TryGetAlignmentFromDockId(
        string? dockId,
        out Alignment alignment)
    {
        alignment = dockId switch
        {
            DockLayoutIds.LeftTools => Alignment.Left,
            DockLayoutIds.RightTools => Alignment.Right,
            DockLayoutIds.TopTools => Alignment.Top,
            DockLayoutIds.BottomTools => Alignment.Bottom,
            _ => Alignment.Unset
        };
        return alignment != Alignment.Unset;
    }

    public static bool TryGetAlignmentFromPaneId(
        string? paneId,
        out Alignment alignment)
    {
        alignment = paneId switch
        {
            DockLayoutIds.LeftPane => Alignment.Left,
            DockLayoutIds.RightPane => Alignment.Right,
            DockLayoutIds.TopPane => Alignment.Top,
            DockLayoutIds.BottomPane => Alignment.Bottom,
            _ => Alignment.Unset
        };
        return alignment != Alignment.Unset;
    }
}
