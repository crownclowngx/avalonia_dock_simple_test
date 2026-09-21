using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Docking;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 协调工具恢复、稳定停靠点重建以及明确主窗口目标的全宽兼容策略。
/// 状态流程集中在此处，HostDockFactory 只提供 Dock 库操作，不拥有工作区状态。
/// </summary>
internal sealed class ToolDockCoordinator(
    HostDockFactory factory,
    DockWorkspaceBuilder workspaceBuilder,
    Func<string, Alignment> getAlignment,
    Action<string>? reportSkipped = null)
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

    /// <summary>
    /// 只为主窗口文档区和稳定全局目标保留旧全宽约定；普通 ToolDock 或任意嵌套分割
    /// 不归一化。调用方已确认主窗口归属；ID 只用于取得主骨架引用，不用于匹配拖放目标。
    /// </summary>
    internal void OnDockSplitCompleted(
        IDock originalTarget,
        IDockable insertedDock,
        DockOperation operation,
        IRootDock root)
    {
        if (_normalizingVerticalDock ||
            operation is not (DockOperation.Top or DockOperation.Bottom) ||
            insertedDock is not IToolDock sourceDock ||
            sourceDock.VisibleDockables is not { Count: > 0 })
        {
            return;
        }

        var rows = DockTreeNavigator.FindDockById<ProportionalDock>(root, DockLayoutIds.WorkspaceRows);
        var columns = DockTreeNavigator.FindDockById<ProportionalDock>(root, DockLayoutIds.WorkspaceColumns);
        if (!DockSplitPolicy.IsMainFullWidthTarget(root, originalTarget)) return;

        // 不完整的稳定骨架不允许启动第二轮修改，保留基类已完成的局部分割供用户继续操作。
        if (rows?.VisibleDockables is null || columns is null || !rows.VisibleDockables.Contains(columns))
        {
            ReportSkipped("main-skeleton-unavailable");
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

            FlattenTemporarySplit(temporaryOwner, root);
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
        if (alignment is Alignment.Left or Alignment.Right)
        {
            var columns = DockTreeNavigator.FindDockById<ProportionalDock>(
                              root,
                              DockLayoutIds.WorkspaceColumns)
                          ?? throw new InvalidOperationException(
                              $"Dock '{DockLayoutIds.WorkspaceColumns}' was not found.");
            // 空区域可能已被 Dock 的移除/拖动流程清理。左右区域位于横向布局两端，
            // 不要求 Documents 是直接子节点，因而也保留已有文档拆分结构。
            if (alignment == Alignment.Left)
            {
                factory.InsertDockable(columns, pane, 0);
                factory.InsertDockable(columns, new ProportionalDockSplitter(), 1);
            }
            else
            {
                factory.AddDockable(columns, new ProportionalDockSplitter());
                factory.AddDockable(columns, pane);
            }
            return;
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

    /// <summary>
    /// 在 UI 线程将单子项临时容器替换为原子项。RemoveDockable 即使 collapse=false 仍会清理
    /// 孤立分隔条，因此必须先插入替代项，再移除旧容器；调用方的批量范围延迟发布最终结构。
    /// </summary>
    private void FlattenTemporarySplit(IProportionalDock? temporaryDock, IRootDock root)
    {
        if (temporaryDock is null ||
            !string.IsNullOrEmpty(temporaryDock.Id))
        {
            return;
        }

        if (temporaryDock.Owner is not IDock parent || temporaryDock.VisibleDockables is null ||
            parent.VisibleDockables?.Contains(temporaryDock) != true ||
            !DockTreeNavigator.IsDockAttached(root, temporaryDock))
        {
            ReportSkipped("temporary-container-detached");
            return;
        }

        var remaining = temporaryDock.VisibleDockables
            .Where(item => item is not IProportionalDockSplitter)
            .ToArray();
        if (remaining.Length != 1 || !ReferenceEquals(remaining[0].Owner, temporaryDock))
        {
            ReportSkipped("temporary-container-not-single-child");
            return;
        }

        var remainingDockable = remaining[0];
        var wasActive = ReferenceEquals(parent.ActiveDockable, temporaryDock);
        var wasDefault = ReferenceEquals(parent.DefaultDockable, temporaryDock);
        var proportion = temporaryDock.Proportion;
        var collapsedProportion = temporaryDock.CollapsedProportion;
        factory.RemoveDockable(remainingDockable, collapse: false);
        // 仍保留临时容器作为位置锚点。不得复用移除父项之前缓存的索引。
        var parentIndex = parent.VisibleDockables!.IndexOf(temporaryDock);
        if (parentIndex < 0)
        {
            // 同步监听者已改动父列表时保留子项与原容器的关系，不再使用过期父索引；
            // 这里不承诺外部监听者对整棵树的修改能够回滚。
            factory.AddDockable(temporaryDock, remainingDockable);
            ReportSkipped("temporary-container-changed-during-replacement");
            return;
        }
        factory.InsertDockable(parent, remainingDockable, parentIndex);
        factory.RemoveDockable(temporaryDock, collapse: false);
        remainingDockable.Proportion = proportion;
        remainingDockable.CollapsedProportion = collapsedProportion;
        if (wasActive)
        {
            parent.ActiveDockable = remainingDockable;
        }
        if (wasDefault) parent.DefaultDockable = remainingDockable;
    }

    private void ReportSkipped(string reason)
    {
        // 诊断设施失败不能把已安全保留的布局再次变成拖放异常；这里只隔离诊断回调。
        try { reportSkipped?.Invoke(reason); }
        catch { /* 诊断回调不拥有业务控制流。 */ }
    }
}
