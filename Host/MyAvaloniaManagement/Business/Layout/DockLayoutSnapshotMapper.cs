using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 在运行时 Dock 树与持久化 V2 快照之间进行双向映射。
/// 映射只描述结构转换，不负责文件读写、版本决策或生命周期编排。
/// </summary>
internal static class DockLayoutSnapshotMapper
{
    internal static DockLayoutSnapshotV2 Capture(
        IRootDock root,
        WorkspaceSession session)
    {
        var mainDockables = EnumerateDockables(root).ToArray();
        var allToolDocks = mainDockables
            .OfType<IToolDock>()
            .ToArray();
        var hidden = mainDockables
            .OfType<IRootDock>()
            .SelectMany(dock => dock.HiddenDockables ?? [])
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var pinned = CapturePinnedPlacements(mainDockables);

        var placements = new Dictionary<string, ToolPlacement>(StringComparer.Ordinal);
        foreach (var pair in session.CreatedTools)
        {
            var tool = pair.Value;
            pinned.TryGetValue(tool, out var pinnedPlacement);
            var currentDock = allToolDocks.FirstOrDefault(dock =>
                dock.VisibleDockables?.Any(candidate =>
                    ReferenceEquals(candidate, tool)) == true);
            var dockId = pinnedPlacement?.DockId ??
                         ResolveStableDockId(currentDock) ??
                         ResolveStableDockId(tool.OriginalOwner as IToolDock) ??
                         GetDefaultDockId(session, tool.Id);
            var isPinned = pinnedPlacement is not null;
            var isVisible = currentDock is not null || isPinned;

            placements.Add(
                pair.Key,
                new ToolPlacement(
                    tool,
                    dockId,
                    isVisible && !hidden.Contains(tool),
                    isPinned));
        }

        var toolSnapshots = new List<DockToolSnapshotV2>(placements.Count);
        foreach (var dockId in DockLayoutIds.ToolDockIds)
        {
            var placementGroup = placements.Values
                .Where(placement => placement.DockId == dockId)
                .ToArray();
            var visibleOrder = allToolDocks
                .Where(dock => ResolveStableDockId(dock) == dockId)
                .SelectMany(dock => dock.VisibleDockables ?? [])
                .OfType<Tool>()
                .Select(tool => tool.Id)
                .ToArray();
            var pinnedOrder = pinned.Values
                .Where(placement => placement.DockId == dockId)
                .OrderBy(placement => placement.Order)
                .Select(placement => placement.Tool.Id)
                .ToArray();
            var order = visibleOrder
                .Concat(pinnedOrder)
                .Concat(placementGroup.Select(placement => placement.Tool.Id))
                .Distinct(StringComparer.Ordinal)
                .Select((id, index) => (id, index))
                .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);

            foreach (var placement in placementGroup)
            {
                toolSnapshots.Add(new DockToolSnapshotV2
                {
                    Id = placement.Tool.Id,
                    DockId = dockId,
                    Order = order[placement.Tool.Id],
                    IsVisible = placement.IsVisible,
                    IsPinned = placement.IsPinned,
                });
            }
        }

        var panes = CapturePaneSnapshots(
            mainDockables,
            allToolDocks,
            session);

        return new DockLayoutSnapshotV2
        {
            Panes = panes,
            Tools = toolSnapshots,
            ActiveToolId = FindActiveToolId(root)
        };
    }

    internal static void ApplySnapshot(
        DockLayoutSnapshotV2 snapshot,
        IRootDock root,
        WorkspaceSession session)
    {
        EnsureSnapshotDocks(snapshot, root, session);

        var dockables = EnumerateDockables(root).ToArray();
        var paneMap = dockables
            .Where(dockable => !string.IsNullOrWhiteSpace(dockable.Id))
            .ToDictionary(dockable => dockable.Id, StringComparer.Ordinal);
        var toolDocks = dockables
            .OfType<IToolDock>()
            .Where(dock => DockLayoutIds.IsToolDockId(dock.Id))
            .ToDictionary(dock => dock.Id, StringComparer.Ordinal);

        foreach (var pane in snapshot.Panes)
        {
            paneMap[pane.Id].Proportion = pane.Proportion;
        }

        // V2 只表达主窗口内的稳定 ToolDock；浮动窗口从线格式和恢复逻辑中完全删除。
        foreach (var group in snapshot.Tools
                     .GroupBy(tool => tool.DockId, StringComparer.Ordinal))
        {
            var targetDock = toolDocks[group.Key];
            var orderedTools = group
                .OrderBy(tool => tool.Order)
                .Select(tool => session.CreatedTools[tool.Id])
                .ToArray();
            for (var index = 0; index < orderedTools.Length; index++)
            {
                var tool = orderedTools[index];
                session.DockFactory.RemoveDockable(tool, collapse: false);
                session.DockFactory.InsertDockable(targetDock, tool, index);
            }
        }

        foreach (var toolState in snapshot.Tools.Where(tool => !tool.IsVisible))
        {
            session.DockFactory.HideDockable(session.CreatedTools[toolState.Id]);
        }

        foreach (var toolState in snapshot.Tools
                     .Where(tool => tool.IsPinned)
                     .OrderBy(tool => tool.DockId, StringComparer.Ordinal)
                     .ThenBy(tool => tool.Order))
        {
            session.DockFactory.PinDockable(session.CreatedTools[toolState.Id]);
        }

        if (snapshot.ActiveToolId is { } activeToolId)
        {
            var activeState = snapshot.Tools.Single(tool => tool.Id == activeToolId);
            if (activeState.IsVisible && !activeState.IsPinned)
            {
                session.DockFactory.SetActiveDockable(session.CreatedTools[activeToolId]);
            }
        }
    }

    private static IEnumerable<IDockable> EnumerateDockables(IDockable root)
    {
        yield return root;
        if (root is not IDock { VisibleDockables: { } children })
        {
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var descendant in EnumerateDockables(child))
            {
                yield return descendant;
            }
        }
    }

    private static string? FindActiveToolId(IRootDock root)
    {
        foreach (var dock in EnumerateDockables(root).OfType<IDock>())
        {
            if (dock.ActiveDockable is Tool tool)
            {
                return tool.Id;
            }
        }

        if (root.Windows is null)
        {
            return null;
        }

        foreach (var window in root.Windows)
        {
            if (window.Layout is null)
            {
                continue;
            }

            foreach (var dock in EnumerateDockables(window.Layout).OfType<IDock>())
            {
                if (dock.ActiveDockable is Tool tool)
                {
                    return tool.Id;
                }
            }
        }

        return null;
    }

    private static string GetDefaultDockId(
        WorkspaceSession session,
        string toolId) =>
        ToolDockPlacement.GetDockId(session.GetToolAlignment(toolId));

    internal static void EnsureSnapshotDocks(
        DockLayoutSnapshotV2 snapshot,
        IRootDock root,
        WorkspaceSession session)
    {
        var alignments = snapshot.Tools
            .Select(tool => tool.DockId)
            .Select(id => ToolDockPlacement.TryGetAlignmentFromDockId(
                id,
                out var alignment)
                ? alignment
                : Alignment.Unset)
            .Concat(snapshot.Panes.Select(pane =>
                ToolDockPlacement.TryGetAlignmentFromPaneId(
                    pane.Id,
                    out var alignment)
                    ? alignment
                    : Alignment.Unset))
            .Where(alignment => alignment != Alignment.Unset)
            .Distinct();

        foreach (var alignment in alignments)
        {
            session.EnsureToolDock(root, alignment);
        }
    }

    private static List<DockPaneSnapshotV2> CapturePaneSnapshots(
        IReadOnlyCollection<IDockable> dockables,
        IReadOnlyCollection<IToolDock> toolDocks,
        WorkspaceSession session)
    {
        var createdTools = session.CreatedTools.Values
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var panes = new List<DockPaneSnapshotV2>();

        foreach (var alignment in new[]
                 {
                     Alignment.Left,
                     Alignment.Top,
                     Alignment.Bottom,
                     Alignment.Right
                 })
        {
            var paneId = ToolDockPlacement.GetPaneId(alignment);
            var stablePane = dockables.FirstOrDefault(
                dockable => dockable.Id == paneId);
            var dynamicToolDock = toolDocks.FirstOrDefault(dock =>
                !DockLayoutIds.IsToolDockId(dock.Id) &&
                ResolveStableDockId(dock) ==
                ToolDockPlacement.GetDockId(alignment) &&
                dock.VisibleDockables?.Any(createdTools.Contains) == true);
            if (stablePane is null && dynamicToolDock is null)
            {
                continue;
            }

            var proportionSource = (IDockable?)dynamicToolDock ?? stablePane;
            var proportion = GetPersistableProportion(
                proportionSource,
                alignment);
            panes.Add(new DockPaneSnapshotV2
            {
                Id = paneId,
                Proportion = proportion
            });
        }

        return panes;
    }

    private static double GetPersistableProportion(
        IDockable? dockable,
        Alignment alignment)
    {
        var proportion = dockable?.Proportion ?? double.NaN;
        if (!double.IsFinite(proportion) || proportion <= 0)
        {
            proportion = dockable?.CollapsedProportion ?? double.NaN;
        }

        if (!double.IsFinite(proportion) || proportion <= 0)
        {
            proportion = ToolDockPlacement.GetDefaultProportion(alignment);
        }

        return Math.Clamp(proportion, 0.05, 0.95);
    }

    private static string? ResolveStableDockId(IToolDock? dock)
    {
        if (dock is null)
        {
            return null;
        }

        if (IsKnownToolDockId(dock.Id))
        {
            return dock.Id;
        }

        return dock.Alignment is Alignment.Left
            or Alignment.Right
            or Alignment.Top
            or Alignment.Bottom
            ? ToolDockPlacement.GetDockId(dock.Alignment)
            : null;
    }

    private static bool IsKnownToolDockId(string? id) =>
        DockLayoutIds.IsToolDockId(id);

    private static Dictionary<Tool, PinnedPlacement> CapturePinnedPlacements(
        IEnumerable<IDockable> dockables)
    {
        var result = new Dictionary<Tool, PinnedPlacement>(
            ReferenceEqualityComparer.Instance);

        foreach (var rootDock in dockables.OfType<IRootDock>())
        {
            AddPinnedPlacements(
                result,
                rootDock.LeftPinnedDockables,
                DockLayoutIds.LeftTools);
            AddPinnedPlacements(
                result,
                rootDock.TopPinnedDockables,
                DockLayoutIds.TopTools);
            AddPinnedPlacements(
                result,
                rootDock.BottomPinnedDockables,
                DockLayoutIds.BottomTools);
            AddPinnedPlacements(
                result,
                rootDock.RightPinnedDockables,
                DockLayoutIds.RightTools);
        }

        return result;
    }

    private static void AddPinnedPlacements(
        IDictionary<Tool, PinnedPlacement> placements,
        IEnumerable<IDockable>? dockables,
        string dockId)
    {
        if (dockables is null)
        {
            return;
        }

        var order = 0;
        foreach (var tool in dockables.OfType<Tool>())
        {
            placements.TryAdd(tool, new PinnedPlacement(tool, dockId, order));
            order++;
        }
    }

    private sealed record ToolPlacement(
        Tool Tool,
        string DockId,
        bool IsVisible,
        bool IsPinned);

    private sealed record PinnedPlacement(
        Tool Tool,
        string DockId,
        int Order);
}
