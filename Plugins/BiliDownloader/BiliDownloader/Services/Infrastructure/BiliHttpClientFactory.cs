namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// 创建 BiliDownloader 专用 HTTP 客户端。调用方拥有并负责释放返回的客户端。
/// </summary>
public interface IBiliHttpClientFactory
{
    HttpClient CreateMediaClient();
    HttpClient CreateCoverClient();
}

public sealed class BiliHttpClientFactory : IBiliHttpClientFactory
{
    public HttpClient CreateMediaClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(60),
        };
        client.DefaultRequestHeaders.Add("User-Agent", HttpConstants.UserAgent);
        client.DefaultRequestHeaders.Add("Referer", HttpConstants.Referer);
        client.DefaultRequestHeaders.Add("Origin", HttpConstants.Origin);
        return client;
    }

    public HttpClient CreateCoverClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
        client.DefaultRequestHeaders.Add("User-Agent", HttpConstants.UserAgent);
        client.DefaultRequestHeaders.Add("Referer", HttpConstants.Referer);
        return client;
    }
}
