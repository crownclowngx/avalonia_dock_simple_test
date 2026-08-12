using System;
using Dock.Model.Core;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 将插件元数据中的字符串 Alignment 统一映射为 Dock 的四向布局语义。
/// </summary>
internal static class ToolDockPlacement
{
    public static Alignment ToAlignment(ToolDockSide side) => side switch
    {
        ToolDockSide.Right => Alignment.Right,
        ToolDockSide.Top => Alignment.Top,
        ToolDockSide.Bottom => Alignment.Bottom,
        _ => Alignment.Left,
    };

    public static Alignment NormalizeAlignment(Alignment alignment) =>
        alignment is Alignment.Left
            or Alignment.Right
            or Alignment.Top
            or Alignment.Bottom
            ? alignment
            : Alignment.Left;

    public static Alignment ParseAlignment(string? value)
    {
        if (Enum.TryParse<Alignment>(
                value?.Trim(),
                ignoreCase: true,
                out var alignment) &&
            alignment is Alignment.Left
                or Alignment.Right
                or Alignment.Top
                or Alignment.Bottom)
        {
            return alignment;
        }

        return Alignment.Left;
    }

    public static string GetDockId(Alignment alignment) =>
        alignment switch
        {
            Alignment.Right => DockLayoutIds.RightTools,
            Alignment.Top => DockLayoutIds.TopTools,
            Alignment.Bottom => DockLayoutIds.BottomTools,
            _ => DockLayoutIds.LeftTools
        };

    public static string GetDockId(string? alignment) =>
        GetDockId(ParseAlignment(alignment));

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
