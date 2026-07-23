using System.Collections.Generic;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Models.Tools;

/// <summary>
/// 工具管理数据结构，包含工具管理所需的所有信息
/// </summary>
public class ToolManagementData
{
    /// <summary>
    /// 工具元数据字典（只读）
    /// </summary>
    public required IReadOnlyDictionary<string, ToolMetadata> ToolMetadata { get; init; }

    /// <summary>
    /// 已创建的工具字典（只读）
    /// </summary>
    public required IReadOnlyDictionary<string, Tool> CreatedTools { get; init; }

    /// <summary>
    /// 根停靠点
    /// </summary>
    public required IRootDock RootDock { get; init; }
}