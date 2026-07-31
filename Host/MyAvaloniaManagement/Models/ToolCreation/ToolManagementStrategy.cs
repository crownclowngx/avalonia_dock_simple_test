using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.ToolCreation;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace MyAvaloniaManagement.Models.ToolCreation;

/// <summary>
/// 创建并描述工具管理面板。
/// </summary>
/// <param name="serviceProvider">用于解析带有工厂和消息服务依赖的工具 ViewModel。</param>
/// <remarks>
/// 工具管理器需要读取已经创建的其他工具，因此通过容器解析并由工厂最后创建。
/// </remarks>
public class ToolManagementStrategy(IServiceProvider serviceProvider)
    : IToolCreationStrategy
{
    /// <summary>
    /// 从依赖注入容器创建工具管理实例。
    /// </summary>
    /// <returns>工具管理工具实例</returns>
    public Tool CreateTool()
    {
        return serviceProvider.GetRequiredService<ToolManagementViewModel>();
    }

    /// <summary>
    /// 获取工具管理工具的元数据
    /// </summary>
    /// <returns>工具管理工具的元数据</returns>
    public ToolMetadata GetMetadata()
    {
        return new ToolMetadata
        {
            ToolTypeId = DockNameConstant.ToolManagement,
            DisplayName = "工具管理",
            Description = "管理所有工具的显示和隐藏",
            IconPath = "",
            Alignment = "Right"
        };
    }
}
