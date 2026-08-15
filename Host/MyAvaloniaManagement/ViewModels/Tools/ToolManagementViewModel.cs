using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.ViewModels.Tools;

/// <summary>
/// 展示宿主已注册工具，并协调工具的隐藏、恢复和实际可见状态。
/// </summary>
/// <remarks>
/// 工具可见性以 Dock 树为事实来源，消息只承担变化通知。
/// 这样无论变化来自本工具、关闭按钮还是外部布局恢复，界面状态都能重新同步。
/// </remarks>
internal sealed partial class ToolManagementViewModel : Tool
{
    private readonly ManagementFactory _factory;
    private readonly IMessengerService _messengerService;

    /// <summary>
    /// 获取或设置可由用户管理的工具项集合。
    /// </summary>
    [ObservableProperty] private ObservableCollection<ToolManagementItem> _toolItems = new();

    // 当前工具ID，用于排除自身
    private string _currentToolId;

    /// <summary>
    /// 使用显式工厂和消息服务创建工具管理 ViewModel。
    /// </summary>
    internal ToolManagementViewModel(
        ManagementFactory factory,
        IMessengerService messengerService)
    {
        _factory = factory;
        _messengerService = messengerService;
        Id = HostExtensionIds.ToolManagement.Value;
        Title = "工具管理";
        _currentToolId = Id;
        CanClose = false;
        LoadTools();
        RegisterMessages();
    }

    /// <summary>
    /// 注册消息监听：当工具被外部隐藏/恢复时同步状态
    /// </summary>
    /// <remarks>
    /// 注册失败只可能出现在设计器或服务尚未初始化的兼容路径，
    /// 此时仍可在布局建立后通过 <see cref="SyncToolsVisibility"/> 主动同步。
    /// </remarks>
    private void RegisterMessages()
    {
        try
        {
            _messengerService.Register<ToolManagementViewModel, ToolVisibilityChangedMessage>(
                this,
                (recipient, _) =>
                {
                    recipient.SyncToolsVisibility();
                });
        }
        catch
        {
            // 服务未初始化时忽略，稍后 InitLayout 完成后可手动同步
        }
    }

    /// <summary>
    /// 加载所有已注册且不是工具管理器自身的工具。
    /// </summary>
    /// <remarks>
    /// 布局建立后使用包含 RootDock 的管理数据；布局建立前使用内部只读注册快照。
    /// 这样既不依赖私有字段反射，也能提前构造工具列表，后续可见性仍以真实 Dock 树为准。
    /// </remarks>
    private void LoadTools()
    {
        var toolManagementData = _factory.GetToolManagementData();
        var registrySnapshot = _factory.GetToolRegistrySnapshot();

        // 清除现有项
        ToolItems.Clear();

        // RootDock 尚未初始化时使用工厂提供的只读注册快照。
        var toolMetadata = toolManagementData?.ToolMetadata 
            ?? registrySnapshot.ToolMetadata;
        var createdTools = toolManagementData?.CreatedTools 
            ?? registrySnapshot.CreatedTools;

        // 添加所有工具（排除自身）
        foreach (var metadata in toolMetadata.Values.Where(m => m.ToolTypeId.Value != _currentToolId))
        {
            if (!createdTools.TryGetValue(metadata.ToolTypeId.Value, out var tool))
            {
                continue;
            }

            // 初始化工具可见状态：检查是否在 HiddenDockables 中
            bool isVisible = toolManagementData?.RootDock != null
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

    /// <summary>
    /// 判断工具是否已进入隐藏集合，或已经脱离所有可见 ToolDock。
    /// </summary>
    private static bool IsToolHidden(IRootDock rootDock, IDockable tool)
    {
        // 如果在 HiddenDockables 中，则是被隐藏
        if (rootDock.HiddenDockables != null && rootDock.HiddenDockables.Contains(tool))
            return true;
        // 图钉收起的工具位于 RootDock 的 PinnedDockables 集合中，仍属于已显示状态
        if (DockTreeNavigator.IsToolPinned(rootDock, tool))
            return false;
        // 如果不在任何 ToolDock 的 VisibleDockables 中，也是被隐藏
        return DockTreeNavigator.FindToolDock(rootDock, tool) == null;
    }

    /// <summary>
    /// 根据当前 Dock 树状态隐藏或恢复指定工具。
    /// </summary>
    /// <remarks>
    /// 不可关闭工具受到保护；恢复失败时不提前修改界面状态。
    /// 成功变化后发布布局刷新消息，使其他视图观察到同一状态。
    /// </remarks>
    [RelayCommand]
    public void ToggleToolVisibility(ToolManagementItem item)
    {
        if (item == null || !item.CanClose)
            return;

        var toolManagementData = _factory.GetToolManagementData();
        if (toolManagementData == null)
        {
            return;
        }

        if (!toolManagementData.CreatedTools.TryGetValue(item.ToolId, out var tool))
        {
            return;
        }

        // 检查工具当前是否在某个 ToolDock 的 VisibleDockables 中
        var currentDock = DockTreeNavigator.FindToolDock(
            toolManagementData.RootDock,
            tool);
        var isPinned = DockTreeNavigator.IsToolPinned(
            toolManagementData.RootDock,
            tool);

        if (currentDock != null || isPinned)
        {
            var nextActive = currentDock?.VisibleDockables?
                .FirstOrDefault(candidate => !ReferenceEquals(candidate, tool));
            _factory.HideDockable(tool);
            if (currentDock is not null)
            {
                currentDock.ActiveDockable = nextActive;
            }
            item.IsVisible = false;
        }
        else
        {
            if (!_factory.RestoreTool(toolManagementData.RootDock, tool))
            {
                return;
            }

            item.IsVisible = true;
        }

        // 通知布局刷新
        _messengerService.Send(new UpdateLayoutMessage("UpdateLayout"));
    }

    /// <summary>
    /// 以当前 Dock 树为准同步所有工具项的实际可见状态。
    /// </summary>
    public void SyncToolsVisibility()
    {
        var toolManagementData = _factory.GetToolManagementData();
        if (toolManagementData == null) return;

        foreach (var item in ToolItems)
        {
            if (!toolManagementData.CreatedTools.TryGetValue(item.ToolId, out var tool))
            {
                continue;
            }

            bool actualVisibility = !IsToolHidden(toolManagementData.RootDock, tool);
            if (item.IsVisible != actualVisibility)
            {
                item.IsVisible = actualVisibility;
            }
        }
    }
}
