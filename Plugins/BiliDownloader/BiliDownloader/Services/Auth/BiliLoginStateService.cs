using BiliDownloader.Messages;
using BiliDownloader.Messaging;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Auth;

/// <summary>
/// Bilibili 登录全局状态。启动时先恢复本地密文，再非阻塞验证远端状态。
/// </summary>
public sealed class BiliLoginStateService
{
    private static readonly IPluginLogger Log = PluginLog.For<BiliLoginStateService>();
    private readonly IBiliCredentialStore _credentialStore;
    private readonly IBiliSessionApi _sessionApi;
    private readonly IBiliDownloaderEventBus _eventBus;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly object _backgroundGate = new();
    private CancellationTokenSource? _backgroundValidationCts;
    private Task? _backgroundValidationTask;
    private BiliCredentialSession? _currentSession;
    private bool _restoreCompleted;
    private long _sessionGeneration;
    private string _cookieHeader = string.Empty;

    public bool IsLoggedIn { get; private set; }
    public bool IsPersistentLogin { get; private set; }
    public string? UserName { get; private set; }
    public string? UserAvatar { get; private set; }
    public string? StatusMessage { get; private set; }

    internal string CookieHeader => _cookieHeader;

    public BiliLoginStateService(
        IBiliCredentialStore credentialStore,
        IBiliSessionApi sessionApi,
        IBiliDownloaderEventBus eventBus)
    {
        _credentialStore = credentialStore;
        _sessionApi = sessionApi;
        _eventBus = eventBus;
    }

    /// <summary>
    /// 应用启动时只从本地密文恢复会话，不发起网络请求。
    /// </summary>
    public async Task RestoreSavedSessionAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_restoreCompleted)
            {
                return;
            }

            BiliCredentialSession? session;
            try
            {
                await _credentialStore.InitAsync(cancellationToken);
                session = await _credentialStore.LoadSessionAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                StatusMessage = "无法读取已保存的登录信息，可重新登录并仅在本次会话使用。";
                Log.Error(StatusMessage, ex);
                BroadcastState();
                return;
            }

            var cookieHeader = BuildCookieHeader(session?.Cookies ?? []);
            if (session is null || string.IsNullOrWhiteSpace(cookieHeader))
            {
                ResetMemoryState();
                _restoreCompleted = true;
                return;
            }

            _currentSession = session;
            _cookieHeader = cookieHeader;
            IsLoggedIn = true;
            IsPersistentLogin = true;
            UserName = session.UserName;
            UserAvatar = session.UserAvatar;
            StatusMessage = "正在验证已保存的登录状态…";
            _sessionGeneration++;
            _restoreCompleted = true;
            BroadcastState();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// 幂等启动后台验证。该方法本身不等待网络结果。
    /// </summary>
    public void StartBackgroundValidation()
    {
        lock (_backgroundGate)
        {
            if (_backgroundValidationTask is not null)
            {
                return;
            }

            if (!IsLoggedIn || string.IsNullOrWhiteSpace(_cookieHeader))
            {
                return;
            }

            _backgroundValidationCts = new CancellationTokenSource();
            _backgroundValidationTask = ValidateCurrentSessionAsync(
                _backgroundValidationCts.Token);
        }
    }

    /// <summary>
    /// 兼容现有登录入口：确保本地状态已恢复，但不在 UI 命令中重复启动远端请求。
    /// </summary>
    public Task InitAsync() => RestoreSavedSessionAsync();

    /// <summary>
    /// 扫码成功后的处理。安全持久化失败不会丢弃本次会话 Cookie。
    /// </summary>
    public async Task<bool> LoginAsync(List<(string Name, string Value)> cookies)
    {
        var session = new BiliCredentialSession(
            cookies.Select(cookie => new BiliCredentialCookie(cookie.Name, cookie.Value)).ToList());
        var cookieHeader = BuildCookieHeader(session.Cookies);
        var validation = await _sessionApi.CheckLoginAsync(cookieHeader);
        if (validation.Status != LoginValidationStatus.Valid)
        {
            StatusMessage = validation.Status == LoginValidationStatus.Unavailable
                ? "登录已确认，但当前无法验证账号信息，请稍后重试。"
                : "登录凭据无效，请重新扫码。";
            BroadcastState();
            return false;
        }

        var verifiedSession = session with
        {
            UserName = validation.UserName,
            UserAvatar = validation.UserAvatar,
        };
        var persisted = true;

        await _stateLock.WaitAsync();
        try
        {
            _sessionGeneration++;
            try
            {
                await _credentialStore.SaveSessionAsync(verifiedSession);
            }
            catch (Exception ex)
            {
                persisted = false;
                Log.Error("登录信息无法持久化，将仅在本次会话中使用。", ex);
            }

            ApplyLoggedInState(verifiedSession, persisted);
            _restoreCompleted = true;
            BroadcastState();
            return true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task LogoutAsync()
    {
        CancelBackgroundValidation();

        await _stateLock.WaitAsync();
        try
        {
            _sessionGeneration++;
            if (!string.IsNullOrWhiteSpace(_cookieHeader))
            {
                await _sessionApi.ExitLoginAsync(_cookieHeader);
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
            _restoreCompleted = true;
            BroadcastState();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<bool> CheckLoginValidAsync(CancellationToken cancellationToken = default)
    {
        await ValidateCurrentSessionAsync(cancellationToken);
        return IsLoggedIn;
    }

    /// <summary>
    /// 关闭时取消并观察后台验证，避免遗留未观察任务。
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? task;
        lock (_backgroundGate)
        {
            _backgroundValidationCts?.Cancel();
            task = _backgroundValidationTask;
        }

        if (task is not null)
        {
            try
            {
                await task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 只吸收后台验证自身的取消。Host 的关闭令牌取消必须继续向 Lifecycle 传播，
                // 否则 Host 会把未完成的协作停止误判为成功。
            }
        }

        lock (_backgroundGate)
        {
            // 若 Host 等待被取消而后台请求尚未真正退出，保留任务与 CTS，下一次幂等 Stop
            // 仍可继续观察它；绝不把仍在运行的任务伪装成已经清理。
            if (task is { IsCompleted: false }) return;
            _backgroundValidationCts?.Dispose();
            _backgroundValidationCts = null;
            _backgroundValidationTask = null;
        }
    }

    private async Task ValidateCurrentSessionAsync(CancellationToken cancellationToken)
    {
        string cookieHeader;
        long generation;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsLoggedIn || _currentSession is null || string.IsNullOrWhiteSpace(_cookieHeader))
            {
                return;
            }

            cookieHeader = _cookieHeader;
            generation = _sessionGeneration;
        }
        finally
        {
            _stateLock.Release();
        }

        LoginValidationResult validation;
        try
        {
            validation = await _sessionApi.CheckLoginAsync(cookieHeader, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error("后台验证登录状态失败。", ex);
            validation = new LoginValidationResult(LoginValidationStatus.Unavailable);
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (generation != _sessionGeneration
                || !string.Equals(cookieHeader, _cookieHeader, StringComparison.Ordinal)
                || _currentSession is null)
            {
                return;
            }

            switch (validation.Status)
            {
                case LoginValidationStatus.Valid:
                    var verifiedSession = _currentSession with
                    {
                        UserName = validation.UserName,
                        UserAvatar = validation.UserAvatar,
                    };
                    try
                    {
                        await _credentialStore.SaveSessionAsync(verifiedSession, cancellationToken);
                        IsPersistentLogin = true;
                        StatusMessage = null;
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = "账号信息已验证，但更新本地缓存失败。";
                        Log.Error(StatusMessage, ex);
                    }

                    _currentSession = verifiedSession;
                    UserName = validation.UserName;
                    UserAvatar = validation.UserAvatar;
                    IsLoggedIn = true;
                    BroadcastState();
                    break;

                case LoginValidationStatus.Invalid:
                    try
                    {
                        await _credentialStore.DeleteAllAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("删除已失效登录信息失败。", ex);
                    }

                    _sessionGeneration++;
                    ResetMemoryState("登录已过期，请重新登录。");
                    BroadcastState();
                    break;

                case LoginValidationStatus.Unavailable:
                    StatusMessage = "暂时无法验证登录状态，已保留本地登录信息。";
                    BroadcastState();
                    break;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void ApplyLoggedInState(BiliCredentialSession session, bool isPersistent)
    {
        _currentSession = session;
        _cookieHeader = BuildCookieHeader(session.Cookies);
        IsLoggedIn = true;
        IsPersistentLogin = isPersistent;
        UserName = session.UserName;
        UserAvatar = session.UserAvatar;
        StatusMessage = isPersistent ? null : "登录信息仅在本次会话有效。";
    }

    private void ResetMemoryState(string? statusMessage = null)
    {
        _currentSession = null;
        _cookieHeader = string.Empty;
        IsLoggedIn = false;
        IsPersistentLogin = false;
        UserName = null;
        UserAvatar = null;
        StatusMessage = statusMessage;
    }

    private void CancelBackgroundValidation()
    {
        lock (_backgroundGate)
        {
            _backgroundValidationCts?.Cancel();
        }
    }

    private static string BuildCookieHeader(IEnumerable<BiliCredentialCookie> cookies)
        => string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));

    private void BroadcastState()
    {
        try
        {
            _eventBus.Publish(new LoginStateChangedMessage(
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
