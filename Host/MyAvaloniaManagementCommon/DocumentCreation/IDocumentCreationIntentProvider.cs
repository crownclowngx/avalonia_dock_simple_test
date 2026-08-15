namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 文档策略可选实现的多入口契约。
/// 设计意图：保持 <see cref="IDocumentCreationStrategy"/> 二方法 ABI 不变，让旧插件自动退化为单入口。
/// </summary>
public interface IDocumentCreationIntentProvider
{
    /// <summary>取得该策略支持的全部显式创建入口。</summary>
    /// <returns>稳定顺序的只读元数据；没有多入口时返回空集合。</returns>
    /// <remarks>宿主读取后会执行身份去重，插件不得在后续调用中改变既有入口的语义。</remarks>
    IReadOnlyList<DocumentCreationIntentMetadata> GetCreationIntents();
}
