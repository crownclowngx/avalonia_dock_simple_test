namespace MyPlugTest.Services;

/// <summary>
/// 获取 URL 文本内容的最小业务边界。
/// ViewModel 只描述“读取文本”的意图，不直接依赖 Flurl 的静态扩展方法，
/// 从而让网络实现、资源生命周期和界面状态各自保持单一职责。
/// </summary>
public interface IUrlContentService
{
    /// <summary>
    /// 请求指定 URL 并返回响应正文；调用失败时抛出带状态码和响应正文的
    /// <see cref="UrlContentRequestException"/>，供界面维持现有错误提示格式。
    /// </summary>
    Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// URL 文本请求失败的插件级异常，隔离 ViewModel 与具体 HTTP 客户端的异常类型。
/// </summary>
public sealed class UrlContentRequestException : Exception
{
    public UrlContentRequestException(
        int? statusCode,
        string? responseContent,
        Exception innerException)
        : base(innerException.Message, innerException)
    {
        StatusCode = statusCode;
        ResponseContent = responseContent;
    }

    /// <summary>HTTP 状态码；请求尚未获得响应时可能为空。</summary>
    public int? StatusCode { get; }

    /// <summary>服务端返回的错误正文；网络层未返回正文时可能为空。</summary>
    public string? ResponseContent { get; }
}
