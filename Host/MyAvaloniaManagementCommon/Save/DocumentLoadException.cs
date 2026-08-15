namespace MyAvaloniaManagementCommon.Save;

/// <summary>
/// 表示文档内容不完整、损坏或不满足安全读取约束。
/// </summary>
/// <remarks>
/// 设计意图：插件只抛出稳定、可直接展示的业务错误，宿主负责统一呈现；
/// 异常消息不得包含原始 JSON、凭据、签名地址或第三方异常正文。
/// </remarks>
public sealed class DocumentLoadException : Exception
{
    /// <summary>使用可安全展示的稳定消息创建文档读取异常。</summary>
    /// <param name="message">不包含文档正文或敏感技术信息的用户消息。</param>
    public DocumentLoadException(string message)
        : base(message)
    {
    }

    /// <summary>使用稳定消息和仅供诊断链使用的内部异常创建文档读取异常。</summary>
    /// <param name="message">不包含文档正文或敏感技术信息的用户消息。</param>
    /// <param name="innerException">原始异常；宿主持久化前必须执行脱敏。</param>
    public DocumentLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
