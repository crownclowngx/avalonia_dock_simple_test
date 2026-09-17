using System;
using System.Collections.Generic;
using System.Linq;
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
        // 本次查询共享关系索引，不逐工具重扫浮窗。快照不跨调用保存，下一次读取自然看到布局变化。
        var snapshot = WorkspaceLayoutQuerySnapshot.Capture(session.RootDock);
        return session.GetRegisteredTools().Select(entry =>
        {
            var id = entry.Descriptor.ToolTypeId.Value;
            session.CreatedTools.TryGetValue(id, out var tool);
            ToolLayoutState? layout = tool is null || session.RootDock is null ? null :
                snapshot.IsHidden(tool) ? ToolLayoutState.Hidden : snapshot.IsPinned(tool) ? ToolLayoutState.AutoHidden :
                snapshot.IsDocked(tool) ? snapshot.FindWindow(tool) is not null ?
                    ToolLayoutState.Floating : ToolLayoutState.Docked : ToolLayoutState.Hidden;
            var visible = layout is ToolLayoutState.Docked or ToolLayoutState.AutoHidden or ToolLayoutState.Floating;
            var reason = !entry.IsAvailable ? entry.UnavailableReason : session.RootDock is null ? "工作区尚未就绪" :
                !session.CanOperateTools ? "工作区正在退出" : tool is null ? "工具激活失败，请查看插件诊断" : string.Empty;
            return new ToolWorkspaceState(id, entry.Descriptor.DisplayName, visible, visible && session.CanOperateTools)
            {
                Description = entry.Descriptor.Description,
                OwnerId = entry.OwnerId,
                SourceName = entry.SourceName,
                IconRequest = new HostIconRequest(entry.OwnerId, entry.Descriptor.IconPath),
                LayoutState = layout,
                IsActive = tool is not null && snapshot.IsActive(tool),
                CanOpen = entry.IsAvailable && tool is not null && session.CanOperateTools,
                UnavailableReason = reason
            };
        }).ToArray();
    }
}
