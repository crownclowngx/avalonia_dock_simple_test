using BiliDownloader.Messages;
using BiliDownloader.Services.Infrastructure;
using MyAvaloniaManagementCommon.Message;

namespace BiliDownloader.Services.Auth;

/// <summary>
/// Bilibili 登录全局状态。持久化失败时允许本次进程继续使用内存登录态。
/// </summary>
public sealed class BiliLoginStateService
{
    private static readonly IPluginLogger Log = PluginLog.For<BiliLoginStateService>();
    private readonly IBiliCredentialStore _credentialStore;
    private readonly BiliLoginService _loginService;
    private readonly IMessengerService _messengerService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private string _cookieHeader = string.Empty;

    public bool IsLoggedIn { get; private set; }
    public bool IsPersistentLogin { get; private set; }
    public string? UserName { get; private set; }
    public string? UserAvatar { get; private set; }
    public string? StatusMessage { get; private set; }

    internal string CookieHeader => _cookieHeader;

    public BiliLoginStateService(
        IBiliCredentialStore credentialStore,
        BiliLoginService loginService,
        IMessengerService messengerService)
    {
        _credentialStore = credentialStore;
        _loginService = loginService;
        _messengerService = messengerService;
    }

    /// <summary>
    /// 用户明确点击登录后加载并验证历史凭据。网络不可用时保留密文并允许重试。
    /// </summary>
    public async Task InitAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            IReadOnlyDictionary<string, string> cookies;
            try
            {
                await _credentialStore.InitAsync();
                cookies = await _credentialStore.LoadCookiesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = "无法读取已保存的登录信息，可重新登录并仅在本次会话使用。";
                Log.Error(StatusMessage, ex);
                BroadcastState();
                return;
            }

            var cookieHeader = BuildCookieHeader(cookies);
            if (string.IsNullOrWhiteSpace(cookieHeader))
            {
                _initialized = true;
                ResetMemoryState();
                return;
            }

            var validation = await _loginService.CheckLoginAsync(cookieHeader);
            switch (validation.Status)
            {
                case LoginValidationStatus.Valid:
                    ApplyLoggedInState(cookieHeader, validation, isPersistent: true);
                    _initialized = true;
                    BroadcastState();
                    break;

                case LoginValidationStatus.Invalid:
                    await _credentialStore.DeleteAllAsync();
                    ResetMemoryState();
                    _initialized = true;
                    BroadcastState();
                    break;

                case LoginValidationStatus.Unavailable:
                    StatusMessage = "当前无法验证历史登录状态，请检查网络后重试。";
                    BroadcastState();
                    break;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// 扫码成功后的处理。安全持久化失败不会丢弃本次会话 Cookie。
    /// </summary>
    public async Task<bool> LoginAsync(List<(string Name, string Value)> cookies)
    {
        var cookieHeader = BuildCookieHeader(cookies);
        var validation = await _loginService.CheckLoginAsync(cookieHeader);
        if (validation.Status != LoginValidationStatus.Valid)
        {
            StatusMessage = validation.Status == LoginValidationStatus.Unavailable
                ? "登录已确认，但当前无法验证账号信息，请稍后重试。"
                : "登录凭据无效，请重新扫码。";
            BroadcastState();
            return false;
        }

        var persisted = true;
        try
        {
            await _credentialStore.SaveCookiesAsync(cookies);
        }
        catch (Exception ex)
        {
            persisted = false;
            Log.Error("登录信息无法持久化，将仅在本次会话中使用。", ex);
        }

        ApplyLoggedInState(cookieHeader, validation, persisted);
        _initialized = true;
        BroadcastState();
        return true;
    }

    public async Task LogoutAsync()
    {
        if (!string.IsNullOrWhiteSpace(_cookieHeader))
        {
            await _loginService.ExitLoginAsync(_cookieHeader);
        }

        try
        {
            await _credentialStore.DeleteAllAsync();
        }
        catch (Exception ex)
        {
            Log.Error("清除本地登录信息失败。", ex);
        }

        ResetMemoryState();
        _initialized = true;
        BroadcastState();
    }

    public async Task<bool> CheckLoginValidAsync()
    {
        if (!IsLoggedIn)
        {
            return false;
        }

        var validation = await _loginService.CheckLoginAsync(_cookieHeader);
        if (validation.Status == LoginValidationStatus.Valid)
        {
            UserName = validation.UserName;
            UserAvatar = validation.UserAvatar;
            StatusMessage = IsPersistentLogin ? null : "登录信息仅在本次会话有效。";
            return true;
        }

        if (validation.Status == LoginValidationStatus.Unavailable)
        {
            StatusMessage = "当前无法验证登录状态，已保留现有凭据。";
            BroadcastState();
            return true;
        }

        try
        {
            await _credentialStore.DeleteAllAsync();
        }
        catch (Exception ex)
        {
            Log.Error("删除已失效登录信息失败。", ex);
        }

        ResetMemoryState();
        BroadcastState();
        return false;
    }

    private void ApplyLoggedInState(
        string cookieHeader,
        LoginValidationResult validation,
        bool isPersistent)
    {
        _cookieHeader = cookieHeader;
        IsLoggedIn = true;
        IsPersistentLogin = isPersistent;
        UserName = validation.UserName;
        UserAvatar = validation.UserAvatar;
        StatusMessage = isPersistent ? null : "登录信息仅在本次会话有效。";
    }

    private void ResetMemoryState()
    {
        _cookieHeader = string.Empty;
        IsLoggedIn = false;
        IsPersistentLogin = false;
        UserName = null;
        UserAvatar = null;
        StatusMessage = null;
    }

    private static string BuildCookieHeader(IEnumerable<KeyValuePair<string, string>> cookies)
        => string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}"));

    private static string BuildCookieHeader(IEnumerable<(string Name, string Value)> cookies)
        => string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));

    private void BroadcastState()
    {
        try
        {
            _messengerService.Send(new LoginStateChangedMessage(
                IsLoggedIn,
                UserName,
                UserAvatar,
                IsPersistentLogin,
                StatusMessage));
        }
        catch
        {
            // UI 消息失败不改变登录事实。
        }
    }
}
