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
    /// <summary>获取插件定义的初始化文本；宿主不解释其内容。</summary>
    public string InitializationData { get; init; } = string.Empty;
    /// <summary>获取可选初始标题；空字符串表示由策略决定。</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>
    /// 获取插件定义的进程内附加对象。该成员不进入持久化，也不得用作跨插件协议。
    /// </summary>
    /// <remarks>该候选契约将在 G11 根据真实调用方决定删除或收口。</remarks>
    public object AdditionalData { get; init; } = new();

    /// <summary>
    /// 首次展示的工作流入口；null 表示该类型的默认入口。
    /// </summary>
    public CreationIntentId? CreationIntentId { get; init; }
}
