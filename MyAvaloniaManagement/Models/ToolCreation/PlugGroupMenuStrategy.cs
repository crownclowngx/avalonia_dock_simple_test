using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Models.ToolCreation;

/// <summary>
/// PlugGroupMenu工具创建策略
/// </summary>
public class PlugGroupMenuStrategy : IToolCreationStrategy
{
    /// <summary>
    /// 创建PlugGroupMenuViewModel实例
    /// </summary>
    /// <returns>创建的Tool实例</returns>
    public Tool CreateTool()
    {
        return new PlugGroupMenuViewModel
        {
            Id = "plugGroupMenu",
            Title = "插件",
            CanClose = true,
        };
    }

    /// <summary>
    /// 获取PlugGroupMenu的元数据
    /// </summary>
    /// <returns>Tool元数据</returns>
    public ToolMetadata GetMetadata()
    {
        return new ToolMetadata
        {
            ToolTypeId = "plugGroupMenu",
            DisplayName = "插件分组菜单",
            Description = "显示按分类组织的插件文档菜单",
            IconPath = "", // 可根据实际情况设置图标路径
            Alignment = "Right" // 该工具应该在右侧面板
        };
    }
}