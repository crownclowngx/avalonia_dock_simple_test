using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 保存当前会话的工具恢复记录，并把 Dock 可见结构投影为纯数据。它不拥有业务实例或文件句柄；
/// 弱身份表只为运行期容器分配稳定布局 ID，窗口关闭后不会因保存记录而保留原生对象。
/// </summary>
internal sealed partial class DockLayoutWorkspaceState
{
    private readonly ConditionalWeakTable<IDockable, LayoutIdentity> _nodeIds = new();
    private readonly ConditionalWeakTable<IDockWindow, LayoutIdentity> _windowIds = new();
    internal DockLayoutSnapshotV3? Remembered { get; private set; }

    internal void Remember(DockLayoutSnapshotV3 snapshot)
    {
        DockLayoutV3Validator.Validate(snapshot);
        Remembered = snapshot;
    }

    /// <summary>必须在 UI 线程完成捕获；返回值不含 Window、Dock、View 或插件 payload。</summary>
    internal DockLayoutSnapshotV3 Capture(WorkspaceSession session)
    {
        var root = session.RootDock ?? throw new InvalidOperationException("工作区尚未创建。");
        var known = session.CreatedTools.Values.Where(tool => session.IsToolAvailable(tool.Id)).ToHashSet(ReferenceEqualityComparer.Instance);
        var primary = session.DockFactory.GetDockable<IDocumentDock>(DockLayoutIds.Documents);
        var context = session.DockFactory.WindowContext;
        var previousTools = Remembered?.Tools.ToDictionary(tool => tool.Id, StringComparer.Ordinal) ?? [];
        var pinned = new Dictionary<IDockable, (string Dock, int Order)>(ReferenceEqualityComparer.Instance);
        foreach (var windowRoot in DockTreeNavigator.EnumerateWorkspace(root).OfType<IRootDock>())
        {
            AddPinned(windowRoot.LeftPinnedDockables, DockLayoutIds.LeftTools);
            AddPinned(windowRoot.RightPinnedDockables, DockLayoutIds.RightTools);
            AddPinned(windowRoot.TopPinnedDockables, DockLayoutIds.TopTools);
            AddPinned(windowRoot.BottomPinnedDockables, DockLayoutIds.BottomTools);
        }
        var tools = session.CreatedTools.Values.Where(tool => session.IsToolAvailable(tool.Id)).Select(tool =>
        {
            var group = DockTreeNavigator.FindToolDock(root, tool) as IDock ??
                DockTreeNavigator.FindDocumentDock(root, tool);
            var previous = previousTools.GetValueOrDefault(tool.Id);
            var floating = DockTreeNavigator.FindWindow(root, tool) is not null;
            var returnDock = pinned.TryGetValue(tool, out var pin) ? pin.Dock :
                !floating && group is IToolDock toolDock ? ToolDockPlacement.GetDockId(toolDock.Alignment) :
                previous?.ReturnDockId ?? ToolDockPlacement.GetDockId(session.GetToolAlignment(tool.Id));
            var order = pinned.ContainsKey(tool) ? pin.Order : !floating && group?.VisibleDockables is { } visible
                ? Math.Max(0, visible.IndexOf(tool)) : previous?.ReturnOrder ?? 0;
            return new DockLayoutTool(tool.Id, pinned.ContainsKey(tool) ? "autoHidden" : group is null ? "hidden" : "visible", returnDock, order);
        }).ToArray();
        var main = new DockLayoutWindow("main", context.CaptureBounds(context.MainWindow),
            (Visit(root, main: true) ?? DockLayoutNode.Documents()) with { Proportion = 1 });
        var floatingWindows = new List<DockLayoutWindow>();
        foreach (var window in DockTreeNavigator.EnumerateWindows(root))
        {
            if (window.Layout is not { } layout || Visit(layout, main: false) is not { } node) continue;
            var bounds = window.Host is Avalonia.Controls.Window host ? context.CaptureBounds(host) :
                new DockWindowBounds(Finite(window.X, 80), Finite(window.Y, 80), Size(window.Width, 640), Size(window.Height, 480),
                    window.WindowState == DockWindowState.Maximized, null);
            floatingWindows.Add(new(GetWindowId(window), bounds, node with { Proportion = 1 }));
        }
        var result = DockLayoutTree.Merge(new(3, main, floatingWindows, tools), Remembered);
        Remembered = result;
        return result;

        void AddPinned(IList<IDockable>? items, string dock)
        {
            if (items is null) return;
            for (var index = 0; index < items.Count; index++) pinned[items[index]] = (dock, index);
        }

        DockLayoutNode? Visit(IDockable item, bool main)
        {
            if (item is not IDock dock) return null;
            var proportion = double.IsFinite(item.Proportion) && item.Proportion > 0 ? Math.Min(1, item.Proportion) : 1;
            if (dock is IToolDock or IDocumentDock)
            {
                var ids = (dock.VisibleDockables ?? []).OfType<Tool>().Where(known.Contains).Select(tool => tool.Id).ToArray();
                DockLayoutNode? group = ids.Length == 0 ? null : DockLayoutNode.Group(GetNodeId(item), ids, proportion,
                    dock.ActiveDockable is Tool active && ids.Contains(active.Id) ? active.Id : null);
                if (!main || !ReferenceEquals(dock, primary)) return group;
                if (group is null) return DockLayoutNode.Documents() with { Proportion = proportion };
                return DockLayoutNode.Split(GetNodeId(item) + "-mixed", "horizontal",
                    [DockLayoutNode.Documents() with { Proportion = 0.5 }, group with { Proportion = 0.5 }], proportion);
            }
            var children = (dock.VisibleDockables ?? []).Select(child => Visit(child, main)).OfType<DockLayoutNode>().ToArray();
            var orientation = dock is IProportionalDock { Orientation: Dock.Model.Core.Orientation.Vertical } ? "vertical" : "horizontal";
            return DockLayoutTree.Collapse(DockLayoutNode.Split(GetNodeId(item), orientation, [], proportion), children);
        }
    }

    private string GetNodeId(IDockable node) => _nodeIds.GetValue(node, _ => new("node-" + Guid.NewGuid().ToString("N"))).Value;
    private string GetWindowId(IDockWindow window) => _windowIds.GetValue(window, _ => new("window-" + Guid.NewGuid().ToString("N"))).Value;
    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
    private static double Size(double value, double fallback) => double.IsFinite(value) && value > 0 ? value : fallback;
    private sealed record LayoutIdentity(string Value);
}
