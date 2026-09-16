using System;
using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>只转换已经严格验证的 V2 数据，不读取或修改旧文件，也不触发旧 Store 的坏文件隔离。</summary>
internal static class DockLayoutV2Migration
{
    internal static DockLayoutSnapshotV3 Convert(DockLayoutSnapshotV2 source)
    {
        if (DockLayoutSnapshotValidator.Validate(source) is { } error)
            throw new DockLayoutFormatException(error.Code, error.StableId);
        source = RetiredToolLayoutMigration.Apply(source);
        var tools = source.Tools.Select(t => new DockLayoutTool(t.Id,
            !t.IsVisible ? "hidden" : t.IsPinned ? "autoHidden" : "visible", t.DockId, t.Order)).ToArray();
        var groups = source.Tools.GroupBy(t => t.DockId).ToDictionary(g => g.Key, g =>
            DockLayoutNode.Group(g.Key, g.OrderBy(t => t.Order).Select(t => t.Id), 1,
                g.Any(t => t.Id == source.ActiveToolId && t.IsVisible && !t.IsPinned) ? source.ActiveToolId : null));
        var columns = new List<DockLayoutNode>();
        Add(columns, DockLayoutIds.LeftTools, DockLayoutIds.LeftPane);
        columns.Add(DockLayoutNode.Documents());
        Add(columns, DockLayoutIds.RightTools, DockLayoutIds.RightPane);
        var rows = new List<DockLayoutNode>();
        Add(rows, DockLayoutIds.TopTools, DockLayoutIds.TopPane);
        rows.Add(Split("WorkspaceColumns", "horizontal", columns));
        Add(rows, DockLayoutIds.BottomTools, DockLayoutIds.BottomPane);
        var result = new DockLayoutSnapshotV3(3, new("main", DockWindowBounds.Default,
            Split("WorkspaceRows", "vertical", rows)), [], tools);
        DockLayoutV3Validator.Validate(result);
        return result;

        void Add(List<DockLayoutNode> nodes, string dock, string pane)
        {
            if (groups.TryGetValue(dock, out var group)) nodes.Add(group with
                { Proportion = source.Panes.FirstOrDefault(p => p.Id == pane)?.Proportion ?? 0.2 });
        }
    }

    /// <summary>删除空分割后合并单子项；保留工具比例，中心内容占用其余空间。</summary>
    private static DockLayoutNode Split(string id, string orientation, List<DockLayoutNode> nodes)
    {
        if (nodes.Count == 1) return nodes[0] with { Proportion = 1 };
        var center = nodes.FindIndex(n => n.Kind != "tools");
        nodes[center] = nodes[center] with { Proportion = Math.Max(0.05, 1 - nodes.Where((_, i) => i != center).Sum(n => n.Proportion)) };
        var sum = nodes.Sum(n => n.Proportion);
        return DockLayoutNode.Split(id, orientation, nodes.Select(n => n with { Proportion = n.Proportion / sum }));
    }
}
