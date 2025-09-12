namespace MyAvaloniaManagementCommon.ToolCreation;

/// <summary>
/// Tool元数据类，用于存储Tool的说明信息
/// </summary>
public class ToolMetadata
{
    /// <summary>
    /// Tool类型ID
    /// </summary>
    public string ToolTypeId { get; set; }
    
    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName { get; set; }
    
    /// <summary>
    /// 描述信息
    /// </summary>
    public string Description { get; set; }
    
    /// <summary>
    /// 图标路径
    /// </summary>
    public string IconPath { get; set; }
    
    /// <summary>
    /// Tool的对齐方式（Left, Right, Top, Bottom）
    /// </summary>
    public string Alignment { get; set; } = "Left";
}