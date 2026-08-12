using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagementCommon.Save;

/// <summary>
/// 支持由宿主保存和恢复的 Document。
/// </summary>
public interface ISavableDocument
{
    string FilePath { get; set; }

    /// <summary>
    /// 必须与创建该 Document 的策略元数据一致的类型身份。
    /// </summary>
    DocumentTypeId SaveDocumentTypeId { get; }

    DocumentSaveData CreateSaveDocumentMetaData(string filePath);

    void LoadDocumentByMetaData(DocumentSaveData saveData);
}
