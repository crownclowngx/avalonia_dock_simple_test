using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.ToolCreation;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace MyAvaloniaManagement.Models.ToolCreation;

/// <summary>
/// 创建并描述文件系统浏览工具。
/// </summary>
/// <param name="serviceProvider">用于解析带有可测试依赖的工具 ViewModel。</param>
/// <remarks>
/// 由容器创建 ViewModel，而不是直接调用无参构造，确保生产和测试都使用同一依赖图。
/// </remarks>
public class FileSystemTreeStrategy(IServiceProvider serviceProvider)
    : IToolCreationStrategy
{
    /// <summary>
    /// 从依赖注入容器创建并配置文件系统工具实例。
    /// </summary>
    /// <returns>创建的Tool实例</returns>
    public Tool CreateTool()
    {
        var tool = serviceProvider.GetRequiredService<FileSystemTreeViewModel>();
        tool.Id = "fileSystemTree";
        tool.Title = "文件";
        tool.CanClose = false;
        return tool;
    }

    /// <summary>
    /// 获取文件系统工具的稳定类型、显示信息和默认停靠位置。
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
