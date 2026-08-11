using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BiliDownloader.Services.Auth;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 登录状态栏子 ViewModel：负责登录状态显示、登录/登出操作
/// </summary>
public partial class LoginBarViewModel : ObservableObject, IDisposable
{
    private readonly BiliLoginStateService _loginStateService;
    private readonly ILoginDialogService _loginDialogService;
    // 登录窗口和登录状态初始化都属于当前 Document 的临时 UI 流程，因此使用父级关闭令牌。
    // 本地 CTS 让子对象被单独释放时也能终止命令；登录凭据服务本身仍是插件级状态，不因关闭
    // 某一个标签而清空，关闭仅阻止该标签继续展示或消费登录结果。
    private readonly CancellationToken _documentToken;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private string? _userName;

    [ObservableProperty]
    private string? _statusMessage;

    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand LogoutCommand { get; }

    public LoginBarViewModel(
        BiliLoginStateService loginStateService,
        BiliLoginService loginService,
        CancellationToken documentToken = default)
        : this(loginStateService, new AvaloniaLoginDialogService(loginStateService, loginService), documentToken)
    {
    }

    public LoginBarViewModel(
        BiliLoginStateService loginStateService,
        ILoginDialogService loginDialogService,
        CancellationToken documentToken = default)
    {
        _loginStateService = loginStateService;
        _loginDialogService = loginDialogService;
        _documentToken = documentToken;

        LoginCommand = new AsyncRelayCommand(EnsureLoggedInAsync);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync);

        // 构造阶段只读取内存快照，不访问网络；远端校验只能由用户点击登录后触发。
        IsLoggedIn = _loginStateService.IsLoggedIn;
        UserName = GetDisplayName(_loginStateService.IsLoggedIn, _loginStateService.UserName);
        StatusMessage = _loginStateService.StatusMessage;
    }

    /// <summary>
    /// 用户明确点击登录后，先尝试加载并验证历史登录态；验证失败时再显示二维码窗口。
    /// </summary>
    public async Task EnsureLoggedInAsync(CancellationToken commandToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            _documentToken,
            _disposeCts.Token);
        var cancellationToken = linked.Token;
        await _loginStateService.InitAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (IsDisposed) return;
        IsLoggedIn = _loginStateService.IsLoggedIn;
        UserName = GetDisplayName(_loginStateService.IsLoggedIn, _loginStateService.UserName);
        StatusMessage = _loginStateService.StatusMessage;

        if (IsLoggedIn) return;
        await _loginDialogService.EnsureLoggedInAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsDisposed) return;
        IsLoggedIn = _loginStateService.IsLoggedIn;
        UserName = GetDisplayName(IsLoggedIn, _loginStateService.UserName);
        StatusMessage = _loginStateService.StatusMessage;
    }

    private async Task LogoutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _loginStateService.LogoutAsync();
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0 || _documentToken.IsCancellationRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        LoginCommand.Cancel();
        LogoutCommand.Cancel();
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    internal static string? GetDisplayName(bool isLoggedIn, string? userName)
        => isLoggedIn && string.IsNullOrWhiteSpace(userName) ? "已保存账号" : userName;
}
