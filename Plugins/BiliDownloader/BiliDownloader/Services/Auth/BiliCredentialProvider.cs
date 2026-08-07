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

/// <summary>把任意凭据实现投影为账号上下文，便于测试或宿主覆盖凭据实现而不产生具体类型耦合。</summary>
public sealed class BiliAccountContext : IBiliAccountContext
{
    private readonly IBiliCredentialProvider _credentials;
    public BiliAccountContext(IBiliCredentialProvider credentials) => _credentials = credentials;
    public bool IsLoggedIn => _credentials.IsLoggedIn;
    public string GetCookieHeader() => _credentials.GetCookieHeader();

    public long? UserId
    {
        get
        {
            foreach (var segment in GetCookieHeader().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = segment.IndexOf('=');
                if (separator > 0 && segment[..separator].Equals("DedeUserID", StringComparison.Ordinal)
                    && long.TryParse(segment[(separator + 1)..], out var id) && id > 0)
                    return id;
            }
            return null;
        }
    }
}
