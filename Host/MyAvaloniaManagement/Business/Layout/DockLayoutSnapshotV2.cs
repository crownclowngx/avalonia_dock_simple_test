using System;
using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// Dock 布局 V2 的唯一内存快照。这里只保存宿主可以重建的结构状态，禁止加入 Document 或插件业务数据。
/// </summary>
internal sealed record DockLayoutSnapshotV2
{
    internal const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<DockPaneSnapshotV2> Panes { get; init; } = [];

    public List<DockToolSnapshotV2> Tools { get; init; } = [];

    public string? ActiveToolId { get; init; }
}

/// <summary>保存一个稳定 Pane 的比例，不保存运行时 Dock 对象。</summary>
internal sealed record DockPaneSnapshotV2
{
    public required string Id { get; init; }

    public double Proportion { get; init; }
}

/// <summary>保存 Tool 在主窗口稳定 ToolDock 中的顺序和显示状态。</summary>
internal sealed record DockToolSnapshotV2
{
    public required string Id { get; init; }

    public required string DockId { get; init; }

    public int Order { get; init; }

    public bool IsVisible { get; init; }

    public bool IsPinned { get; init; }
}

/// <summary>布局校验只返回稳定错误码和已经通过格式检查的稳定 ID。</summary>
internal readonly record struct DockLayoutValidationError(
    string Code,
    string? StableId);

/// <summary>
/// 校验已经完成严格 JSON 读取的 V2 快照；本类型不访问文件系统，也不读取运行时 Dock 树。
/// </summary>
internal static class DockLayoutSnapshotValidator
{
    private const int MaximumStableIdLength = 128;
    private const double MinimumPaneProportion = 0.05;
    private const double MaximumPaneProportion = 0.95;

    internal static DockLayoutValidationError? Validate(DockLayoutSnapshotV2? snapshot)
    {
        if (snapshot is null)
        {
            return new("LAYOUT_EMPTY", null);
        }

        if (snapshot.SchemaVersion != DockLayoutSnapshotV2.CurrentSchemaVersion)
        {
            return new("LAYOUT_SCHEMA_UNSUPPORTED", null);
        }

        if (snapshot.Panes is null || snapshot.Tools is null)
        {
            return new("LAYOUT_COLLECTION_INVALID", null);
        }

        var paneIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pane in snapshot.Panes)
        {
            if (pane is null || !IsStableId(pane.Id))
            {
                return new("LAYOUT_PANE_ID_INVALID", NormalizeForLog(pane?.Id));
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
            if (tool is null || !IsStableId(tool.Id))
            {
                return new("LAYOUT_TOOL_ID_INVALID", NormalizeForLog(tool?.Id));
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

            if (tool.IsPinned && !tool.IsVisible)
            {
                return new("LAYOUT_PINNED_STATE_INVALID", tool.Id);
            }
        }

        if (snapshot.ActiveToolId is { } activeToolId &&
            (!IsStableId(activeToolId) || !toolIds.Contains(activeToolId)))
        {
            return new("LAYOUT_ACTIVE_TOOL_INVALID", NormalizeForLog(activeToolId));
        }

        return null;
    }

    private static bool IsStableId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumStableIdLength &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');

    private static string? NormalizeForLog(string? value) =>
        IsStableId(value) ? value : null;
}
