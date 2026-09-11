using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.Models.Tools;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>一次遍历捕获布局，再按稳定 ID 合并声明；不让未激活或不可用工具从目录无声消失。</summary>
internal sealed class ToolWorkspaceReadModel(WorkspaceSession session)
{
    internal event EventHandler? Changed { add => session.LayoutChanged += value; remove => session.LayoutChanged -= value; }
    internal bool CanOpen(string id) => session.CanOperateTools && session.IsToolAvailable(id) && session.CreatedTools.ContainsKey(id);
    internal IReadOnlyList<ToolWorkspaceState> Capture()
    {
        var nodes = session.RootDock is { } root ? DockTreeNavigator.Enumerate(root).ToArray() : [];
        var roots = nodes.OfType<IRootDock>().ToArray();
        var hidden = roots.SelectMany(item => item.HiddenDockables ?? []).ToHashSet();
        var pinned = roots.SelectMany(item =>
            (item.LeftPinnedDockables ?? []).Concat(item.RightPinnedDockables ?? [])
                .Concat(item.TopPinnedDockables ?? []).Concat(item.BottomPinnedDockables ?? [])).ToHashSet();
        var docked = nodes.OfType<IToolDock>().SelectMany(item => item.VisibleDockables ?? []).ToHashSet();
        var active = nodes.OfType<IDock>().Select(item => item.ActiveDockable).ToHashSet();
        return session.GetRegisteredTools().Select(entry =>
        {
            var id = entry.Descriptor.ToolTypeId.Value;
            session.CreatedTools.TryGetValue(id, out var tool);
            ToolLayoutState? layout = tool is null || session.RootDock is null ? null :
                hidden.Contains(tool) ? ToolLayoutState.Hidden : pinned.Contains(tool) ? ToolLayoutState.AutoHidden :
                docked.Contains(tool) ? ToolLayoutState.Docked : ToolLayoutState.Hidden;
            var visible = layout is ToolLayoutState.Docked or ToolLayoutState.AutoHidden;
            var reason = !entry.IsAvailable ? entry.UnavailableReason : session.RootDock is null ? "工作区尚未就绪" :
                !session.CanOperateTools ? "工作区正在退出" : tool is null ? "工具激活失败，请查看插件诊断" : string.Empty;
            return new ToolWorkspaceState(id, entry.Descriptor.DisplayName, visible, visible && session.CanOperateTools)
            {
                Description = entry.Descriptor.Description,
                OwnerId = entry.OwnerId,
                SourceName = entry.SourceName,
                IconRequest = new HostIconRequest(entry.OwnerId, entry.Descriptor.IconPath),
                LayoutState = layout,
                IsActive = tool is not null && active.Contains(tool),
                CanOpen = entry.IsAvailable && tool is not null && session.CanOperateTools,
                UnavailableReason = reason
            };
        }).ToArray();
    }
}
