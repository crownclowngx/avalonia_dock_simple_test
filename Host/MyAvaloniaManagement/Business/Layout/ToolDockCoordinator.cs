using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.Events;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 协调工具恢复、稳定停靠点重建以及 Top/Bottom 临时结构归一化。
/// 状态流程集中在此处，ManagementFactory 继续作为 Dock 库的兼容适配入口。
/// </summary>
internal sealed class ToolDockCoordinator(
    ManagementFactory factory,
    DockWorkspaceBuilder workspaceBuilder,
    Func<string, Alignment> getAlignment,
    IHostEventBus eventBus)
{
    private bool _normalizingVerticalDock;

    internal ToolDock EnsureToolDock(IRootDock root, Alignment alignment)
    {
        ArgumentNullException.ThrowIfNull(root);
        alignment = ToolDockPlacement.NormalizeAlignment(alignment);
        var toolDockId = ToolDockPlacement.GetDockId(alignment);
        if (DockTreeNavigator.FindDockById<ToolDock>(root, toolDockId)
            is { } existingDock)
        {
            return existingDock;
        }

        var paneId = ToolDockPlacement.GetPaneId(alignment);
        var pane = DockTreeNavigator.FindDockById<ProportionalDock>(root, paneId);
        if (pane is null)
        {
            pane = workspaceBuilder.CreateToolPane(
                paneId,
                toolDockId,
                alignment,
                [],
                ToolDockPlacement.GetDefaultProportion(alignment));
            InsertMissingPane(root, pane, alignment);
            return (ToolDock)pane.VisibleDockables![0];
        }

        var toolDock = workspaceBuilder.CreateStableToolDock(toolDockId, alignment);
        factory.AddDockable(pane, toolDock);
        pane.ActiveDockable = toolDock;
        return toolDock;
    }

    internal bool RestoreTool(IRootDock root, Tool tool)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(tool);

        var originalToolDock = tool.OriginalOwner as IToolDock;
        if (originalToolDock is IDock attachedOwner &&
            DockTreeNavigator.IsDockAttached(root, attachedOwner))
        {
            factory.RestoreDockable(tool);
            if (DockTreeNavigator.IsDockableAttached(root, tool))
            {
                factory.SetActiveDockable(tool);
                return true;
            }
        }

        var alignment = originalToolDock is null
            ? getAlignment(tool.Id)
            : ToolDockPlacement.NormalizeAlignment(originalToolDock.Alignment);
        var targetDock = EnsureToolDock(root, alignment);

        DockTreeNavigator.RemoveFromHiddenDockables(root, tool);
        if (factory.FindRoot(tool, _ => true) is { HiddenDockables: { } hidden })
        {
            hidden.Remove(tool);
        }

        tool.OriginalOwner = null;
        factory.AddDockable(targetDock, tool);
        factory.SetActiveDockable(tool);
        return true;
    }

    internal bool ShowTool(
        IRootDock? root,
        IReadOnlyDictionary<string, Tool> createdTools,
        string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId) ||
            root is null ||
            !createdTools.TryGetValue(toolId, out var tool))
        {
            return false;
        }

        if (!DockTreeNavigator.IsDockableAttached(root, tool) &&
            !DockTreeNavigator.IsToolPinned(root, tool))
        {
            if (!RestoreTool(root, tool))
            {
                return false;
            }
        }
        else
        {
            factory.SetActiveDockable(tool);
        }

        eventBus.Publish(new UpdateLayoutMessage("ShowTool"));
        return true;
    }

    internal void OnDockableDocked(
        IDockable? dockable,
        DockOperation operation,
        IRootDock? fallbackRoot)
    {
        if (_normalizingVerticalDock ||
            operation is not (DockOperation.Top or DockOperation.Bottom) ||
            dockable is not IToolDock sourceDock ||
            sourceDock.VisibleDockables is not { Count: > 0 })
        {
            return;
        }

        var alignment = operation == DockOperation.Top
            ? Alignment.Top
            : Alignment.Bottom;
        if (sourceDock.Id == ToolDockPlacement.GetDockId(alignment))
        {
            return;
        }

        var tools = sourceDock.VisibleDockables.OfType<Tool>().ToArray();
        if (tools.Length == 0)
        {
            return;
        }

        var root = factory.FindRoot(sourceDock, _ => true) ?? fallbackRoot;
        if (root is null)
        {
            return;
        }

        var activeTool = sourceDock.ActiveDockable as Tool ?? tools[0];
        _normalizingVerticalDock = true;
        try
        {
            var targetDock = EnsureToolDock(root, alignment);
            var temporaryOwner = sourceDock.Owner as IProportionalDock;
            foreach (var tool in tools)
            {
                factory.RemoveDockable(tool, collapse: false);
                factory.AddDockable(targetDock, tool);
            }

            if (sourceDock.Owner is IDock sourceOwner &&
                sourceOwner.VisibleDockables?.Contains(sourceDock) == true)
            {
                factory.RemoveDockable(sourceDock, collapse: true);
            }

            FlattenTemporarySplit(temporaryOwner);
            factory.SetActiveDockable(activeTool);
        }
        finally
        {
            _normalizingVerticalDock = false;
        }
    }

    private void InsertMissingPane(
        IRootDock root,
        ProportionalDock pane,
        Alignment alignment)
    {
        if (alignment is not (Alignment.Top or Alignment.Bottom))
        {
            throw new InvalidOperationException(
                $"稳定停靠区域 '{pane.Id}' 已脱离主布局。");
        }

        var rows = DockTreeNavigator.FindDockById<ProportionalDock>(
                       root,
                       DockLayoutIds.WorkspaceRows)
                   ?? throw new InvalidOperationException(
                       $"Dock '{DockLayoutIds.WorkspaceRows}' was not found.");
        var columnsIndex = rows.VisibleDockables?
            .ToList()
            .FindIndex(item => item.Id == DockLayoutIds.WorkspaceColumns) ?? -1;
        if (columnsIndex < 0)
        {
            throw new InvalidOperationException(
                $"Dock '{DockLayoutIds.WorkspaceColumns}' was not found.");
        }

        var splitter = new ProportionalDockSplitter();
        if (alignment == Alignment.Top)
        {
            factory.InsertDockable(rows, pane, columnsIndex);
            factory.InsertDockable(rows, splitter, columnsIndex + 1);
        }
        else
        {
            factory.InsertDockable(rows, splitter, columnsIndex + 1);
            factory.InsertDockable(rows, pane, columnsIndex + 2);
        }
    }

    private void FlattenTemporarySplit(IProportionalDock? temporaryDock)
    {
        if (temporaryDock is null ||
            !string.IsNullOrEmpty(temporaryDock.Id) ||
            temporaryDock.Owner is not IDock parent ||
            temporaryDock.VisibleDockables is null)
        {
            return;
        }

        var remaining = temporaryDock.VisibleDockables
            .Where(item => item is not IProportionalDockSplitter)
            .ToArray();
        if (remaining.Length != 1 || parent.VisibleDockables is null)
        {
            return;
        }

        var parentIndex = parent.VisibleDockables.IndexOf(temporaryDock);
        if (parentIndex < 0)
        {
            return;
        }

        var remainingDockable = remaining[0];
        var wasActive = ReferenceEquals(parent.ActiveDockable, temporaryDock);
        factory.RemoveDockable(remainingDockable, collapse: false);
        factory.RemoveDockable(temporaryDock, collapse: false);
        factory.InsertDockable(parent, remainingDockable, parentIndex);
        if (wasActive)
        {
            parent.ActiveDockable = remainingDockable;
        }
    }
}
