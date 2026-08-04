using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BiliDownloader.Services.Auth;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 登录状态栏子 ViewModel：负责登录状态显示、登录/登出操作
/// </summary>
public partial class LoginBarViewModel : ObservableObject
{
    private readonly BiliLoginStateService _loginStateService;
    private readonly ILoginDialogService _loginDialogService;

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
        BiliLoginService loginService)
        : this(loginStateService, new AvaloniaLoginDialogService(loginStateService, loginService))
    {
    }

    public LoginBarViewModel(
        BiliLoginStateService loginStateService,
        ILoginDialogService loginDialogService)
    {
        _loginStateService = loginStateService;
        _loginDialogService = loginDialogService;

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
    public async Task EnsureLoggedInAsync()
    {
        await _loginStateService.InitAsync();
        IsLoggedIn = _loginStateService.IsLoggedIn;
        UserName = GetDisplayName(_loginStateService.IsLoggedIn, _loginStateService.UserName);
        StatusMessage = _loginStateService.StatusMessage;

        if (IsLoggedIn) return;
        await _loginDialogService.EnsureLoggedInAsync();
        IsLoggedIn = _loginStateService.IsLoggedIn;
        UserName = GetDisplayName(IsLoggedIn, _loginStateService.UserName);
        StatusMessage = _loginStateService.StatusMessage;
    }

    private async Task LogoutAsync()
    {
        await _loginStateService.LogoutAsync();
    }

    internal static string? GetDisplayName(bool isLoggedIn, string? userName)
        => isLoggedIn && string.IsNullOrWhiteSpace(userName) ? "已保存账号" : userName;
}
