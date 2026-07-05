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
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.ViewModels.Tools;

public partial class ToolManagementViewModel : Tool
{
    // 工具管理项集合
    [ObservableProperty] private ObservableCollection<ToolManagementItem> _toolItems = new();

    // 当前工具ID，用于排除自身
    private string _currentToolId;

    public ToolManagementViewModel()
    {
        Id = DockNameConstant.ToolManagement;
        Title = "工具管理";
        _currentToolId = Id;
        CanClose = false;
        LoadTools();
    }

    /// <summary>
    /// 加载所有已注册的工具
    /// </summary>
    private void LoadTools()
    {
        var factory = ServiceProvider.GetRequiredService<ManagementFactory>();
        var toolManagementData = factory?.GetToolManagementData();

        // 清除现有项
        ToolItems.Clear();

        // 即使 RootDock 尚未初始化，也可以通过反射获取元数据和已创建工具来填充列表
        var toolMetadata = toolManagementData?.ToolMetadata 
            ?? GetFieldViaReflection<Dictionary<string, ToolMetadata>>(factory, "_toolMetadata");
        var createdTools = toolManagementData?.CreatedTools 
            ?? GetFieldViaReflection<Dictionary<string, Tool>>(factory, "_createdTools");

        if (toolMetadata == null || createdTools == null)
        {
            return;
        }

        // 添加所有工具（排除自身）
        foreach (var metadata in toolMetadata.Values.Where(m => m.ToolTypeId != _currentToolId))
        {
            if (!createdTools.TryGetValue(metadata.ToolTypeId, out var tool))
            {
                continue;
            }

            // 初始化工具可见状态：检查是否在 HiddenDockables 中
            bool isVisible = toolManagementData?.RootDock != null
                ? !IsToolHidden(toolManagementData.RootDock, tool)
                : true;

            ToolItems.Add(new ToolManagementItem
            {
                ToolId = metadata.ToolTypeId,
                DisplayName = metadata.DisplayName,
                IsVisible = isVisible,
                CanClose = tool.CanClose
            });
        }
    }

    /// <summary>
    /// 通过反射获取私有字段
    /// </summary>
    private static T? GetFieldViaReflection<T>(object? obj, string fieldName) where T : class
    {
        if (obj == null) return null;
        var field = obj.GetType().GetField(fieldName, 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(obj) as T;
    }

    /// <summary>
    /// 检查工具是否在 HiddenDockables 中，或不在任何 ToolDock 的 VisibleDockables 中
    /// </summary>
    private static bool IsToolHidden(IRootDock rootDock, IDockable tool)
    {
        // 如果在 HiddenDockables 中，则是被隐藏
        if (rootDock.HiddenDockables != null && rootDock.HiddenDockables.Contains(tool))
            return true;
        // 如果不在任何 ToolDock 的 VisibleDockables 中，也是被隐藏
        return FindToolDockContainingTool(rootDock, tool) == null;
    }

    /// <summary>
    /// 切换工具可见性命令
    /// </summary>
    [RelayCommand]
    public void ToggleToolVisibility(ToolManagementItem item)
    {
        if (item == null || !item.CanClose)
            return;

        var factory = ServiceProvider.GetRequiredService<ManagementFactory>();
        var toolManagementData = factory?.GetToolManagementData();
        if (toolManagementData == null)
        {
            return;
        }

        if (!toolManagementData.CreatedTools.TryGetValue(item.ToolId, out var tool))
        {
            return;
        }

        // 检查工具当前是否在某个 ToolDock 的 VisibleDockables 中
        var currentDock = FindToolDockContainingTool(toolManagementData.RootDock, tool);

        if (currentDock != null)
        {
            // 工具当前可见 → 隐藏它：从 VisibleDockables 中原地移除（不替换集合！）
            currentDock.VisibleDockables!.Remove(tool);

            // 处理 ActiveDockable 切换
            if (currentDock.ActiveDockable == tool)
            {
                currentDock.ActiveDockable = currentDock.VisibleDockables.Count > 0
                    ? currentDock.VisibleDockables[0]
                    : null;
            }

            item.IsVisible = false;
        }
        else
        {
            // 工具当前不可可见 → 显示它：找到正确的 ToolDock，原地 Add（不替换集合！）
            var targetDock = FindTargetToolDock(toolManagementData.RootDock, item.ToolId, toolManagementData.ToolMetadata);
            if (targetDock == null)
            {
                return;
            }

            // 如果工具在 HiddenDockables 中，先从中移除
            if (toolManagementData.RootDock.HiddenDockables != null &&
                toolManagementData.RootDock.HiddenDockables.Contains(tool))
            {
                toolManagementData.RootDock.HiddenDockables.Remove(tool);
            }

            // 确保不重复添加
            if (!targetDock.VisibleDockables!.Contains(tool))
            {
                targetDock.VisibleDockables.Add(tool);
            }

            // 设置 Owner 并激活
            tool.Owner = targetDock;
            targetDock.ActiveDockable = tool;

            item.IsVisible = true;
        }

        // 通知布局刷新
        ServiceProvider.GetRequiredService<IMessengerService>()?.Send(new UpdateLayoutMessage("UpdateLayout"));
    }

    /// <summary>
    /// 递归查找包含特定工具的 ToolDock
    /// </summary>
    private static ToolDock? FindToolDockContainingTool(IDock dock, IDockable tool)
    {
        if (dock is ToolDock toolDock &&
            toolDock.VisibleDockables != null &&
            toolDock.VisibleDockables.Contains(tool))
        {
            return toolDock;
        }

        if (dock.VisibleDockables != null)
        {
            foreach (var dockable in dock.VisibleDockables)
            {
                if (dockable is IDock childDock)
                {
                    var result = FindToolDockContainingTool(childDock, tool);
                    if (result != null) return result;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 根据工具的 Alignment 元数据查找目标 ToolDock
    /// </summary>
    private static ToolDock? FindTargetToolDock(IDock dock, string toolId, IReadOnlyDictionary<string, ToolMetadata> metadata)
    {
        var alignment = metadata.TryGetValue(toolId, out var meta) ? meta.Alignment : "Left";
        var targetAlignment = alignment.Equals("Right", StringComparison.OrdinalIgnoreCase)
            ? Alignment.Right
            : Alignment.Left;
        return FindToolDockByAlignment(dock, targetAlignment);
    }

    /// <summary>
    /// 根据对齐方式查找匹配的 ToolDock
    /// </summary>
    private static ToolDock? FindToolDockByAlignment(IDock dock, Alignment targetAlignment)
    {
        if (dock is ToolDock toolDock && toolDock.Alignment == targetAlignment)
        {
            return toolDock;
        }

        if (dock.VisibleDockables != null)
        {
            foreach (var dockable in dock.VisibleDockables)
            {
                if (dockable is IDock childDock)
                {
                    var result = FindToolDockByAlignment(childDock, targetAlignment);
                    if (result != null) return result;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 同步工具的实际可见状态
    /// 当初始化完成后调用此方法确保状态一致
    /// </summary>
    public void SyncToolsVisibility()
    {
        var factory = ServiceProvider.GetRequiredService<ManagementFactory>();
        var toolManagementData = factory?.GetToolManagementData();
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