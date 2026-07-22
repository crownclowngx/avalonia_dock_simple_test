using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BiliDownloader.Services.Auth;
using BiliDownloader.Views.Login;
using BiliDownloader.ViewModels.Login;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 登录状态栏子 ViewModel：负责登录状态显示、登录/登出操作
/// </summary>
public partial class LoginBarViewModel : ObservableObject
{
    private readonly BiliLoginStateService _loginStateService;
    private readonly BiliLoginService _loginService;

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
    {
        _loginStateService = loginStateService;
        _loginService = loginService;

        LoginCommand = new AsyncRelayCommand(EnsureLoggedInAsync);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync);

        // 构造阶段只读取内存快照，不访问网络；远端校验只能由用户点击登录后触发。
        IsLoggedIn = _loginStateService.IsLoggedIn;
        UserName = _loginStateService.UserName;
        StatusMessage = _loginStateService.StatusMessage;
    }

    /// <summary>
    /// 用户明确点击登录后，先尝试加载并验证历史登录态；验证失败时再显示二维码窗口。
    /// </summary>
    public async Task EnsureLoggedInAsync()
    {
        await _loginStateService.InitAsync();
        IsLoggedIn = _loginStateService.IsLoggedIn;
        UserName = _loginStateService.UserName;
        StatusMessage = _loginStateService.StatusMessage;

        if (IsLoggedIn) return;
        await ShowLoginWindowAsync();
    }

    private async Task ShowLoginWindowAsync()
    {
        var vm = new LoginWindowViewModel(_loginService, _loginStateService);
        var window = new LoginWindow { DataContext = vm };
        var parentWindow = GetParentWindow();
        if (parentWindow != null)
            await window.ShowDialog(parentWindow);
        else
            window.Show();
    }

    private async Task LogoutAsync()
    {
        await _loginStateService.LogoutAsync();
    }

    private Avalonia.Controls.Window? GetParentWindow()
    {
        try
        {
            var app = Avalonia.Application.Current;
            return app?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        }
        catch
        {
            return null;
        }
    }
}
