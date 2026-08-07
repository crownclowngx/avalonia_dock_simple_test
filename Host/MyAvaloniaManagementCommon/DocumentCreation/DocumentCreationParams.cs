namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 用于创建Document的参数类
/// </summary>
public class DocumentCreationParams
{
    /// <summary>
    /// Document的类别
    /// </summary>
    public string DocumentType { get; set; }
    
    /// <summary>
    /// 初始化字符串
    /// </summary>
    public string InitializationData { get; set; }
    
    /// <summary>
    /// 文档标题
    /// </summary>
    public string Title { get; set; }
    
    /// <summary>
    /// 其他可选参数
    /// </summary>
    public object AdditionalData { get; set; }

    /// <summary>
    /// 创建入口意图。它只决定文档首次展示的工作流，不改变文档类型和持久化契约。
    /// </summary>
    public string CreationIntentId { get; set; }
    
    public DocumentCreationParams(string documentType)
    {
        DocumentType = documentType;
        InitializationData = string.Empty;
        Title = string.Empty;
        AdditionalData = new object();
        CreationIntentId = string.Empty;
    }
}
