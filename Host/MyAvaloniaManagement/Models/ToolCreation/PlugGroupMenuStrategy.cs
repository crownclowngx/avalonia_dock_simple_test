using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagementCommon.ToolCreation;
using System;

namespace MyAvaloniaManagement.Models.ToolCreation;

/// <summary>
/// 创建并描述插件分组菜单工具。
/// </summary>
/// <param name="viewModelFactory">创建带有工厂和菜单依赖的工具 ViewModel。</param>
/// <remarks>
/// 创建结果和元数据共同使用 <see cref="DockNameConstant.PlugGroupMenu"/>，
/// 保证 DockableLocator、布局恢复和工具字典引用同一个稳定 ID。
/// </remarks>
internal sealed class PlugGroupMenuStrategy(
    Func<PlugGroupMenuViewModel> viewModelFactory)
    : IToolCreationStrategy
{
    /// <summary>
    /// 通过组合根提供的窄工厂创建并配置插件菜单工具实例。
    /// </summary>
    /// <returns>创建的Tool实例</returns>
    public Tool CreateTool()
    {
        var tool = viewModelFactory();
        tool.Title = "插件";
        tool.CanClose = false;
        return tool;
    }

    /// <summary>
    /// 获取插件菜单工具的稳定类型、显示信息和默认停靠位置。
    /// </summary>
    /// <returns>Tool元数据</returns>
    public ToolMetadata GetMetadata()
    {
        return new ToolMetadata(
            HostExtensionIds.PluginMenu,
            "插件分组菜单",
            ToolDockSide.Right,
            [new ToolTypeId("plugGroupMenu")])
        {
            Description = "显示按分类组织的插件文档菜单",
            IconPath = ""
        };
    }
}
