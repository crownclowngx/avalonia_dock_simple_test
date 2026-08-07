namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 文档策略可选实现的多入口契约。
/// 设计意图：保持 <see cref="IDocumentCreationStrategy"/> 二方法 ABI 不变，让旧插件自动退化为单入口。
/// </summary>
public interface IDocumentCreationIntentProvider
{
    IReadOnlyList<DocumentCreationIntentMetadata> GetCreationIntents();
}
