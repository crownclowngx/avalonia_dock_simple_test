namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 同一文档类型的一个创建入口。
/// 设计意图：把“用户从哪里开始”与“创建什么文档”解耦，避免为每个入口复制 Document。
/// </summary>
public sealed class DocumentCreationIntentMetadata
{
    public DocumentCreationIntentMetadata(string intentId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        IntentId = intentId.Trim();
        DisplayName = displayName.Trim();
    }

    public string IntentId { get; }
    public string DisplayName { get; }
    public string Description { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
}
