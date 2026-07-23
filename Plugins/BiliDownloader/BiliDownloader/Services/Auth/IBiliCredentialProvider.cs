namespace BiliDownloader.Services.Auth;

/// <summary>
/// Bilibili 凭据提供者接口：在下载执行时获取当前登录态
/// </summary>
public interface IBiliCredentialProvider
{
    /// <summary>获取当前 Cookie Header 字符串（可直接用于 HTTP 请求）</summary>
    string GetCookieHeader();

    /// <summary>当前是否已登录</summary>
    bool IsLoggedIn { get; }
}
