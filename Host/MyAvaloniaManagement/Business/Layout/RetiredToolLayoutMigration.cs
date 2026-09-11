using System;
using System.Linq;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>只退役已知内置项，绝不把其他缺失贡献变成静默丢失。</summary>
internal static class RetiredToolLayoutMigration
{
    internal const string ToolManagementId = "myavalonia.host.tool.management";
    internal static DockLayoutSnapshotV2 Apply(DockLayoutSnapshotV2 snapshot)
    {
        if (!snapshot.Tools.Any(tool => tool.Id == ToolManagementId)) return snapshot;
        return snapshot with
        {
            Tools = snapshot.Tools.Where(tool => tool.Id != ToolManagementId).GroupBy(tool => tool.DockId, StringComparer.Ordinal)
                .SelectMany(group => group.OrderBy(tool => tool.Order).Select((tool, order) => tool with { Order = order })).ToList(),
            Panes = snapshot.Panes.Select(pane => pane with { }).ToList(),
            ActiveToolId = snapshot.ActiveToolId == ToolManagementId ? null : snapshot.ActiveToolId
        };
    }
}
