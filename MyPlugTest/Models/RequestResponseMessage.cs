using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MyPlugTest.Models;

/// <summary>
/// 请求响应消息类，用于在ViewModel之间传递HTTP请求的响应内容
/// </summary>
public class RequestResponseMessage : ValueChangedMessage<string>
{
    public string RequestUrl { get; }
    public DateTime Timestamp { get; }
    public bool IsSuccess { get; }

    public RequestResponseMessage(string responseContent, string requestUrl, bool isSuccess = true)
        : base(responseContent)
    {
        RequestUrl = requestUrl;
        Timestamp = DateTime.Now;
        IsSuccess = isSuccess;
    }
}