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
        ReferenceEquals(root, target) ||
        root.VisibleDockables?
            .OfType<IDock>()
            .Any(child => IsDockAttached(child, target)) == true;

    internal static bool IsDockableAttached(IDock root, IDockable target) =>
        root.VisibleDockables?.Any(dockable =>
            ReferenceEquals(dockable, target) ||
            dockable is IDock child && IsDockableAttached(child, target)) == true;

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
        if (dock is ToolDock toolDock &&
            toolDock.VisibleDockables?.Contains(tool) == true)
        {
            return toolDock;
        }

        return dock.VisibleDockables?
            .OfType<IDock>()
            .Select(child => FindToolDock(child, tool))
            .FirstOrDefault(result => result is not null);
    }

    internal static IDocumentDock? FindDocumentDock(
        IDock dock,
        IDockable document)
    {
        if (dock is IDocumentDock documentDock &&
            documentDock.VisibleDockables?.Contains(document) == true)
        {
            return documentDock;
        }

        return dock.VisibleDockables?
            .OfType<IDock>()
            .Select(child => FindDocumentDock(child, document))
            .FirstOrDefault(result => result is not null);
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
