namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 文档元数据类，用于存储文档类型的说明信息
/// </summary>
public class DocumentMetadata
{
    /// <summary>
    /// 文档类型ID
    /// </summary>
    public string DocumentTypeId { get; set; }
    
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
    /// 是否在菜单中显示
    /// </summary>
    public bool ShowInMenu { get; set; }
    
    /// <summary>
    /// 菜单分类
    /// </summary>
    public string MenuCategory { get; set; }
    
    
    
    public DocumentMetadata(string documentTypeId, string displayName)
    {
        DocumentTypeId = documentTypeId;
        DisplayName = displayName;
        Description = string.Empty;
        IconPath = string.Empty;
        ShowInMenu = true;
        MenuCategory = "未归类插件";
    }
}