namespace MyPlugTest.Models;

/// <summary>
/// 表示欢迎 Document 向消息接收 Document 发布的一次 URL 请求结果。
/// </summary>
/// <remarks>
/// 事件是 MyPlugTest 自有的普通不可变 DTO，不继承任何消息框架类型。这样发送方和接收方只依赖
/// MyPlugTest 自有同步事件端口，CommunityToolkit 仍只承担 MVVM 通知和命令职责。
/// </remarks>
public sealed record RequestResponseMessage
{
    /// <summary>创建一条请求结果事件，并记录当前本地展示时间。</summary>
    public RequestResponseMessage(
        string responseContent,
        string requestUrl,
        bool isSuccess = true)
    {
        ResponseContent = responseContent ?? throw new ArgumentNullException(nameof(responseContent));
        RequestUrl = requestUrl ?? throw new ArgumentNullException(nameof(requestUrl));
        IsSuccess = isSuccess;
        Timestamp = DateTimeOffset.Now;
    }

    /// <summary>获取响应正文。</summary>
    public string ResponseContent { get; }

    /// <summary>获取请求地址。</summary>
    public string RequestUrl { get; }

    /// <summary>获取事件创建时间。</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>获取请求是否成功。</summary>
    public bool IsSuccess { get; }
}
