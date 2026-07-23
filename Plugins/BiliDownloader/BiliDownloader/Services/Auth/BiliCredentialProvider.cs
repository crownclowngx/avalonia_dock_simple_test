namespace BiliDownloader.Services.Auth;

/// <summary>
/// Bilibili 凭据提供者实现：委托给 BiliLoginStateService 获取当前登录态
/// </summary>
public class BiliCredentialProvider : IBiliCredentialProvider
{
    private readonly BiliLoginStateService _loginStateService;

    public BiliCredentialProvider(BiliLoginStateService loginStateService)
    {
        _loginStateService = loginStateService;
    }

    /// <inheritdoc />
    public string GetCookieHeader()
    {
        return _loginStateService.CookieHeader;
    }

    /// <inheritdoc />
    public bool IsLoggedIn => _loginStateService.IsLoggedIn;
}
