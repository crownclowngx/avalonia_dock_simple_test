using System;
using System.Linq;
using Dock.Model.Controls;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 在修改 Dock 树之前验证布局快照引用的贡献、Pane、Tool 和稳定 ID。
/// </summary>
/// <remarks>
/// 验证器只读取冻结目录和当前运行时结构，不创建 Pane、不移动 Tool，也不保存文件。
/// 生命周期先调用 <see cref="ValidateContributions"/>，确认声明与可用性后才允许 Mapper
/// 补齐快照所需 Dock；随后调用 <see cref="Validate"/> 检查实际结构。分成两个明确阶段，
/// 是为了保证未知或不可用贡献失败时默认布局仍保持零修改。
/// </remarks>
internal static class DockLayoutRuntimeValidator
{
    /// <summary>
    /// 验证快照引用的 Tool 已声明、当前可用并且已经产生运行时实例。
    /// </summary>
    internal static DockLayoutValidationError? ValidateContributions(
        DockLayoutSnapshotV2 snapshot,
        WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(session);

        var tools = session.CreatedTools;
        foreach (var tool in snapshot.Tools)
        {
            if (!session.IsRegisteredTool(tool.Id))
            {
                return new("LAYOUT_PLUGIN_MISSING", tool.Id);
            }

            if (!session.IsToolAvailable(tool.Id))
            {
                return new("LAYOUT_PLUGIN_UNAVAILABLE", tool.Id);
            }

            if (!tools.ContainsKey(tool.Id))
            {
                return new("LAYOUT_TOOL_ACTIVATION_MISSING", tool.Id);
            }
        }

        return null;
    }

    /// <summary>
    /// 验证贡献检查之后的运行时 Dock 树包含快照要求的 Pane 与稳定 ToolDock。
    /// </summary>
    internal static DockLayoutValidationError? Validate(
        DockLayoutSnapshotV2 snapshot,
        IRootDock root,
        WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(session);

        if (ValidateContributions(snapshot, session) is { } contributionError)
        {
            return contributionError;
        }

        var runtimeIds = DockTreeNavigator.Enumerate(root)
            .Where(dockable => !string.IsNullOrWhiteSpace(dockable.Id))
            .Select(dockable => dockable.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var pane in snapshot.Panes)
        {
            if (!runtimeIds.Contains(pane.Id))
            {
                return new("LAYOUT_PANE_MISSING", pane.Id);
            }
        }

        foreach (var tool in snapshot.Tools)
        {
            if (!runtimeIds.Contains(tool.DockId) ||
                !DockLayoutIds.IsToolDockId(tool.DockId))
            {
                return new("LAYOUT_TOOL_DOCK_MISSING", tool.Id);
            }
        }

        return null;
    }
}
