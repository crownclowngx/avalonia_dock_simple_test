using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagementCommon.Save;

/// <summary>
/// 支持由宿主保存和恢复的 Document。
/// </summary>
public interface ISavableDocument
{
    /// <summary>
    /// 获取或设置主文件路径。实现不得在创建保存快照时修改该属性；路径只由宿主在
    /// 主文件成功提交后更新。
    /// </summary>
    string FilePath { get; set; }

    /// <summary>
    /// 必须与创建该 Document 的策略元数据一致的类型身份。
    /// </summary>
    DocumentTypeId SaveDocumentTypeId { get; }

    /// <summary>
    /// 创建待写入指定路径的不可变语义快照。
    /// </summary>
    /// <remarks>
    /// 此方法必须无保存状态副作用：不得修改 FilePath、标题、脏状态或另存保护。
    /// 文件系统事务失败时，宿主需要保证 Document 仍准确表示“尚未保存”。
    /// </remarks>
    DocumentSaveData CreateSaveDocumentMetaData(string filePath);

    /// <summary>
    /// 从已经通过宿主信封解析的保存数据恢复业务状态。
    /// </summary>
    /// <exception cref="DocumentLoadException">
    /// 内容损坏、不完整或违反安全读取约束时抛出。异常消息必须稳定且不包含原始正文。
    /// </exception>
    void LoadDocumentByMetaData(DocumentSaveData saveData);
}
