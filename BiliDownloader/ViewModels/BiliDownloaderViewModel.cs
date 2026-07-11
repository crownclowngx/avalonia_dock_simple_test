using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Save;
using BiliDownloader.Constants;
using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services;
using BiliDownloader.Views.Login;
using BiliDownloader.ViewModels.Login;
using Newtonsoft.Json;

namespace BiliDownloader.ViewModels;

/// <summary>
/// BiliDownloader Document ViewModel：负责 URL 解析、参数收集、任务提交、进度接收
/// </summary>
public class BiliDownloaderViewModel : Document, ISavableDocument
{
    public string SaveDocumentTypeId => SaveDocumentTypeIdConstant.BiliDownloaderDocumentId;
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 本 Document 实例的唯一标识（持久化到 SaveData，跨重启不丢）
    /// </summary>
    public string DocumentId { get; private set; } = Guid.NewGuid().ToString("N");

    private readonly BiliApiService _apiService = new();
    private readonly IMessengerService? _messengerService;

    #region 属性

    private string _url = "";
    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    private string _downloadInfo = "";
    public string DownloadInfo
    {
        get => _downloadInfo;
        set => SetProperty(ref _downloadInfo, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _isLoggedIn;
    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        set => SetProperty(ref _isLoggedIn, value);
    }

    private string? _userName;
    public string? UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    private bool _isParsed;
    public bool IsParsed
    {
        get => _isParsed;
        set => SetProperty(ref _isParsed, value);
    }

    private BiliVideoCollection? _videoCollection;
    public BiliVideoCollection? VideoCollection
    {
        get => _videoCollection;
        set => SetProperty(ref _videoCollection, value);
    }

    public ObservableCollection<BiliVideoItem> VideoItems { get; } = new();

    public ObservableCollection<BiliQualityOption> QualityOptions { get; } = new();

    private BiliQualityOption? _selectedQuality;
    public BiliQualityOption? SelectedQuality
    {
        get => _selectedQuality;
        set => SetProperty(ref _selectedQuality, value);
    }

    private string _outputDirectory = "";
    public string OutputDirectory
    {
        get => _outputDirectory;
        set => SetProperty(ref _outputDirectory, value);
    }

    private double _totalProgress;
    public double TotalProgress
    {
        get => _totalProgress;
        set => SetProperty(ref _totalProgress, value);
    }

    #endregion

    #region Commands

    public IAsyncRelayCommand ParseCommand { get; }
    public IRelayCommand SelectFolderCommand { get; }
    public IRelayCommand SubmitDownloadCommand { get; }
    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand LogoutCommand { get; }

    #endregion

    public BiliDownloaderViewModel()
    {
        ParseCommand = new AsyncRelayCommand(ParseAsync);
        SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync);
        SubmitDownloadCommand = new RelayCommand(SubmitDownload);
        LoginCommand = new AsyncRelayCommand(ShowLoginWindowAsync);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync);

        // 默认输出目录
        OutputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "BiliDownloader");

        // 拉取当前登录状态
        var stateService = BiliLoginStateService.Instance;
        IsLoggedIn = stateService.IsLoggedIn;
        UserName = stateService.UserName;

        // 注册消息总线
        try
        {
            _messengerService = new MessengerService();

            // 登录状态变更
            _messengerService.Register<BiliDownloaderViewModel, LoginStateChangedMessage>(
                this, (vm, msg) =>
                {
                    vm.IsLoggedIn = msg.IsLoggedIn;
                    vm.UserName = msg.UserName;
                });

            // 下载进度回传（按 DocumentId 过滤）
            _messengerService.Register<BiliDownloaderViewModel, DownloadTaskProgressMessage>(
                this, (vm, msg) =>
                {
                    if (msg.TargetDocumentId != vm.DocumentId) return;
                    vm.HandleProgressMessage(msg);
                });
        }
        catch
        {
            // ServiceProvider 尚未初始化时忽略
        }
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

    /// <summary>
    /// 从 SQLite 恢复本 Document 的未完成任务状态（由 View 首次加载时调用）
    /// </summary>
    public async Task RecoverTasksFromStoreAsync()
    {
        try
        {
            var store = new DownloadTaskStore();
            await store.InitAsync();
            var records = await store.GetByDocumentIdAsync(DocumentId);

            foreach (var record in records)
            {
                // 查找已有的 VideoItem（按 ItemId 匹配），没有则创建
                var item = VideoItems.FirstOrDefault(v => v.ItemId == record.TaskId);
                if (item == null)
                {
                    item = new BiliVideoItem
                    {
                        ItemId = record.TaskId,
                        Title = record.ItemTitle,
                        Aid = record.Aid,
                        Bvid = record.Bvid,
                        Cid = record.Cid,
                        IsSelected = false, // 已提交的任务不再勾选
                    };
                    VideoItems.Add(item);
                }

                item.Status = MapStatusToDisplay(record.Status);
                item.Progress = record.Progress;
            }
        }
        catch (Exception ex)
        {
            DownloadInfo = $"恢复任务状态失败: {ex.Message}";
        }
    }

    #region 解析逻辑

    private async Task ParseAsync()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            DownloadInfo = "请输入有效的B站视频链接";
            return;
        }

        if (!IsLoggedIn)
        {
            DownloadInfo = "请先登录后再解析";
            await ShowLoginWindowAsync();
            return;
        }

        var parsed = BiliApiService.ParseVideoId(Url);
        if (parsed == null)
        {
            DownloadInfo = "无法解析链接，请输入有效的B站视频链接（BV号或av号）";
            return;
        }

        try
        {
            IsLoading = true;
            DownloadInfo = "正在解析视频信息...";

            var cookie = BiliLoginStateService.Instance.CookieHeader;

            // 获取视频集合
            var collection = await _apiService.GetVideoCollectionAsync(
                parsed.Value.Id, parsed.Value.IsBvid, cookie);
            VideoCollection = collection;

            // 填充视频列表
            VideoItems.Clear();
            foreach (var item in collection.Items)
                VideoItems.Add(item);

            // 获取可用清晰度（用第一个视频试探）
            QualityOptions.Clear();
            if (collection.Items.Count > 0)
            {
                var first = collection.Items[0];
                var qualities = await _apiService.GetAvailableQualitiesAsync(
                    first.Aid, first.Cid, cookie);
                foreach (var q in qualities)
                    QualityOptions.Add(q);

                // 默认选最高画质
                SelectedQuality = QualityOptions.FirstOrDefault();
            }

            IsParsed = true;
            DownloadInfo = $"解析成功: {collection.SeriesTitle} ({collection.Items.Count} 个视频)";
            IsModified = true;
        }
        catch (Exception ex)
        {
            DownloadInfo = $"解析异常: {ex.Message}";
            IsParsed = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region 任务提交

    private void SubmitDownload()
    {
        if (!IsParsed || VideoItems.Count == 0)
        {
            DownloadInfo = "请先解析视频";
            return;
        }

        var selectedItems = VideoItems.Where(v => v.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            DownloadInfo = "请至少勾选一个视频";
            return;
        }

        if (SelectedQuality == null)
        {
            DownloadInfo = "请选择清晰度";
            return;
        }

        // 检查 ffmpeg 是否就绪
        if (!FfmpegService.IsReady)
        {
            DownloadInfo = "ffmpeg 未就绪，请在调度器工具中等待下载完成或手动配置路径";
            return;
        }

        // 构造消息
        var downloadItems = selectedItems.Select(v => new DownloadItemInfo
        {
            ItemId = v.ItemId,
            Title = v.Title,
            Aid = v.Aid,
            Bvid = v.Bvid,
            Cid = v.Cid,
            Duration = v.Duration,
        }).ToList();

        var message = new SubmitDownloadTaskMessage(
            sourceDocumentId: DocumentId,
            seriesTitle: VideoCollection?.SeriesTitle ?? "下载",
            items: downloadItems,
            qualityId: SelectedQuality.QualityId,
            outputDirectory: OutputDirectory,
            cookie: BiliLoginStateService.Instance.CookieHeader);

        // 通过消息总线发送给调度器
        try
        {
            _messengerService?.Send(message);
            DownloadInfo = $"已提交 {selectedItems.Count} 个下载任务到调度器";

            // 标记为已提交
            foreach (var item in selectedItems)
            {
                item.Status = "排队中";
                item.IsSelected = false;
            }
        }
        catch (Exception ex)
        {
            DownloadInfo = $"提交任务失败: {ex.Message}";
        }
    }

    #endregion

    #region 进度接收

    private void HandleProgressMessage(DownloadTaskProgressMessage msg)
    {
        var item = VideoItems.FirstOrDefault(v => v.ItemId == msg.TaskId);
        if (item == null) return;

        item.Status = MapStatusToDisplay(msg.Status);
        item.Progress = msg.Progress;

        if (msg.Status == "failed" && !string.IsNullOrEmpty(msg.ErrorMessage))
        {
            item.Status = $"失败: {msg.ErrorMessage}";
        }

        // 更新总进度
        if (VideoItems.Count > 0)
        {
            TotalProgress = VideoItems.Average(v => v.Progress);
        }
    }

    private static string MapStatusToDisplay(string status) => status switch
    {
        "pending" => "排队中",
        "downloading_video" => "下载视频",
        "downloading_audio" => "下载音频",
        "merging" => "合并中",
        "done" => "完成",
        "failed" => "失败",
        _ => status,
    };

    #endregion

    #region 文件夹选择

    private async Task SelectFolderAsync()
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择下载输出目录"
            };

            var parentWindow = GetParentWindow();
            if (parentWindow != null)
            {
                var result = await dialog.ShowAsync(parentWindow);
                if (!string.IsNullOrEmpty(result))
                    OutputDirectory = result;
            }
        }
        catch (Exception ex)
        {
            DownloadInfo = $"选择文件夹失败: {ex.Message}";
        }
    }

    #endregion

    #region 登录相关

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

    #endregion

    #region 持久化

    public DocumentSaveData CreateSaveDocumentMetaData(string filePath)
    {
        var saveDataObject = new
        {
            DocumentId,
            Url = _url,
            DownloadInfo = _downloadInfo,
            OutputDirectory = _outputDirectory,
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
            if (saveData == null) return;
            var data = JsonConvert.DeserializeObject<dynamic>(saveData.Content);
            if (data == null) return;

            _url = data.Url?.ToString() ?? "";
            _downloadInfo = data.DownloadInfo?.ToString() ?? "";
            _outputDirectory = data.OutputDirectory?.ToString() ?? _outputDirectory;

            // 恢复 DocumentId
            var savedDocId = data.DocumentId?.ToString();
            if (!string.IsNullOrEmpty(savedDocId))
                DocumentId = savedDocId;

            OnPropertyChanged(nameof(Url));
            OnPropertyChanged(nameof(DownloadInfo));
            OnPropertyChanged(nameof(OutputDirectory));
            OnPropertyChanged(nameof(DocumentId));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载文档错误: {ex.Message}");
        }
    }

    #endregion
}
