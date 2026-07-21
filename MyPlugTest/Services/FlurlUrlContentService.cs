using Flurl.Http;

namespace MyPlugTest.Services;

/// <summary>
/// 使用 Flurl 实现 URL 文本请求。该服务只封装网络副作用，不保存任何 Document 状态，
/// 因而可以安全地由插件模块注册为 Singleton 并在多个 Document 之间复用。
/// </summary>
public sealed class FlurlUrlContentService : IUrlContentService
{
    /// <inheritdoc />
    public async Task<string> GetStringAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await url.GetStringAsync(cancellationToken: cancellationToken);
        }
        catch (FlurlHttpException ex)
        {
            string? responseContent = null;
            try
            {
                responseContent = await ex.GetResponseStringAsync();
            }
            catch
            {
                // 读取错误正文失败时仍保留原始状态码和异常，不能用二次异常覆盖首要失败原因。
            }

            throw new UrlContentRequestException(ex.StatusCode, responseContent, ex);
        }
    }
}
