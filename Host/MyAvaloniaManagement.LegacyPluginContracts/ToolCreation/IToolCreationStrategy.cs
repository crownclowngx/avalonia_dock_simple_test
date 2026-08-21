using Dock.Model.Mvvm.Controls;

namespace MyAvaloniaManagementCommon.ToolCreation;

/// <summary>
/// 创建Tool的策略接口
/// </summary>
public interface IToolCreationStrategy
{
    /// <summary>
    /// 创建Tool实例
    /// </summary>
    /// <returns>创建的Tool实例</returns>
    Tool CreateTool();

    /// <summary>
    /// 获取Tool的元数据
    /// </summary>
    ToolMetadata GetMetadata();
}