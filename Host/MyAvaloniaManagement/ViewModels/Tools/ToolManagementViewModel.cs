using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Models.Tools;

namespace MyAvaloniaManagement.ViewModels.Tools;

/// <summary>Tool 管理器接受的最小状态同步端口。</summary>
/// <remarks>
/// Factory 只需要请求“重新读取 Dock 事实”，不需要知道复选框集合、命令或具体 ViewModel。
/// 该 internal 接口让直接回调保持窄依赖，同时不把宿主内部变化提升为 Plugin SDK 事件。
/// </remarks>
internal interface IToolVisibilityStateSink
{
    /// <summary>以当前 Dock 树为事实来源，重新投影全部 Tool 的可见状态。</summary>
    void SyncToolsVisibility();
}

/// <summary>
/// 展示宿主已注册工具，并把显隐意图交给唯一拥有 Dock 状态的 Factory。
/// </summary>
/// <remarks>
/// 工具可见性始终以 Dock 树为事实来源。用户命令只提交期望状态，Factory 完成 Dock 变更后
/// 再通过窄接口要求本对象重新投影，因此关闭按钮、欢迎页恢复和管理器切换共用同一提交路径。
/// </remarks>
internal sealed partial class ToolManagementViewModel : Tool, IToolVisibilityStateSink
{
    private readonly ManagementFactory _factory;

    /// <summary>获取或设置可由用户管理的工具项集合。</summary>
    [ObservableProperty]
    private ObservableCollection<ToolManagementItem> _toolItems = new();

    // 当前工具 ID 用于排除工具管理器自身；它不能管理或隐藏自己。
    private readonly string _currentToolId;

    /// <summary>使用显式 Factory 创建工具管理 ViewModel。</summary>
    public ToolManagementViewModel(ManagementFactory factory)
    {
        _factory = factory;
        Id = HostExtensionIds.ToolManagement.Value;
        Title = "工具管理";
        _currentToolId = Id;
        CanClose = false;
        LoadTools();
    }

    /// <summary>加载所有已注册且不是工具管理器自身的工具。</summary>
    /// <remarks>
    /// 布局建立后使用包含 RootDock 的管理数据；布局建立前使用内部只读注册快照。
    /// 这样既不依赖私有字段反射，也能提前构造工具列表，后续可见性仍以真实 Dock 树为准。
    /// </remarks>
    private void LoadTools()
    {
        var toolManagementData = _factory.GetToolManagementData();
        var registrySnapshot = _factory.GetToolRegistrySnapshot();

        ToolItems.Clear();

        var toolMetadata = toolManagementData?.ToolMetadata
            ?? registrySnapshot.ToolMetadata;
        var createdTools = toolManagementData?.CreatedTools
            ?? registrySnapshot.CreatedTools;

        foreach (var metadata in toolMetadata.Values.Where(
                     item => item.ToolTypeId.Value != _currentToolId))
        {
            if (!createdTools.TryGetValue(metadata.ToolTypeId.Value, out var tool))
            {
                continue;
            }

            var isVisible = toolManagementData?.RootDock is not null
                ? !IsToolHidden(toolManagementData.RootDock, tool)
                : true;

            ToolItems.Add(new ToolManagementItem
            {
                ToolId = metadata.ToolTypeId.Value,
                DisplayName = metadata.DisplayName,
                IsVisible = isVisible,
                CanClose = tool.CanClose
            });
        }
    }

    /// <summary>判断工具是否已进入隐藏集合，或已经脱离所有可见 ToolDock。</summary>
    private static bool IsToolHidden(IRootDock rootDock, IDockable tool)
    {
        if (rootDock.HiddenDockables?.Contains(tool) == true)
        {
            return true;
        }

        // 图钉收起的 Tool 仍属于已显示状态，不能把它误报为已关闭。
        if (DockTreeNavigator.IsToolPinned(rootDock, tool))
        {
            return false;
        }

        return DockTreeNavigator.FindToolDock(rootDock, tool) is null;
    }

    /// <summary>根据当前投影请求隐藏或恢复指定工具。</summary>
    /// <remarks>
    /// ViewModel 不直接修改 Dock 集合。Factory 在完整提交显隐变化后同步所有 Tool 投影并通知
    /// 主窗口刷新布局绑定；失败或不可关闭项不会产生状态和布局通知。
    /// </remarks>
    [RelayCommand]
    public void ToggleToolVisibility(ToolManagementItem item)
    {
        if (item is null || !item.CanClose)
        {
            return;
        }

        _factory.TrySetToolVisibility(item.ToolId, !item.IsVisible);
    }

    /// <summary>以当前 Dock 树为准同步所有工具项的实际可见状态。</summary>
    public void SyncToolsVisibility()
    {
        var toolManagementData = _factory.GetToolManagementData();
        if (toolManagementData is null)
        {
            return;
        }

        foreach (var item in ToolItems)
        {
            if (!toolManagementData.CreatedTools.TryGetValue(item.ToolId, out var tool))
            {
                continue;
            }

            var actualVisibility = !IsToolHidden(toolManagementData.RootDock, tool);
            if (item.IsVisible != actualVisibility)
            {
                item.IsVisible = actualVisibility;
            }
        }
    }
}
