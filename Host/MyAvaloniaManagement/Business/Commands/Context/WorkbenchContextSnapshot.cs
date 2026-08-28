using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Commands.Context;

/// <summary>描述 Host 当前能够确认的最小工作台上下文事实。</summary>
/// <remarks>
/// v1 只表达活动 Document 的稳定身份、所有权和持久化能力。快照不保存模型、Control、Dock、
/// Provider、Scope 或任意对象字典；<see cref="Revision"/> 也只用于识别上下文代次，不是文件修订号。
/// </remarks>
internal sealed record WorkbenchContextSnapshot
{
    private WorkbenchContextSnapshot(
        bool hasActiveDocument,
        DocumentTypeId? activeDocumentTypeId,
        PluginId? activeDocumentOwnerId,
        bool isActiveDocumentPersistable,
        long revision)
    {
        HasActiveDocument = hasActiveDocument;
        ActiveDocumentTypeId = activeDocumentTypeId;
        ActiveDocumentOwnerId = activeDocumentOwnerId;
        IsActiveDocumentPersistable = isActiveDocumentPersistable;
        Revision = revision;
    }

    /// <summary>获取当前是否存在活动 Document。</summary>
    internal bool HasActiveDocument { get; }

    /// <summary>获取活动 Document 的稳定类型；没有活动 Document 时为 null。</summary>
    internal DocumentTypeId? ActiveDocumentTypeId { get; }

    /// <summary>获取活动插件 Document 的所有者；Host Document 或空上下文时为 null。</summary>
    internal PluginId? ActiveDocumentOwnerId { get; }

    /// <summary>获取活动 Document 是否声明了 Host 持久化能力。</summary>
    internal bool IsActiveDocumentPersistable { get; }

    /// <summary>获取 Context 快照的单调递增代次。</summary>
    internal long Revision { get; }

    /// <summary>创建没有活动 Document 的快照。</summary>
    internal static WorkbenchContextSnapshot Empty(long revision = 0) =>
        new(false, null, null, false, revision);

    /// <summary>从 Workspace 已确认的稳定值创建活动 Document 快照。</summary>
    /// <remarks>
    /// 工厂刻意不接收 Adapter、模型或服务对象，使该值类型在构造入口和属性两侧都保持纯净。
    /// </remarks>
    internal static WorkbenchContextSnapshot ActiveDocument(
        DocumentTypeId documentTypeId,
        PluginId? ownerId,
        bool isPersistable,
        long revision)
        => new(
            true,
            documentTypeId,
            ownerId,
            isPersistable,
            revision);
}
