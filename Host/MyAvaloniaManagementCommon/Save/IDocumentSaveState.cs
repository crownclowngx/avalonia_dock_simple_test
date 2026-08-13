namespace MyAvaloniaManagementCommon.Save;

/// <summary>
/// 描述可保存 Document 相对于最近一次成功磁盘提交的公共状态。
/// </summary>
/// <remarks>
/// <para>
/// 该接口刻意不包含序列化方法、路径选择或关闭交互。Document 只负责判断自己的
/// 可持久业务状态是否变化；宿主负责文件事务，并且只有主文件原子写入成功后才能
/// 调用 <see cref="AcceptChanges"/>。
/// </para>
/// <para>
/// 实现通常把 <see cref="IsDirty"/> 映射到 Dock Document 的 IsModified 属性，
/// 使标签视觉状态与关闭保护使用同一事实来源。生成保存快照不得提前清除此状态。
/// </para>
/// </remarks>
public interface IDocumentSaveState
{
    /// <summary>
    /// 获取当前 Document 是否包含尚未成功写入主文件的可持久变化。
    /// </summary>
    bool IsDirty { get; }

    /// <summary>
    /// 接受当前状态作为新的保存基线。
    /// </summary>
    /// <remarks>
    /// 仅可由宿主在主文件原子写入成功后调用。备份写入失败不改变主文件已经成功
    /// 提交的事实，因此仍会调用本方法，但宿主必须向用户报告备份警告。
    /// </remarks>
    void AcceptChanges();
}
