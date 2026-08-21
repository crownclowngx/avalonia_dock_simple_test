namespace MyAvaloniaManagementCommon.Save;

/// <summary>
/// 定义 Document 创建和恢复插件业务内容快照的能力。
/// </summary>
/// <remarks>
/// <para>
/// 本接口只描述插件内容，不描述磁盘事务。文件路径、Document 类型归属、标题、保存时间、
/// 原子写入、恢复备份和关闭提交点全部由宿主管理；实现同时需要通过
/// <see cref="IDocumentSaveState"/> 报告脏状态，但两项职责保持为独立接口。
/// </para>
/// <para>
/// G8 在 Managed Plugin v1 封板前进行过一次有意的破坏式修改：删除旧候选接口中的
/// <c>FilePath</c>、<c>SaveDocumentTypeId</c>、<c>CreateSaveDocumentMetaData</c> 和
/// <c>LoadDocumentByMetaData</c>。项目不存在已发布旧插件或历史 Document，因此不提供
/// Obsolete 转发成员；保留它们反而会让插件与宿主同时拥有路径和身份事实。
/// </para>
/// </remarks>
public interface ISavableDocument
{
    /// <summary>
    /// 创建当前业务状态的不可变内容快照。
    /// </summary>
    /// <remarks>
    /// 此方法只能读取业务状态并进行序列化，不得修改标题、脏状态或另存保护。
    /// 它不接收目标路径，因为业务内容不能因用户选择的保存位置而改变。
    /// </remarks>
    DocumentContentSnapshot CreateContentSnapshot();

    /// <summary>
    /// 从已经通过宿主信封验证的内容快照恢复业务状态。
    /// </summary>
    /// <remarks>
    /// 参数只包含内容 schema 和 payload。标题、插件身份、Document 类型及保存时间均已由
    /// 宿主验证或应用，插件不得在 payload 中维护第二份宿主身份事实。
    /// </remarks>
    /// <exception cref="DocumentLoadException">
    /// 内容损坏、不完整或违反安全读取约束时抛出。异常消息必须稳定且不包含原始正文。
    /// </exception>
    void RestoreContent(DocumentContentSnapshot snapshot);
}
