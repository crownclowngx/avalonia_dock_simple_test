using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Save;
using BiliDownloader.Constants;
using BiliDownloader.Messages;
using BiliDownloader.Services;
using BiliDownloader.Views.Login;
using BiliDownloader.ViewModels.Login;
using Newtonsoft.Json;

namespace BiliDownloader.ViewModels;

public class BiliDownloaderViewModel : Document, ISavableDocument
{
    public string SaveDocumentTypeId => SaveDocumentTypeIdConstant.BiliDownloaderDocumentId;
    public string FilePath { get; set; } = string.Empty;

    private string _url = "";
    private string _downloadInfo = "";
    private bool _isLoading = false;
    private bool _isLoggedIn = false;
    private string? _userName;
    private readonly IMessengerService? _messengerService;

    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    public string DownloadInfo
    {
        get => _downloadInfo;
        set => SetProperty(ref _downloadInfo, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>
    /// 当前是否已登录（不序列化到文件）
    /// </summary>
    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        set => SetProperty(ref _isLoggedIn, value);
    }

    /// <summary>
    /// 当前登录用户名（不序列化到文件）
    /// </summary>
    public string? UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    public IRelayCommand DownloadCommand { get; }
    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand LogoutCommand { get; }

    public BiliDownloaderViewModel()
    {
        DownloadCommand = new AsyncRelayCommand(DownloadAsync);
        LoginCommand = new AsyncRelayCommand(ShowLoginWindowAsync);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync);

        // 拉取当前登录状态
        var stateService = BiliLoginStateService.Instance;
        IsLoggedIn = stateService.IsLoggedIn;
        UserName = stateService.UserName;

        // 注册消息总线接收器，响应登录状态变更广播
        try
        {
            _messengerService = new MessengerService();
            _messengerService?.Register<BiliDownloaderViewModel, LoginStateChangedMessage>(
                this,
                (vm, msg) =>
                {
                    vm.IsLoggedIn = msg.IsLoggedIn;
                    vm.UserName = msg.UserName;
                });
        }
        catch
        {
            // ServiceProvider 尚未初始化时忽略
        }
    }

    /// <summary>
    /// 弹出登录窗口（Document 被点击时由 View 调用，或手动触发）
    /// </summary>
    public async Task EnsureLoggedInAsync()
    {
        // 等待初始化完成（幂等：已初始化则立即返回），确保登录状态已从 SQLite 加载
        await BiliLoginStateService.Instance.InitAsync();

        // 初始化完成后重新同步状态（首次可能因 fire-and-forget 未广播而错过）
        var state = BiliLoginStateService.Instance;
        IsLoggedIn = state.IsLoggedIn;
        UserName = state.UserName;

        if (IsLoggedIn) return;
        await ShowLoginWindowAsync();
    }

    private async Task ShowLoginWindowAsync()
    {
        var vm = new LoginWindowViewModel();
        var window = new LoginWindow
        {
            DataContext = vm
        };

        // 获取父窗口
        var parentWindow = GetParentWindow();
        if (parentWindow != null)
        {
            await window.ShowDialog(parentWindow);
        }
        else
        {
            // 无父窗口时作为独立窗口显示
            window.Show();
        }
    }

    private async Task LogoutAsync()
    {
        await BiliLoginStateService.Instance.LogoutAsync();
    }

    private Window? GetParentWindow()
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

    private async Task DownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            DownloadInfo = "请输入有效的B站视频链接";
            return;
        }

        // 下载前检查登录状态
        if (!IsLoggedIn)
        {
            DownloadInfo = "请先登录后再下载";
            await ShowLoginWindowAsync();
            return;
        }

        try
        {
            IsLoading = true;
            DownloadInfo = "正在解析视频信息...";

            // TODO: 实现实际的B站视频解析逻辑
            await Task.Delay(500);

            DownloadInfo = $"已获取链接: {Url}\n解析功能开发中...";
            IsModified = true;
        }
        catch (Exception ex)
        {
            DownloadInfo = $"解析异常: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public DocumentSaveData CreateSaveDocumentMetaData(string filePath)
    {
        // 注意：登录状态（IsLoggedIn、UserName）不序列化到文件中
        var saveDataObject = new
        {
            Url = _url,
            DownloadInfo = _downloadInfo
        };

        var saveData = new DocumentSaveData
        {
            DocumentTypeId = SaveDocumentTypeId,
            Title = Title,
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(saveDataObject),
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "1.0" })
        };

        IsModified = false;
        return saveData;
    }

    public void LoadDocumentByMetaData(DocumentSaveData saveData)
    {
        try
        {
            if (saveData != null)
            {
                var viewModelData = JsonConvert.DeserializeObject<dynamic>(saveData.Content);
                if (viewModelData != null)
                {
                    _url = viewModelData.Url?.ToString() ?? "";
                    _downloadInfo = viewModelData.DownloadInfo?.ToString() ?? "";
                    OnPropertyChanged(nameof(Url));
                    OnPropertyChanged(nameof(DownloadInfo));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载文档错误: {ex.Message}");
        }
    }
}
