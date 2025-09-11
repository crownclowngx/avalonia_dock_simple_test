namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 支持保存和打开功能的文档接口
/// </summary>
public interface ISavableDocument
{
    /// <summary>
    /// 文档文件路径
    /// </summary>
    string FilePath { get; set; }
    
    /// <summary>
    /// 文档类型ID，用于标识文档类型
    /// </summary>
    string SaveDocumentTypeId { get; }
    
    /// <summary>
    /// 保存文档内容到指定路径
    /// 注意title将默认使用保存时候的文件名
    /// </summary>
    /// <param name="filePath">保存路径</param>
    DocumentSaveData CreateSaveDocumentMetaData(string filePath);
    
    /// <summary>
    /// 从指定路径加载文档内容
    /// </summary>
    /// <param name="saveData">加载路径</param>
    void LoadDocumentByMetaData(DocumentSaveData  saveData);
}