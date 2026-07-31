using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.ViewModels;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 协调默认 Dock 树、结构快照和宿主窗口生命周期。
/// </summary>
internal sealed class DockLayoutLifecycle(DockLayoutStore store)
{
    private DockLayoutSnapshotV1? _pendingSnapshot;

    internal IRootDock Prepare(ManagementFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _pendingSnapshot = store.Load();
        var root = factory.CreateLayout();
        factory.InitLayout(root);
        return root;
    }

    /// <summary>
    /// 窗口显示后再移动或浮动工具，避免 Dock 的 HostWindow 尚未创建时产生半初始化窗口。
    /// </summary>
    internal IRootDock ApplyPending(
        IRootDock defaultRoot,
        ManagementFactory factory)
    {
        ArgumentNullException.ThrowIfNull(defaultRoot);
        ArgumentNullException.ThrowIfNull(factory);

        var snapshot = Interlocked.Exchange(ref _pendingSnapshot, null);
        if (snapshot is null)
        {
            return defaultRoot;
        }

        snapshot = NormalizeLegacyTwoWaySnapshot(snapshot, factory);
        EnsureSnapshotDocks(snapshot, defaultRoot, factory);

        if (ValidateAgainstRuntime(snapshot, defaultRoot, factory) is { } error)
        {
            store.RejectLoadedSnapshot(error.Code, error.StableId);
            return defaultRoot;
        }

        try
        {
            ApplySnapshot(snapshot, defaultRoot, factory);
            return defaultRoot;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            // 应用失败后必须丢弃整个已修改树，不能让一半旧布局和一半默认布局共同运行。
            store.RejectLoadedSnapshot("LAYOUT_APPLY_FAILED", null);
            var replacement = factory.CreateLayout();
            factory.InitLayout(replacement);
            return replacement;
        }
    }

    internal void Save(IRootDock root, ManagementFactory factory)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(factory);

        try
        {
            store.Save(Capture(root, factory));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // 退出保存失败不能阻止作用域释放或进程退出；错误仅使用固定代码记录。
            Console.Error.WriteLine(
                $"DockLayout errorCode=LAYOUT_SAVE_FAILED stableId=- type={exception.GetType().Name}");
        }
    }

    internal static DockLayoutSnapshotV1 Capture(
        IRootDock root,
        ManagementFactory factory)
    {
        var mainDockables = EnumerateDockables(root).ToArray();
        var allToolDocks = mainDockables
            .OfType<IToolDock>()
            .ToArray();
        var floatingWindows = root.Windows?.ToArray() ?? [];
        var hidden = mainDockables
            .OfType<IRootDock>()
            .SelectMany(dock => dock.HiddenDockables ?? [])
            .ToHashSet(ReferenceEqualityComparer.Instance);

        var placements = new Dictionary<string, ToolPlacement>(StringComparer.Ordinal);
        foreach (var pair in factory.CreatedTools)
        {
            var tool = pair.Value;
            var floatingWindow = floatingWindows.FirstOrDefault(window =>
                window.Layout is not null &&
                EnumerateDockables(window.Layout).Any(candidate =>
                    ReferenceEquals(candidate, tool)));
            var currentDock = allToolDocks.FirstOrDefault(dock =>
                dock.VisibleDockables?.Any(candidate =>
                    ReferenceEquals(candidate, tool)) == true);
            var dockId = ResolveStableDockId(currentDock) ??
                         ResolveStableDockId(tool.OriginalOwner as IToolDock) ??
                         GetDefaultDockId(factory, tool.Id);
            var isFloating = floatingWindow is not null;
            var isVisible = isFloating || currentDock is not null;

            placements.Add(
                pair.Key,
                new ToolPlacement(
                    tool,
                    dockId,
                    isVisible && !hidden.Contains(tool),
                    false,
                    null));
        }

        var toolSnapshots = new List<DockToolSnapshotV1>(placements.Count);
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
            var order = visibleOrder
                .Concat(placementGroup.Select(placement => placement.Tool.Id))
                .Distinct(StringComparer.Ordinal)
                .Select((id, index) => (id, index))
                .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);

            foreach (var placement in placementGroup)
            {
                toolSnapshots.Add(new DockToolSnapshotV1
                {
                    Id = placement.Tool.Id,
                    DockId = dockId,
                    Order = order[placement.Tool.Id],
                    IsVisible = placement.IsVisible,
                    IsFloating = placement.IsFloating,
                    FloatingBounds = placement.FloatingBounds
                });
            }
        }

        var panes = CapturePaneSnapshots(
            mainDockables,
            allToolDocks,
            factory);

        return new DockLayoutSnapshotV1
        {
            Panes = panes,
            Tools = toolSnapshots,
            ActiveToolId = FindActiveToolId(root)
        };
    }

    /// <summary>
    /// 修复旧版仅支持 Left/Right 时被错误记录到 LeftTools 的 Top/Bottom 工具。
    /// </summary>
    internal static DockLayoutSnapshotV1 NormalizeLegacyTwoWaySnapshot(
        DockLayoutSnapshotV1 snapshot,
        ManagementFactory factory)
    {
        var hasFourWayMarker =
            snapshot.Panes.Any(pane =>
                pane.Id is DockLayoutIds.TopPane or DockLayoutIds.BottomPane) ||
            snapshot.Tools.Any(tool =>
                tool.DockId is DockLayoutIds.TopTools or DockLayoutIds.BottomTools);
        if (hasFourWayMarker)
        {
            return snapshot;
        }

        var indexedTools = snapshot.Tools
            .Select((tool, index) => (Tool: tool, OriginalIndex: index))
            .ToArray();
        var changed = false;

        for (var index = 0; index < indexedTools.Length; index++)
        {
            var entry = indexedTools[index];
            var alignment = factory.GetToolAlignment(entry.Tool.Id);
            if (alignment is not (Alignment.Top or Alignment.Bottom))
            {
                continue;
            }

            indexedTools[index] =
            (
                entry.Tool with
                {
                    DockId = ToolDockPlacement.GetDockId(alignment),
                    IsVisible = true,
                    IsFloating = false,
                    FloatingBounds = null
                },
                entry.OriginalIndex
            );
            changed = true;
        }

        if (!changed)
        {
            return snapshot;
        }

        var dockIds = DockLayoutIds.ToolDockIds
            .Concat(indexedTools.Select(entry => entry.Tool.DockId))
            .Distinct(StringComparer.Ordinal);
        var normalizedTools = new List<DockToolSnapshotV1>(indexedTools.Length);
        foreach (var dockId in dockIds)
        {
            var order = 0;
            foreach (var entry in indexedTools
                         .Where(entry => entry.Tool.DockId == dockId)
                         .OrderBy(entry => entry.Tool.Order)
                         .ThenBy(entry => entry.OriginalIndex))
            {
                normalizedTools.Add(entry.Tool with { Order = order++ });
            }
        }

        return snapshot with { Tools = normalizedTools };
    }

    private static DockLayoutValidationError? ValidateAgainstRuntime(
        DockLayoutSnapshotV1 snapshot,
        IRootDock root,
        ManagementFactory factory)
    {
        var tools = factory.CreatedTools;
        foreach (var tool in snapshot.Tools)
        {
            if (!tools.ContainsKey(tool.Id))
            {
                return new("LAYOUT_PLUGIN_MISSING", tool.Id);
            }
        }

        var dockables = EnumerateDockables(root).ToArray();
        var runtimeIds = dockables
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

    internal static void ApplySnapshot(
        DockLayoutSnapshotV1 snapshot,
        IRootDock root,
        ManagementFactory factory)
    {
        EnsureSnapshotDocks(snapshot, root, factory);

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

        // V1 快照可能来自仍支持浮动窗口的旧版本。无论旧状态是否浮动，
        // 都按其稳定 DockId 和顺序放回主窗体；下次保存时会归一化为非浮动状态。
        foreach (var group in snapshot.Tools
                     .GroupBy(tool => tool.DockId, StringComparer.Ordinal))
        {
            var targetDock = toolDocks[group.Key];
            var orderedTools = group
                .OrderBy(tool => tool.Order)
                .Select(tool => factory.CreatedTools[tool.Id])
                .ToArray();
            for (var index = 0; index < orderedTools.Length; index++)
            {
                var tool = orderedTools[index];
                factory.RemoveDockable(tool, collapse: false);
                factory.InsertDockable(targetDock, tool, index);
            }
        }

        foreach (var toolState in snapshot.Tools.Where(tool => !tool.IsVisible))
        {
            factory.HideDockable(factory.CreatedTools[toolState.Id]);
        }

        if (snapshot.ActiveToolId is { } activeToolId)
        {
            var activeState = snapshot.Tools.Single(tool => tool.Id == activeToolId);
            if (activeState.IsVisible)
            {
                factory.SetActiveDockable(factory.CreatedTools[activeToolId]);
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
        ManagementFactory factory,
        string toolId) =>
        ToolDockPlacement.GetDockId(factory.GetToolAlignment(toolId));

    private static void EnsureSnapshotDocks(
        DockLayoutSnapshotV1 snapshot,
        IRootDock root,
        ManagementFactory factory)
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
            factory.EnsureToolDock(root, alignment);
        }
    }

    private static List<DockPaneSnapshotV1> CapturePaneSnapshots(
        IReadOnlyCollection<IDockable> dockables,
        IReadOnlyCollection<IToolDock> toolDocks,
        ManagementFactory factory)
    {
        var createdTools = factory.CreatedTools.Values
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var panes = new List<DockPaneSnapshotV1>();

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
            panes.Add(new DockPaneSnapshotV1
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

    private sealed record ToolPlacement(
        Tool Tool,
        string DockId,
        bool IsVisible,
        bool IsFloating,
        DockFloatingBoundsV1? FloatingBounds);
}
