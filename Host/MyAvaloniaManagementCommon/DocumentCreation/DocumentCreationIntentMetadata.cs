namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 同一 Document 类型的一个不可变创建入口。
/// </summary>
/// <remarks>
/// 设计意图：把“用户从哪里开始”与“创建什么 Document”解耦，避免为每个菜单入口
/// 复制策略或 Document 类型。IntentId 只在所属 DocumentTypeId 内要求唯一。
/// </remarks>
public sealed class DocumentCreationIntentMetadata
{
    public DocumentCreationIntentMetadata(
        CreationIntentId intentId,
        string displayName)
    {
        IntentId = intentId ?? throw new ArgumentNullException(nameof(intentId));
        DisplayName = displayName ?? string.Empty;
    }

    public CreationIntentId IntentId { get; }
    public string DisplayName { get; }
    public string Description { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
}
