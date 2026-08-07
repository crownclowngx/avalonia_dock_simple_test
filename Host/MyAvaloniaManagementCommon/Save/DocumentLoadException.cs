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
    public DocumentLoadException(string message)
        : base(message)
    {
    }

    public DocumentLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
