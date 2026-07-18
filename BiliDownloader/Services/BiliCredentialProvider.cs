namespace BiliDownloader.Services;

/// <summary>
/// Bilibili 凭据提供者实现：委托给 BiliLoginStateService 获取当前登录态
/// </summary>
public class BiliCredentialProvider : IBiliCredentialProvider
{
    /// <inheritdoc />
    public string GetCookieHeader()
    {
        return BiliLoginStateService.Instance.CookieHeader;
    }

    /// <inheritdoc />
    public bool IsLoggedIn => BiliLoginStateService.Instance.IsLoggedIn;
}
