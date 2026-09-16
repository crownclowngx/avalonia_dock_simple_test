using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Constants;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>布局树的纯数据操作。剔除空节点、合并单子项与比例归一化不访问 Dock 或插件。</summary>
internal static class DockLayoutTree
{
    internal static IEnumerable<DockLayoutNode> Enumerate(DockLayoutNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Enumerate(child)) yield return descendant;
    }

    internal static DockLayoutNode? Filter(DockLayoutNode node, ISet<string> toolIds, bool keepDocuments)
    {
        if (node.Kind == "documents") return keepDocuments ? node : null;
        if (node.Kind == "tools")
        {
            var ids = node.ToolIds.Where(toolIds.Contains).ToArray();
            return ids.Length == 0 ? null : node with
            {
                ToolIds = ids,
                ActiveToolId = node.ActiveToolId is { } active && ids.Contains(active) ? active : null,
            };
        }
        return Collapse(node, node.Children.Select(child => Filter(child, toolIds, keepDocuments)).OfType<DockLayoutNode>());
    }

    /// <summary>空分割删除，单子项继承父比例；剩余子项按其相对比例重算，不引入无效的零占比。</summary>
    internal static DockLayoutNode? Collapse(DockLayoutNode original, IEnumerable<DockLayoutNode> nodes)
    {
        var children = nodes.ToArray();
        if (children.Length == 0) return null;
        if (children.Length == 1) return children[0] with { Proportion = original.Proportion };
        var sum = children.Sum(child => child.Proportion);
        return original with { Children = children.Select(child => child with { Proportion = child.Proportion / sum }).ToArray() };
    }

    /// <summary>
    /// 合并运行时可见投影与上次恢复记录。实际可见工具的位置优先，隐藏或不可用项保留原窗口和组；
    /// 工具状态以当前已创建实例为准，不把插件缺失误记为用户隐藏或删除。
    /// live 可以暂时没有隐藏项的树位置，只有合并后的结果才是可写的完整快照。
    /// </summary>
    internal static DockLayoutSnapshotV3 Merge(DockLayoutSnapshotV3 live, DockLayoutSnapshotV3? remembered)
    {
        var tools = (remembered?.Tools ?? []).Concat(live.Tools)
            .Where(tool => !RetiredHostToolIds.Contains(tool.Id))
            .GroupBy(tool => tool.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var previousWindows = remembered is null ? [] : Windows(remembered).ToArray();
        var previousNodes = previousWindows.SelectMany(window => Enumerate(window.Root)).ToDictionary(node => node.Id, StringComparer.Ordinal);
        var liveIds = Windows(live).SelectMany(window => Enumerate(window.Root)).SelectMany(node => node.ToolIds).ToHashSet(StringComparer.Ordinal);
        var retained = tools.Keys.Where(id => !liveIds.Contains(id)).ToHashSet(StringComparer.Ordinal);
        var windows = Windows(live).Select(window => window with { Root = AddGroupMembers(window.Root) }).ToList();

        foreach (var previous in previousWindows)
        {
            var missing = Filter(previous.Root, retained, keepDocuments: false);
            if (missing is null) continue;
            var index = windows.FindIndex(window => window.Id == previous.Id);
            if (index < 0) windows.Add(previous with { Root = missing with { Proportion = 1 } });
            else windows[index] = windows[index] with { Root = MergeBranches(windows[index].Root, missing, previous.Root) with { Proportion = 1 } };
            retained.ExceptWith(Enumerate(missing).SelectMany(node => node.ToolIds));
        }

        // 首次出现但默认隐藏的新工具还没有历史位置。按声明的四向回停位置建立恢复组，
        // 后续捕获复用这些记录，不会在每次保存时为同一工具重新分配组。
        foreach (var group in retained.GroupBy(id => tools[id].ReturnDockId))
        {
            var node = DockLayoutNode.Group("group-" + Guid.NewGuid().ToString("N"), group.OrderBy(id => tools[id].ReturnOrder));
            var orientation = group.Key is DockLayoutIds.TopTools or DockLayoutIds.BottomTools ? "vertical" : "horizontal";
            windows[0] = windows[0] with
            {
                Root = group.Key is DockLayoutIds.LeftTools or DockLayoutIds.TopTools
                    ? Join(node, windows[0].Root, orientation) : Join(windows[0].Root, node, orientation),
            };
        }
        windows = windows.Select(window => window with { Root = ClearHiddenActive(window.Root) }).ToList();
        var result = new DockLayoutSnapshotV3(3, windows[0], windows.Skip(1).ToArray(), tools.Values.OrderBy(tool => tool.Id, StringComparer.Ordinal).ToArray());
        DockLayoutV3Validator.Validate(result);
        return result;

        DockLayoutNode ClearHiddenActive(DockLayoutNode node) => node with
        {
            ActiveToolId = node.ActiveToolId is { } id && tools[id].State == "visible" ? id : null,
            Children = node.Children.Select(ClearHiddenActive).ToArray(),
        };

        DockLayoutNode AddGroupMembers(DockLayoutNode node)
        {
            if (node.Kind == "tools" && previousNodes.TryGetValue(node.Id, out var previous) && previous.Kind == "tools")
            {
                var added = previous.ToolIds.Where(retained.Contains).ToArray();
                retained.ExceptWith(added);
                return node with { ToolIds = MergeOrder(node.ToolIds, added, previous.ToolIds) };
            }
            return node with { Children = node.Children.Select(AddGroupMembers).ToArray() };
        }
    }

    private static IEnumerable<DockLayoutWindow> Windows(DockLayoutSnapshotV3 snapshot) =>
        new[] { snapshot.MainWindow }.Concat(snapshot.FloatingWindows);

    private static IReadOnlyList<string> MergeOrder(IReadOnlyList<string> current, IEnumerable<string> missing, IReadOnlyList<string> previous)
    {
        var result = current.ToList();
        foreach (var id in missing)
        {
            var later = previous.SkipWhile(candidate => candidate != id).Skip(1).FirstOrDefault(result.Contains);
            result.Insert(later is null ? result.Count : result.IndexOf(later), id);
        }
        return result;
    }

    private static DockLayoutNode MergeBranches(DockLayoutNode current, DockLayoutNode missing, DockLayoutNode previous)
    {
        if (current.Id == previous.Id && current.Kind == "split" && previous.Kind == "split")
        {
            var old = Enumerate(previous).ToDictionary(node => node.Id, StringComparer.Ordinal);
            var children = current.Children.ToDictionary(child => child.Id, StringComparer.Ordinal);
            var missingIds = Enumerate(missing).SelectMany(node => node.ToolIds).ToHashSet(StringComparer.Ordinal);
            var missingChildren = previous.Children.Select(child => Filter(child, missingIds, keepDocuments: false)).OfType<DockLayoutNode>().ToArray();
            foreach (var child in missingChildren)
                children[child.Id] = children.TryGetValue(child.Id, out var visible)
                    ? MergeBranches(visible, child, old[child.Id]) : child;
            var order = MergeOrder(current.Children.Select(child => child.Id).ToArray(),
                missingChildren.Select(child => child.Id).Where(id => !current.Children.Any(child => child.Id == id)),
                previous.Children.SelectMany(Enumerate).Select(child => child.Id).ToArray());
            return Collapse(current, order.Select(id => children[id]))!;
        }
        if (Enumerate(previous).Any(node => node.Id == current.Id))
        {
            var missingIds = Enumerate(missing).SelectMany(node => node.ToolIds).ToHashSet(StringComparer.Ordinal);
            return Graft(previous)!;
            DockLayoutNode? Graft(DockLayoutNode node)
            {
                if (node.Id == current.Id)
                {
                    var remainder = Filter(node, missingIds, keepDocuments: false);
                    return (remainder is null ? current : MergeBranches(current, remainder, node)) with { Proportion = node.Proportion };
                }
                if (node.Kind != "split") return Filter(node, missingIds, keepDocuments: false);
                return Collapse(node, node.Children.Select(Graft).OfType<DockLayoutNode>());
            }
        }
        // 用户已重构整段可见结构，原父节点不再存在。保留完整缺失分支并邻接当前结构，
        // 不把不可用工具强行插回用户已经移动的可见组，也不丢弃其内部比例与分组。
        return Join(current, missing, previous.Orientation ?? "horizontal");
    }

    private static DockLayoutNode Join(DockLayoutNode first, DockLayoutNode second, string orientation) =>
        DockLayoutNode.Split("split-" + Guid.NewGuid().ToString("N"), orientation,
            [first with { Proportion = 0.5 }, second with { Proportion = 0.5 }]);
}
