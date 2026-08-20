namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 创建 Document 时由宿主传给策略的参数。
/// </summary>
public sealed class DocumentCreationParams
{
    /// <summary>为指定 Document 类型创建参数对象。</summary>
    /// <param name="documentTypeId">已在宿主注册的稳定类型身份。</param>
    public DocumentCreationParams(DocumentTypeId documentTypeId) =>
        DocumentTypeId = documentTypeId ??
                         throw new ArgumentNullException(nameof(documentTypeId));

    /// <summary>获取本次创建请求的 Document 类型身份。</summary>
    public DocumentTypeId DocumentTypeId { get; }

    /// <summary>获取可选初始标题；空字符串表示由策略决定。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// 首次展示的工作流入口；null 表示该类型的默认入口。
    /// </summary>
    public CreationIntentId? CreationIntentId { get; init; }
}
