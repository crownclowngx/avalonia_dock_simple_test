using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.ViewModels.Tools;
using System;
using MyAvaloniaManagementCommon.ToolCreation;
using MyAvaloniaManagement.Business.Constants;

namespace MyAvaloniaManagement.Models.ToolCreation;

/// <summary>
/// 创建并描述文件系统浏览工具。
/// </summary>
/// <param name="viewModelFactory">创建带有完整依赖的文件树 ViewModel。</param>
/// <remarks>
/// 策略只依赖窄工厂，不获得整个容器；实际工厂由 Host 组合根注册。
/// </remarks>
internal sealed class FileSystemTreeStrategy(
    Func<FileSystemTreeViewModel> viewModelFactory)
    : IToolCreationStrategy
{
    /// <summary>
    /// 通过组合根提供的窄工厂创建并配置文件系统工具实例。
    /// </summary>
    /// <returns>创建的Tool实例</returns>
    public Tool CreateTool()
    {
        var tool = viewModelFactory();
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
        return new ToolMetadata(
            HostExtensionIds.FileSystemTree,
            "文件系统浏览器",
            ToolDockSide.Left,
            [new ToolTypeId("fileSystemTree")])
        {
            Description = "浏览和管理文件系统",
            IconPath = ""
        };
    }
}
