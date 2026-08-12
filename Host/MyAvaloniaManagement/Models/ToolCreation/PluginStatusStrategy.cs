using System;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Models.ToolCreation;

/// <summary>
/// 创建宿主只读插件状态工具。
/// </summary>
public sealed class PluginStatusStrategy(IServiceProvider serviceProvider)
    : IToolCreationStrategy
{
    public Tool CreateTool() =>
        serviceProvider.GetRequiredService<PluginStatusViewModel>();

    public ToolMetadata GetMetadata() => new(
        HostExtensionIds.PluginStatus,
        "插件状态",
        ToolDockSide.Right,
        [new ToolTypeId("pluginStatus")])
    {
        Description = "查看插件加载、依赖和生命周期诊断",
        IconPath = string.Empty,
    };
}
