using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Docking;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 只负责构建具有稳定 ID 的四向宿主工作区，不持有运行期行为。
/// 将结构创建与工具恢复、激活等状态操作分离，便于单独验证初始布局契约。
/// </summary>
internal sealed class DockWorkspaceBuilder(HostDockFactory factory)
{
    internal IRootDock CreateWorkspaceLayout(
        DocumentDock documentDock,
        IEnumerable<Tool> tools,
        Func<string, Alignment> getAlignment)
    {
        ArgumentNullException.ThrowIfNull(documentDock);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(getAlignment);

        var byAlignment = tools.ToLookup(tool => getAlignment(tool.Id));
        var left = byAlignment[Alignment.Left].ToList();
        var right = byAlignment[Alignment.Right].ToList();
        var top = byAlignment[Alignment.Top].ToList();
        var bottom = byAlignment[Alignment.Bottom].ToList();

        var leftPane = CreateToolPane(
            DockLayoutIds.LeftPane,
            DockLayoutIds.LeftTools,
            Alignment.Left,
            left,
            0.15);
        var rightPane = CreateToolPane(
            DockLayoutIds.RightPane,
            DockLayoutIds.RightTools,
            Alignment.Right,
            right,
            0.15);

        var workspaceColumns = new ProportionalDock
        {
            Id = DockLayoutIds.WorkspaceColumns,
            Orientation = Orientation.Horizontal,
            IsCollapsable = false,
            Proportion = double.NaN,
            VisibleDockables = factory.CreateList<IDockable>(
                leftPane,
                new ProportionalDockSplitter(),
                documentDock,
                new ProportionalDockSplitter(),
                rightPane),
            ActiveDockable = documentDock
        };

        var rows = new List<IDockable>();
        if (top.Count > 0)
        {
            rows.Add(CreateToolPane(
                DockLayoutIds.TopPane,
                DockLayoutIds.TopTools,
                Alignment.Top,
                top,
                0.20));
            rows.Add(new ProportionalDockSplitter());
        }

        rows.Add(workspaceColumns);
        if (bottom.Count > 0)
        {
            rows.Add(new ProportionalDockSplitter());
            rows.Add(CreateToolPane(
                DockLayoutIds.BottomPane,
                DockLayoutIds.BottomTools,
                Alignment.Bottom,
                bottom,
                0.20));
        }

        var workspaceRows = new ProportionalDock
        {
            Id = DockLayoutIds.WorkspaceRows,
            Orientation = Orientation.Vertical,
            IsCollapsable = false,
            Proportion = double.NaN,
            VisibleDockables = factory.CreateList<IDockable>([.. rows]),
            ActiveDockable = workspaceColumns
        };

        var windowLayout = factory.CreateRootDock();
        windowLayout.Id = DockLayoutIds.Workspace;
        windowLayout.Title = "Default";
        windowLayout.IsCollapsable = false;
        windowLayout.VisibleDockables = factory.CreateList<IDockable>(workspaceRows);
        windowLayout.ActiveDockable = workspaceRows;
        HostDockFactory.DisableFloating(windowLayout);

        var root = factory.CreateRootDock();
        root.Id = DockLayoutIds.Root;
        root.IsCollapsable = false;
        root.VisibleDockables = factory.CreateList<IDockable>(windowLayout);
        root.ActiveDockable = windowLayout;
        root.DefaultDockable = windowLayout;
        HostDockFactory.DisableFloating(root);
        return root;
    }

    internal ProportionalDock CreateToolPane(
        string paneId,
        string toolDockId,
        Alignment alignment,
        IReadOnlyList<Tool> tools,
        double proportion)
    {
        var toolDock = CreateStableToolDock(toolDockId, alignment, tools);
        return new ProportionalDock
        {
            Id = paneId,
            Proportion = proportion,
            CollapsedProportion = proportion,
            Orientation = Orientation.Vertical,
            IsCollapsable = true,
            VisibleDockables = factory.CreateList<IDockable>(toolDock),
            ActiveDockable = toolDock
        };
    }

    internal ToolDock CreateStableToolDock(
        string toolDockId,
        Alignment alignment,
        IReadOnlyList<Tool>? tools = null) =>
        new()
        {
            Id = toolDockId,
            ActiveDockable = tools?.FirstOrDefault(),
            VisibleDockables = tools is null
                ? factory.CreateList<IDockable>()
                : factory.CreateList<IDockable>([.. tools]),
            Alignment = ToolDockPlacement.NormalizeAlignment(alignment),
            GripMode = GripMode.Visible,
            IsCollapsable = true
        };
}
