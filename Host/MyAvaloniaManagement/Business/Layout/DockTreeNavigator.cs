using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 集中提供 Dock 对象图的纯查询以及集合引用清理操作。
/// 统一树遍历规则，避免多个 ViewModel 和协调器各自实现不同的可见性判断。
/// </summary>
internal static class DockTreeNavigator
{
    /// <summary>
    /// 遍历整个工作区的可见结构和浮窗根。只跟随 children/Windows，按引用去重；绝不沿 Owner 回边。
    /// 原 Enumerate/FindDockById 保持单窗口含义，稳定主骨架查询不能意外命中浮窗。
    /// </summary>
    internal static IEnumerable<IDockable> EnumerateWorkspace(IDockable root)
    {
        var seen = new HashSet<IDockable>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<IDockable>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node)) continue;
            yield return node;
            if (node is IRootDock { Windows: { } windows })
                foreach (var window in windows.Reverse())
                    if (window.Layout is { } layout) pending.Push(layout);
            if (node is IDock { VisibleDockables: { } children })
                foreach (var child in children.Reverse()) pending.Push(child);
        }
    }

    internal static IEnumerable<IDockWindow> EnumerateWindows(IDockable root) => EnumerateWorkspace(root)
        .OfType<IRootDock>().SelectMany(r => r.Windows ?? []).Distinct();

    /// <summary>返回实际承载目标的浮窗；主窗口中的目标返回 null，不按标题匹配。</summary>
    internal static IDockWindow? FindWindow(IDockable root, IDockable target) => EnumerateWindows(root)
        .FirstOrDefault(w => w.Layout is { } layout && Enumerate(layout).Any(n => ReferenceEquals(n, target)));

    internal static IEnumerable<IDockable> Enumerate(IDockable root)
    {
        yield return root;
        if (root is not IDock { VisibleDockables: { } children })
        {
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var descendant in Enumerate(child))
            {
                yield return descendant;
            }
        }
    }

    internal static T? FindDockById<T>(IDock root, string id)
        where T : class, IDock
    {
        if (root is T typed && string.Equals(root.Id, id, StringComparison.Ordinal))
        {
            return typed;
        }

        return root.VisibleDockables?
            .OfType<IDock>()
            .Select(child => FindDockById<T>(child, id))
            .FirstOrDefault(result => result is not null);
    }

    internal static bool IsDockAttached(IDock root, IDock target) =>
        EnumerateWorkspace(root).Any(node => ReferenceEquals(node, target));

    internal static bool IsDockableAttached(IDock root, IDockable target) =>
        EnumerateWorkspace(root).Any(node => ReferenceEquals(node, target));

    internal static bool IsToolPinned(IDock dock, IDockable tool)
    {
        if (dock is IRootDock root &&
            (root.LeftPinnedDockables?.Contains(tool) == true ||
             root.RightPinnedDockables?.Contains(tool) == true ||
             root.TopPinnedDockables?.Contains(tool) == true ||
             root.BottomPinnedDockables?.Contains(tool) == true))
        {
            return true;
        }

        return dock.VisibleDockables?
            .OfType<IDock>()
            .Any(child => IsToolPinned(child, tool)) == true;
    }

    internal static ToolDock? FindToolDock(IDock dock, IDockable tool)
    {
        return EnumerateWorkspace(dock).OfType<ToolDock>()
            .FirstOrDefault(group => group.VisibleDockables?.Contains(tool) == true);
    }

    internal static IDocumentDock? FindDocumentDock(
        IDock dock,
        IDockable document)
    {
        return EnumerateWorkspace(dock).OfType<IDocumentDock>()
            .FirstOrDefault(group => group.VisibleDockables?.Contains(document) == true);
    }

    internal static void RemoveFromHiddenDockables(
        IDock root,
        IDockable dockable)
    {
        if (root is IRootDock { HiddenDockables: { } hidden })
        {
            hidden.Remove(dockable);
        }

        if (root.VisibleDockables is null)
        {
            return;
        }

        foreach (var child in root.VisibleDockables.OfType<IDock>())
        {
            RemoveFromHiddenDockables(child, dockable);
        }
    }
}
