using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using BiliDownloader.ViewModels.Login;
using BiliDownloader.Views.Login;

namespace BiliDownloader.Services.Auth;

/// <summary>
/// 登录交互边界。业务 ViewModel 只请求“确保已登录”，不负责创建 Avalonia Window，
/// 因而下载工作台和任务错误行动能够复用同一登录流程并在测试中替换 UI。
/// </summary>
public interface ILoginDialogService
{
    /// <summary>若已有登录态立即成功，否则展示统一登录窗口并返回用户最终是否完成登录。</summary>
    Task<bool> EnsureLoggedInAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Avalonia 登录窗口适配器。窗口创建被限制在 UI 基础设施层；没有主窗口时安全返回 false，
/// 绝不让后台错误行动创建无所有者窗口。
/// </summary>
public sealed class AvaloniaLoginDialogService : ILoginDialogService
{
    private readonly BiliLoginStateService _stateService;
    private readonly BiliLoginService _loginService;

    public AvaloniaLoginDialogService(BiliLoginStateService stateService, BiliLoginService loginService)
    {
        _stateService = stateService;
        _loginService = loginService;
    }

    public async Task<bool> EnsureLoggedInAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _stateService.InitAsync();
        if (_stateService.IsLoggedIn) return true;

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return false;
        var viewModel = new LoginWindowViewModel(_loginService, _stateService);
        var window = new LoginWindow { DataContext = viewModel };
        await window.ShowDialog(owner);
        return viewModel.LoginSuccess || _stateService.IsLoggedIn;
    }
}
