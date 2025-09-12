using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Models.ToolCreation;

/// <summary>
/// FileSystemTree工具创建策略
/// </summary>
public class FileSystemTreeStrategy : IToolCreationStrategy
{
    /// <summary>
    /// 创建FileSystemTreeViewModel实例
    /// </summary>
    /// <returns>创建的Tool实例</returns>
    public Tool CreateTool()
    {
        return new FileSystemTreeViewModel
        {
            Id = "fileSystemTree",
            Title = "文件",
            CanClose = false
        };
    }

    /// <summary>
    /// 获取FileSystemTree的元数据
    /// </summary>
    /// <returns>Tool元数据</returns>
    public ToolMetadata GetMetadata()
    {
        return new ToolMetadata
        {
            ToolTypeId = "fileSystemTree",
            DisplayName = "文件系统浏览器",
            Description = "浏览和管理文件系统",
            IconPath = "", // 可根据实际情况设置图标路径
            Alignment = "Left" // 该工具应该在左侧面板
        };
    }
}