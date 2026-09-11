using System;
using System.Linq;
using MyAvaloniaManagement.Business.Constants;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>只退役已知内置项，绝不把其他缺失贡献变成静默丢失。</summary>
internal static class RetiredToolLayoutMigration
{
    /// <summary>纯数据迁移：只移除已知退役项，保留其他工具的顺序、位置、显隐及固定状态。</summary>
    internal static DockLayoutSnapshotV2 Apply(DockLayoutSnapshotV2 snapshot)
    {
        if (!snapshot.Tools.Any(tool => RetiredHostToolIds.Contains(tool.Id)) &&
            !RetiredHostToolIds.Contains(snapshot.ActiveToolId)) return snapshot;
        return snapshot with
        {
            Tools = snapshot.Tools.Where(tool => !RetiredHostToolIds.Contains(tool.Id)).GroupBy(tool => tool.DockId, StringComparer.Ordinal)
                .SelectMany(group => group.OrderBy(tool => tool.Order).Select((tool, order) => tool with { Order = order })).ToList(),
            Panes = snapshot.Panes.Select(pane => pane with { }).ToList(),
            ActiveToolId = RetiredHostToolIds.Contains(snapshot.ActiveToolId) ? null : snapshot.ActiveToolId
        };
    }
}
