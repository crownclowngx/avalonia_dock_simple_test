namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 创建 Document 时由宿主传给策略的参数。
/// </summary>
public sealed class DocumentCreationParams
{
    public DocumentCreationParams(DocumentTypeId documentTypeId) =>
        DocumentTypeId = documentTypeId ??
                         throw new ArgumentNullException(nameof(documentTypeId));

    public DocumentTypeId DocumentTypeId { get; }
    public string InitializationData { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public object AdditionalData { get; init; } = new();

    /// <summary>
    /// 首次展示的工作流入口；null 表示该类型的默认入口。
    /// </summary>
    public CreationIntentId? CreationIntentId { get; init; }
}
