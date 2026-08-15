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
    /// <summary>创建一个不可变的 Document 工作流入口描述。</summary>
    /// <param name="intentId">所属 Document 类型内唯一的入口身份。</param>
    /// <param name="displayName">展示给用户的本地化名称。</param>
    public DocumentCreationIntentMetadata(
        CreationIntentId intentId,
        string displayName)
    {
        IntentId = intentId ?? throw new ArgumentNullException(nameof(intentId));
        DisplayName = displayName ?? string.Empty;
    }

    /// <summary>获取入口的稳定身份。</summary>
    public CreationIntentId IntentId { get; }
    /// <summary>获取展示名称。</summary>
    public string DisplayName { get; }
    /// <summary>获取入口用途的简短说明。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>获取可选图标资源路径；空字符串表示使用宿主默认图标。</summary>
    public string IconPath { get; init; } = string.Empty;
}
