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
    private static readonly string[] PersistedPaneIds =
    [
        DockLayoutIds.LeftPane,
        DockLayoutIds.RightPane
    ];

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
        var mainToolDocks = mainDockables
            .OfType<IToolDock>()
            .Where(dock => dock.Id is DockLayoutIds.LeftTools or DockLayoutIds.RightTools)
            .ToDictionary(dock => dock.Id, StringComparer.Ordinal);
        var floatingWindows = root.Windows?.ToArray() ?? [];
        var hidden = root.HiddenDockables is null
            ? new HashSet<IDockable>(ReferenceEqualityComparer.Instance)
            : new HashSet<IDockable>(
                root.HiddenDockables,
                ReferenceEqualityComparer.Instance);

        var placements = new Dictionary<string, ToolPlacement>(StringComparer.Ordinal);
        foreach (var pair in factory.CreatedTools)
        {
            var tool = pair.Value;
            var floatingWindow = floatingWindows.FirstOrDefault(window =>
                window.Layout is not null &&
                EnumerateDockables(window.Layout).Any(candidate =>
                    ReferenceEquals(candidate, tool)));
            var currentDock = mainToolDocks.Values.FirstOrDefault(dock =>
                dock.VisibleDockables?.Any(candidate =>
                    ReferenceEquals(candidate, tool)) == true);
            var originalDockId = tool.OriginalOwner?.Id;
            var dockId = currentDock?.Id ??
                         (IsKnownToolDockId(originalDockId)
                             ? originalDockId!
                             : GetDefaultDockId(factory, tool.Id));
            var isFloating = floatingWindow is not null;
            var isVisible = isFloating || currentDock is not null;

            placements.Add(
                pair.Key,
                new ToolPlacement(
                    tool,
                    dockId,
                    isVisible && !hidden.Contains(tool),
                    isFloating,
                    floatingWindow is null
                        ? null
                        : new DockFloatingBoundsV1
                        {
                            X = floatingWindow.X,
                            Y = floatingWindow.Y,
                            Width = floatingWindow.Width,
                            Height = floatingWindow.Height
                        }));
        }

        var toolSnapshots = new List<DockToolSnapshotV1>(placements.Count);
        foreach (var dockId in new[] { DockLayoutIds.LeftTools, DockLayoutIds.RightTools })
        {
            var placementGroup = placements.Values
                .Where(placement => placement.DockId == dockId)
                .ToArray();
            var visibleOrder = mainToolDocks.GetValueOrDefault(dockId)?
                .VisibleDockables?
                .OfType<Tool>()
                .Select(tool => tool.Id)
                .ToArray() ?? [];
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

        var panes = mainDockables
            .Where(dockable =>
                dockable.Id is not null &&
                PersistedPaneIds.Contains(dockable.Id, StringComparer.Ordinal) &&
                double.IsFinite(dockable.Proportion))
            .Select(dockable => new DockPaneSnapshotV1
            {
                Id = dockable.Id,
                Proportion = dockable.Proportion
            })
            .ToList();

        return new DockLayoutSnapshotV1
        {
            Panes = panes,
            Tools = toolSnapshots,
            ActiveToolId = FindActiveToolId(root)
        };
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
                tool.DockId is not (DockLayoutIds.LeftTools or DockLayoutIds.RightTools))
            {
                return new("LAYOUT_TOOL_DOCK_MISSING", tool.Id);
            }
        }

        return null;
    }

    private static void ApplySnapshot(
        DockLayoutSnapshotV1 snapshot,
        IRootDock root,
        ManagementFactory factory)
    {
        var dockables = EnumerateDockables(root).ToArray();
        var paneMap = dockables
            .Where(dockable => !string.IsNullOrWhiteSpace(dockable.Id))
            .ToDictionary(dockable => dockable.Id, StringComparer.Ordinal);
        var toolDocks = dockables
            .OfType<IToolDock>()
            .Where(dock => dock.Id is DockLayoutIds.LeftTools or DockLayoutIds.RightTools)
            .ToDictionary(dock => dock.Id, StringComparer.Ordinal);

        foreach (var pane in snapshot.Panes)
        {
            paneMap[pane.Id].Proportion = pane.Proportion;
        }

        foreach (var group in snapshot.Tools
                     .Where(tool => !tool.IsFloating)
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

        foreach (var toolState in snapshot.Tools.Where(tool => tool.IsFloating))
        {
            var tool = factory.CreatedTools[toolState.Id];
            factory.FloatDockable(tool);
            var window = root.Windows?
                .LastOrDefault(candidate =>
                    candidate.Layout is not null &&
                    EnumerateDockables(candidate.Layout).Any(dockable =>
                        ReferenceEquals(dockable, tool)))
                ?? throw new InvalidOperationException(
                    $"工具 {tool.Id} 浮动后没有对应 DockWindow。");
            var bounds = toolState.FloatingBounds!;
            window.X = bounds.X;
            window.Y = bounds.Y;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
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
        factory.GetToolAlignment(toolId).Equals(
            "Right",
            StringComparison.OrdinalIgnoreCase)
            ? DockLayoutIds.RightTools
            : DockLayoutIds.LeftTools;

    private static bool IsKnownToolDockId(string? id) =>
        id is DockLayoutIds.LeftTools or DockLayoutIds.RightTools;

    private sealed record ToolPlacement(
        Tool Tool,
        string DockId,
        bool IsVisible,
        bool IsFloating,
        DockFloatingBoundsV1? FloatingBounds);
}
