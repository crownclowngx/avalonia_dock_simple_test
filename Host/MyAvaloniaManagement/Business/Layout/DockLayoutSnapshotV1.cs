using System;
using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// Dock 结构快照。这里只保存宿主可重建的布局信息，禁止加入 Document 或插件业务状态。
/// </summary>
internal sealed record DockLayoutSnapshotV1
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<DockPaneSnapshotV1> Panes { get; init; } = [];

    public List<DockToolSnapshotV1> Tools { get; init; } = [];

    public string? ActiveToolId { get; init; }
}

internal sealed record DockPaneSnapshotV1
{
    public required string Id { get; init; }

    public double Proportion { get; init; }
}

internal sealed record DockToolSnapshotV1
{
    public required string Id { get; init; }

    /// <summary>
    /// 工具回到主窗口时所属的稳定 ToolDock ID；浮动状态不会改变这个归属。
    /// </summary>
    public required string DockId { get; init; }

    public int Order { get; init; }

    public bool IsVisible { get; init; }

    /// <summary>
    /// 工具是否以自动隐藏的边缘标签形式显示。
    /// </summary>
    public bool IsPinned { get; init; }

    public bool IsFloating { get; init; }

    public DockFloatingBoundsV1? FloatingBounds { get; init; }
}

internal sealed record DockFloatingBoundsV1
{
    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }
}

internal readonly record struct DockLayoutValidationError(
    string Code,
    string? StableId);

internal static class DockLayoutSnapshotValidator
{
    private const int MaximumStableIdLength = 128;
    private const double MinimumPaneProportion = 0.05;
    private const double MaximumPaneProportion = 0.95;
    private const double MinimumFloatingSize = 100;
    private const double MaximumFloatingSize = 20_000;
    private const double MaximumFloatingCoordinate = 100_000;

    public static DockLayoutValidationError? Validate(DockLayoutSnapshotV1? snapshot)
    {
        if (snapshot is null)
        {
            return new("LAYOUT_EMPTY", null);
        }

        if (snapshot.SchemaVersion != DockLayoutSnapshotV1.CurrentSchemaVersion)
        {
            return new("LAYOUT_SCHEMA_UNSUPPORTED", null);
        }

        var paneIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pane in snapshot.Panes)
        {
            if (!IsStableId(pane.Id))
            {
                return new("LAYOUT_PANE_ID_INVALID", NormalizeForLog(pane.Id));
            }

            if (!paneIds.Add(pane.Id))
            {
                return new("LAYOUT_PANE_ID_DUPLICATE", pane.Id);
            }

            if (!double.IsFinite(pane.Proportion) ||
                pane.Proportion is < MinimumPaneProportion or > MaximumPaneProportion)
            {
                return new("LAYOUT_PANE_PROPORTION_INVALID", pane.Id);
            }
        }

        var toolIds = new HashSet<string>(StringComparer.Ordinal);
        var dockOrders = new HashSet<(string DockId, int Order)>();
        foreach (var tool in snapshot.Tools)
        {
            if (!IsStableId(tool.Id))
            {
                return new("LAYOUT_TOOL_ID_INVALID", NormalizeForLog(tool.Id));
            }

            if (!toolIds.Add(tool.Id))
            {
                return new("LAYOUT_TOOL_ID_DUPLICATE", tool.Id);
            }

            if (!IsStableId(tool.DockId))
            {
                return new("LAYOUT_TOOL_DOCK_ID_INVALID", tool.Id);
            }

            if (tool.Order < 0 || !dockOrders.Add((tool.DockId, tool.Order)))
            {
                return new("LAYOUT_TOOL_ORDER_INVALID", tool.Id);
            }

            if (!tool.IsFloating && tool.FloatingBounds is not null)
            {
                return new("LAYOUT_FLOATING_STATE_INVALID", tool.Id);
            }

            if (tool.IsPinned && (!tool.IsVisible || tool.IsFloating))
            {
                return new("LAYOUT_PINNED_STATE_INVALID", tool.Id);
            }

            if (tool.IsFloating &&
                (!tool.IsVisible || !IsValidBounds(tool.FloatingBounds)))
            {
                return new("LAYOUT_FLOATING_BOUNDS_INVALID", tool.Id);
            }
        }

        if (snapshot.ActiveToolId is { } activeToolId &&
            (!IsStableId(activeToolId) || !toolIds.Contains(activeToolId)))
        {
            return new("LAYOUT_ACTIVE_TOOL_INVALID", NormalizeForLog(activeToolId));
        }

        return null;
    }

    private static bool IsValidBounds(DockFloatingBoundsV1? bounds) =>
        bounds is not null &&
        double.IsFinite(bounds.X) &&
        double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) &&
        double.IsFinite(bounds.Height) &&
        Math.Abs(bounds.X) <= MaximumFloatingCoordinate &&
        Math.Abs(bounds.Y) <= MaximumFloatingCoordinate &&
        bounds.Width is >= MinimumFloatingSize and <= MaximumFloatingSize &&
        bounds.Height is >= MinimumFloatingSize and <= MaximumFloatingSize;

    private static bool IsStableId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumStableIdLength &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');

    private static string? NormalizeForLog(string? value) =>
        IsStableId(value) ? value : null;
}
