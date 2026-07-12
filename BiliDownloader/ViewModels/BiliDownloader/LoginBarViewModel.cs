using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BiliDownloader.Services;
using BiliDownloader.Views.Login;
using BiliDownloader.ViewModels.Login;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 登录状态栏子 ViewModel：负责登录状态显示、登录/登出操作
/// </summary>
public partial class LoginBarViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private string? _userName;

    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand LogoutCommand { get; }

    public LoginBarViewModel()
    {
        LoginCommand = new AsyncRelayCommand(ShowLoginWindowAsync);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync);

        // 拉取当前登录状态
        var stateService = BiliLoginStateService.Instance;
        IsLoggedIn = stateService.IsLoggedIn;
        UserName = stateService.UserName;
    }

    /// <summary>
    /// 确保登录状态已初始化（由 View 的 OnAttachedToVisualTree 调用）
    /// </summary>
    public async Task EnsureLoggedInAsync()
    {
        await BiliLoginStateService.Instance.InitAsync();
        var state = BiliLoginStateService.Instance;
        IsLoggedIn = state.IsLoggedIn;
        UserName = state.UserName;

        if (IsLoggedIn) return;
        await ShowLoginWindowAsync();
    }

    private async Task ShowLoginWindowAsync()
    {
        var vm = new LoginWindowViewModel();
        var window = new LoginWindow { DataContext = vm };
        var parentWindow = GetParentWindow();
        if (parentWindow != null)
            await window.ShowDialog(parentWindow);
        else
            window.Show();
    }

    private async Task LogoutAsync()
    {
        await BiliLoginStateService.Instance.LogoutAsync();
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
