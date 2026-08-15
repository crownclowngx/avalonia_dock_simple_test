using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.ToolCreation;
using System;

namespace MyAvaloniaManagement.Models.ToolCreation;

/// <summary>
/// 创建并描述工具管理面板。
/// </summary>
/// <param name="viewModelFactory">创建带有工厂和消息依赖的工具 ViewModel。</param>
/// <remarks>
/// 工具管理器需要读取已经创建的其他工具，因此由 ManagementFactory 最后调用窄工厂创建。
/// </remarks>
internal sealed class ToolManagementStrategy(
    Func<ToolManagementViewModel> viewModelFactory)
    : IToolCreationStrategy
{
    /// <summary>
    /// 通过组合根提供的窄工厂创建工具管理实例。
    /// </summary>
    /// <returns>工具管理工具实例</returns>
    public Tool CreateTool()
    {
        return viewModelFactory();
    }

    /// <summary>
    /// 获取工具管理工具的元数据
    /// </summary>
    /// <returns>工具管理工具的元数据</returns>
    public ToolMetadata GetMetadata()
    {
        return new ToolMetadata(
            HostExtensionIds.ToolManagement,
            "工具管理",
            ToolDockSide.Right,
            [new ToolTypeId("toolManagement")])
        {
            Description = "管理所有工具的显示和隐藏",
            IconPath = ""
        };
    }
}
