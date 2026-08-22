using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>
/// 把 Workspace Session 中的 Tool 运行时事实投影为不含 Dock 类型的只读状态。
/// </summary>
/// <remarks>
/// 本类型只做查询，不修改布局。Tool 的显隐命令仍由拥有工作区事务边界的
/// <see cref="WorkspaceSession"/> 执行，避免 ReadModel 同时承担命令职责。
/// </remarks>
internal sealed class ToolWorkspaceReadModel(WorkspaceSession session)
{
    private readonly WorkspaceSession _session = session ??
        throw new ArgumentNullException(nameof(session));

    /// <summary>捕获当前可管理 Tool 的确定性只读快照。</summary>
    internal IReadOnlyList<ToolWorkspaceState> Capture()
    {
        var root = _session.RootDock;
        var tools = _session.CreatedTools;
        var states = new List<ToolWorkspaceState>();
        foreach (var descriptor in _session.GetAvailableToolDescriptors().Values
                     .Where(item => item.ToolTypeId != HostExtensionIds.V2ToolManagement)
                     .OrderBy(item => item.ToolTypeId.Value, StringComparer.Ordinal))
        {
            if (!tools.TryGetValue(descriptor.ToolTypeId.Value, out var tool))
            {
                continue;
            }

            // Pinned Tool 已经属于用户可访问的展示状态，不能因不在普通 ToolDock 中而误报隐藏。
            var isHidden = root is not null && DockTreeNavigator.Enumerate(root)
                .OfType<Dock.Model.Controls.IRootDock>()
                .Any(candidate => candidate.HiddenDockables?.Contains(tool) == true);
            var isVisible = root is null ||
                (!isHidden && (
                    DockTreeNavigator.FindToolDock(root, tool) is not null ||
                    DockTreeNavigator.IsToolPinned(root, tool)));
            var canHide = tool is ManagedToolDockable adapter
                ? adapter.Registration.Descriptor.CloseBehavior == ToolCloseBehavior.Hide
                : tool.CanClose;
            states.Add(new ToolWorkspaceState(
                descriptor.ToolTypeId.Value,
                descriptor.DisplayName,
                isVisible,
                canHide));
        }
        return states;
    }
}
