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

/// <summary>
/// 个人来源所需的最小账号上下文。
/// 设计意图：Provider 不依赖登录 UI 或持久化实现，只读取当前请求所需的账号事实。
/// </summary>
public interface IBiliAccountContext
{
    bool IsLoggedIn { get; }
    long? UserId { get; }
    string GetCookieHeader();
}
