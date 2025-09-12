using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Models.ToolCreation;

/// <summary>
/// 工具管理策略类，用于创建工具管理工具
/// </summary>
public class ToolManagementStrategy : IToolCreationStrategy
{
    /// <summary>
    /// 创建工具管理工具实例
    /// </summary>
    /// <returns>工具管理工具实例</returns>
    public Tool CreateTool()
    {
        return new ToolManagementViewModel();
    }

    /// <summary>
    /// 获取工具管理工具的元数据
    /// </summary>
    /// <returns>工具管理工具的元数据</returns>
    public ToolMetadata GetMetadata()
    {
        return new ToolMetadata
        {
            ToolTypeId = "toolManagement",
            DisplayName = "工具管理",
            Description = "管理所有工具的显示和隐藏",
            IconPath = "",
            Alignment = "Right"
        };
    }
}