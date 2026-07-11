using BiliDownloader.Messages;
using MyAvaloniaManagementCommon.Message;

namespace BiliDownloader.Services;

/// <summary>
/// B站登录全局状态管理服务（单例）。
/// 持有当前登录态、Cookie、用户信息，并通过消息总线广播状态变更。
/// </summary>
public class BiliLoginStateService
{
    private static readonly Lazy<BiliLoginStateService> _instance =
        new(() => new BiliLoginStateService());

    /// <summary>
    /// 全局单例
    /// </summary>
    public static BiliLoginStateService Instance => _instance.Value;

    private readonly BiliCookieStore _cookieStore;
    private readonly BiliLoginService _loginService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized = false;

    /// <summary>当前是否已登录</summary>
    public bool IsLoggedIn { get; private set; }

    /// <summary>当前用户名</summary>
    public string? UserName { get; private set; }

    /// <summary>当前用户头像 URL</summary>
    public string? UserAvatar { get; private set; }

    /// <summary>当前 Cookie 字符串（可直接用于 HTTP 请求 Header）</summary>
    public string CookieHeader { get; private set; } = string.Empty;

    private BiliLoginStateService()
    {
        _cookieStore = new BiliCookieStore();
        _loginService = new BiliLoginService();
    }

    /// <summary>
    /// 初始化：建表 + 加载历史 Cookie + 验证有效性。
    /// 若 Cookie 已过期/失效，自动清空数据库和内存状态。
    /// 支持重复调用（幂等）。
    /// </summary>
    public async Task InitAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;
            _initialized = true;

            await _cookieStore.InitAsync();
            var cookieStr = await _cookieStore.GetCookieStringAsync();
            if (string.IsNullOrWhiteSpace(cookieStr))
            {
                IsLoggedIn = false;
                return;
            }

            // 验证历史 Cookie 是否仍有效
            var (isLoggedIn, userName, userAvatar) =
                await _loginService.CheckLoginAsync(cookieStr);

            if (isLoggedIn)
            {
                IsLoggedIn = true;
                UserName = userName;
                UserAvatar = userAvatar;
                CookieHeader = cookieStr;
                // 初始化成功后广播，通知已创建的 ViewModel 更新状态
                BroadcastState();
            }
            else
            {
                // Cookie 失效，清理数据库和内存
                await ClearLoginStateAsync();
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// 登录成功后的处理：保存 Cookie、刷新状态、广播。
    /// </summary>
    public async Task LoginAsync(List<(string Name, string Value)> cookies)
    {
        await _cookieStore.SaveCookiesAsync(cookies);

        var cookieStr = await _cookieStore.GetCookieStringAsync();
        var (isLoggedIn, userName, userAvatar) =
            await _loginService.CheckLoginAsync(cookieStr);

        if (isLoggedIn)
        {
            IsLoggedIn = true;
            UserName = userName;
            UserAvatar = userAvatar;
            CookieHeader = cookieStr;
            BroadcastState();
        }
    }

    /// <summary>
    /// 主动退出登录：调用 B站 exit API -> 清空本地数据库和内存 -> 广播。
    /// </summary>
    public async Task LogoutAsync()
    {
        // 先调服务端退出（忽略失败）
        if (!string.IsNullOrWhiteSpace(CookieHeader))
        {
            await _loginService.ExitLoginAsync(CookieHeader);
        }

        // 清空本地状态和数据库
        await ClearLoginStateAsync();

        // 广播状态变更
        BroadcastState();
    }

    /// <summary>
    /// 验证当前 Cookie 是否仍然有效。
    /// 若失效则清空数据库和内存状态并广播。
    /// 返回当前是否有效。
    /// </summary>
    public async Task<bool> CheckLoginValidAsync()
    {
        if (!IsLoggedIn) return false;

        var (isLoggedIn, userName, userAvatar) =
            await _loginService.CheckLoginAsync(CookieHeader);

        if (isLoggedIn)
        {
            UserName = userName;
            UserAvatar = userAvatar;
            return true;
        }

        // Cookie 失效，清理
        await ClearLoginStateAsync();
        BroadcastState();
        return false;
    }

    /// <summary>
    /// 本地清理：清空 SQLite 表 + 重置内存状态。
    /// </summary>
    private async Task ClearLoginStateAsync()
    {
        await _cookieStore.DeleteAllCookiesAsync();
        IsLoggedIn = false;
        UserName = null;
        UserAvatar = null;
        CookieHeader = string.Empty;
    }

    /// <summary>
    /// 通过消息总线广播当前登录状态
    /// </summary>
    private void BroadcastState()
    {
        try
        {
            // MessengerService 底层使用 WeakReferenceMessenger.Default，全局共享同一实例
            var messenger = new MessengerService();
            messenger.Send(new LoginStateChangedMessage(IsLoggedIn, UserName, UserAvatar));
        }
        catch
        {
            // 忽略广播失败
        }
    }
}
