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

            // 检查工具是否可见（在当前布局中）
            // 初始化阶段所有工具默认可见（它们刚被添加到布局中）
            bool isVisible = toolManagementData?.RootDock != null 
                ? IsToolVisible(tool) 
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
    /// 检查工具是否在布局中可见
    /// </summary>
    private bool IsToolVisible(IDockable tool)
    {
        var factory = ServiceProvider.GetRequiredService<ManagementFactory>();
        var rootDockField = typeof(ManagementFactory).GetField("_rootDock",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (rootDockField == null)
            return false;

        var rootDock = rootDockField.GetValue(factory) as IRootDock;
        if (rootDock == null)
            return false;

        // 递归检查整个布局树
        return IsToolInLayout(rootDock, tool);
    }

    /// <summary>
    /// 递归检查工具是否在布局中可见
    /// </summary>
    private bool IsToolInLayout(IDock dock, IDockable tool)
    {
        // 检查当前停靠点的VisibleDockables
        if (dock.VisibleDockables != null && dock.VisibleDockables.Contains(tool))
        {
            return true;
        }

        // 递归检查子停靠点
        if (dock.VisibleDockables != null)
        {
            foreach (var dockable in dock.VisibleDockables)
            {
                if (dockable is IDock childDock)
                {
                    if (IsToolInLayout(childDock, tool))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 切换工具可见性命令
    /// </summary>
    [RelayCommand]
    public void ToggleToolVisibility(ToolManagementItem item)
    {
        if (item == null || !item.CanClose)
            return;

        var toolManagementData = ServiceProvider.GetRequiredService<ManagementFactory>()?.GetToolManagementData();
        if (toolManagementData == null)
        {
            return;
        }

        if (!toolManagementData.CreatedTools.TryGetValue(item.ToolId, out var tool))
        {
            return;
        }

        // 查找包含此工具的ToolDock
        var toolDock = FindToolDockContainingTool(toolManagementData.RootDock, tool);

        // 如果没有找到包含该工具的ToolDock（工具已被关闭），尝试根据对齐方式查找匹配的ToolDock
        if (toolDock == null)
        {
            // 获取工具的元数据中的对齐方式
            var alignment = toolManagementData.ToolMetadata.TryGetValue(item.ToolId, out var metadata)
                ? metadata.Alignment
                : "Left";
            var targetAlignment = alignment.Equals("Right", StringComparison.OrdinalIgnoreCase)
                ? Alignment.Right
                : Alignment.Left;
            toolDock = FindToolDockByAlignment(toolManagementData.RootDock, targetAlignment);
        }

        // 最后的兜底：查找任何可用的ToolDock
        if (toolDock == null)
        {
            toolDock = FindAnyToolDock(toolManagementData.RootDock);
        }

        if (toolDock == null)
        {
            return;
        }

        bool isCurrentlyVisible = toolDock.VisibleDockables != null &&
                                  toolDock.VisibleDockables.Contains(tool);
        bool targetVisibility = !isCurrentlyVisible; // 反转当前状态

        if (targetVisibility)
        {
            // 显示工具：添加到ToolDock的VisibleDockables中
            if (toolDock.VisibleDockables != null && !toolDock.VisibleDockables.Contains(tool))
            {
                var updatedList = new List<IDockable>(toolDock.VisibleDockables);
                updatedList.Add(tool);
                toolDock.VisibleDockables = updatedList;

                // 设置为活动工具
                toolDock.ActiveDockable = tool;
            }
        }
        else
        {
            // 隐藏工具：从ToolDock的VisibleDockables中移除
            if (toolDock.VisibleDockables != null && toolDock.VisibleDockables.Contains(tool))
            {
                var updatedList = new List<IDockable>(toolDock.VisibleDockables);
                updatedList.Remove(tool);
                toolDock.VisibleDockables = updatedList;

                // 如果移除后没有工具，设置ActiveDockable为null
                if (toolDock.VisibleDockables.Count == 0)
                {
                    toolDock.ActiveDockable = null;
                }
                else if (toolDock.ActiveDockable == tool)
                {
                    // 如果移除的是当前活动工具，设置新的活动工具
                    toolDock.ActiveDockable = toolDock.VisibleDockables[0];
                }
            }
        }

        // 更新ToolManagementItem的IsVisible属性，确保UI状态与实际状态一致
        item.IsVisible = targetVisibility;

        ServiceProvider.GetRequiredService<IMessengerService>()?.Send(new UpdateLayoutMessage("UpdateLayout"));
    }

    /// <summary>
    /// 查找包含特定工具的ToolDock
    /// </summary>
    private ToolDock? FindToolDockContainingTool(IDock dock, IDockable tool)
    {
        // 检查当前停靠点是否是ToolDock且包含目标工具
        if (dock is ToolDock toolDock &&
            toolDock.VisibleDockables != null &&
            toolDock.VisibleDockables.Contains(tool))
        {
            return toolDock;
        }

        // 递归检查子停靠点
        if (dock.VisibleDockables != null)
        {
            foreach (var dockable in dock.VisibleDockables)
            {
                if (dockable is IDock childDock)
                {
                    var result = FindToolDockContainingTool(childDock, tool);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 查找任何可用的ToolDock
    /// </summary>
    private ToolDock? FindAnyToolDock(IDock dock)
    {
        if (dock is ToolDock toolDock)
        {
            return toolDock;
        }

        if (dock.VisibleDockables != null)
        {
            foreach (var dockable in dock.VisibleDockables)
            {
                if (dockable is IDock childDock)
                {
                    var result = FindAnyToolDock(childDock);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 根据对齐方式查找匹配的ToolDock
    /// </summary>
    private ToolDock? FindToolDockByAlignment(IDock dock, Alignment targetAlignment)
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
                    if (result != null)
                    {
                        return result;
                    }
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
        foreach (var item in ToolItems)
        {
            var factory = ServiceProvider.GetRequiredService<ManagementFactory>();
            var createdToolsField = typeof(ManagementFactory).GetField("_createdTools",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if(createdToolsField == null)
            {
                continue;
            }
            var createdTools = createdToolsField.GetValue(factory) as Dictionary<string, Tool>;
            if (createdTools == null || !createdTools.TryGetValue(item.ToolId, out var tool))
            {
                continue;
            }
            // 更新项目的可见状态
            bool actualVisibility = IsToolVisible(tool);
            if (item.IsVisible != actualVisibility)
            {
                item.IsVisible = actualVisibility;
            }
        }
    }
}