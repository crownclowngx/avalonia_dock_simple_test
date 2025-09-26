namespace MyAvaloniaManagementCommon.Save;

/// <summary>
/// 文档保存数据类，用于统一的保存格式
/// </summary>
public class DocumentSaveData
{
    /// <summary>
    /// 文档类型ID
    /// </summary>
    public required string DocumentTypeId { get; set; }
    
    /// <summary>
    /// 文档标题
    /// </summary>
    public required string Title { get; set; }
    
    /// <summary>
    /// 文档保存时间
    /// </summary>
    public DateTime SaveTime { get; set; }
    
    /// <summary>
    /// 文档内容（以JSON格式存储）
    /// </summary>
    public required string Content { get; set; }
    
    /// <summary>
    /// 插件特定的元数据（以JSON格式存储）
    /// </summary>
    public required string PluginMetadata { get; set; }
}