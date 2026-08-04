using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.ViewModels.BiliScheduler;

/// <summary>
/// 设置子 ViewModel：负责 ffmpeg 管理和默认输出目录配置。
/// </summary>
public partial class SchedulerSettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsStore;
    private readonly IFfmpegRuntimeLocator _ffmpegService;
    private readonly IFfmpegPackageInstaller? _ffmpegInstaller;
    private bool _settingsLoaded;

    [ObservableProperty]
    private bool _ffmpegReady;

    [ObservableProperty]
    private string _ffmpegPath = "";

    [ObservableProperty]
    private string _ffmpegStatus = "检测中...";

    [ObservableProperty]
    private string _ffmpegVersion = "";

    [ObservableProperty]
    private string _ffmpegSource = "";

    [ObservableProperty]
    private bool _isInstallingFfmpeg;

    [ObservableProperty]
    private double _ffmpegInstallProgress;

    [ObservableProperty]
    private string _defaultOutputDirectory = "";

    [ObservableProperty]
    private int _maxConcurrentDownloads = 1;

    public List<int> ConcurrentOptions { get; } = new() { 1, 2, 3, 4, 5 };

    /// <summary>并发下载数变更通知事件（供外部订阅）</summary>
    public event Action<int>? MaxConcurrentDownloadsChanged;

    public IAsyncRelayCommand BrowseFfmpegCommand { get; }
    public IAsyncRelayCommand RedetectFfmpegCommand { get; }
    public IAsyncRelayCommand InstallOrRepairFfmpegCommand { get; }
    public IAsyncRelayCommand BrowseOutputDirCommand { get; }

    public SchedulerSettingsViewModel(
        ISettingsRepository settingsStore,
        IFfmpegRuntimeLocator ffmpegService,
        IFfmpegPackageInstaller? ffmpegInstaller = null)
    {
        _settingsStore = settingsStore;
        _ffmpegService = ffmpegService;
        _ffmpegInstaller = ffmpegInstaller;

        BrowseFfmpegCommand = new AsyncRelayCommand(BrowseFfmpegAsync);
        RedetectFfmpegCommand = new AsyncRelayCommand(CheckFfmpegAsync);
        InstallOrRepairFfmpegCommand = new AsyncRelayCommand(InstallOrRepairFfmpegAsync);
        BrowseOutputDirCommand = new AsyncRelayCommand(BrowseOutputDirAsync);
        if (_ffmpegInstaller is not null)
            _ffmpegInstaller.ProgressChanged += OnInstallProgressChanged;

        // 默认输出目录：程序根目录/视频下载
        var appDir = Path.GetDirectoryName(typeof(SchedulerSettingsViewModel).Assembly.Location) ?? "";
        DefaultOutputDirectory = Path.Combine(appDir, "视频下载");
    }

    /// <summary>
    /// 从 SQLite 加载已保存的设置（ffmpeg 路径 + 默认输出目录）
    /// </summary>
    public async Task LoadSettingsAsync()
    {
        await _settingsStore.InitAsync();

        var savedDir = await _settingsStore.GetSettingAsync("default_output_dir");
        if (!string.IsNullOrEmpty(savedDir))
            DefaultOutputDirectory = savedDir;

        var savedFfmpeg = await _settingsStore.GetSettingAsync("ffmpeg_custom_path");
        if (!string.IsNullOrEmpty(savedFfmpeg))
            _ffmpegService.CustomPath = savedFfmpeg;

        var savedConcurrency = await _settingsStore.GetSettingAsync("max_concurrent_downloads");
        if (int.TryParse(savedConcurrency, out var n) && n >= 1 && n <= 5)
            MaxConcurrentDownloads = n;

        _settingsLoaded = true;
    }

    /// <summary>
    /// DefaultOutputDirectory 变化时自动保存到 SQLite
    /// </summary>
    partial void OnDefaultOutputDirectoryChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && _settingsLoaded)
            _ = _settingsStore.SetSettingAsync("default_output_dir", value);
    }

    /// <summary>
    /// MaxConcurrentDownloads 变化时自动保存到 SQLite 并通知外部
    /// </summary>
    partial void OnMaxConcurrentDownloadsChanged(int value)
    {
        if (_settingsLoaded)
        {
            _ = _settingsStore.SetSettingAsync("max_concurrent_downloads", value.ToString());
            MaxConcurrentDownloadsChanged?.Invoke(value);
        }
    }

    #region ffmpeg 管理

    /// <summary>
    /// 检测 ffmpeg 是否就绪
    /// </summary>
    public async Task CheckFfmpegAsync()
    {
        FfmpegStatus = "正在重新检测 ffmpeg…";
        var status = await _ffmpegService.DetectAsync();
        FfmpegReady = status.IsReady;
        FfmpegPath = status.ExecutablePath ?? "";
        FfmpegVersion = status.Version ?? "";
        FfmpegSource = ToSourceText(status.Source);
        FfmpegStatus = status.Message;
    }

    /// <summary>
    /// 只有用户点击按钮才进入安装流程。该命令不会在加载设置或提交任务时被隐式调用，
    /// 从而保持“启动不联网、安装有明确用户意图”的产品约束。
    /// </summary>
    private async Task InstallOrRepairFfmpegAsync()
    {
        if (_ffmpegInstaller is null)
        {
            FfmpegStatus = "当前构造路径未提供 ffmpeg 安装服务，请选择自定义路径。";
            return;
        }

        try
        {
            IsInstallingFfmpeg = true;
            FfmpegInstallProgress = 0;
            var result = await _ffmpegInstaller.InstallOrRepairAsync();
            FfmpegStatus = result.Message;
            if (result.Success)
            {
                // 安装内置版本代表用户选择托管运行时，清空旧自定义配置防止重启后再次遮蔽它。
                await _settingsStore.SetSettingAsync("ffmpeg_custom_path", "");
                await CheckFfmpegAsync();
            }
        }
        catch (OperationCanceledException)
        {
            FfmpegStatus = "ffmpeg 安装已取消，原有版本未改变。";
        }
        finally
        {
            IsInstallingFfmpeg = false;
        }
    }

    private void OnInstallProgressChanged(FfmpegInstallProgress progress)
    {
        FfmpegInstallProgress = progress.Percentage;
        FfmpegStatus = progress.Message;
    }

    /// <summary>
    /// 浏览选择 ffmpeg.exe 路径
    /// </summary>
    private async Task BrowseFfmpegAsync()
    {
        try
        {
            var parentWindow = GetParentWindow();
            if (parentWindow is null)
            {
                return;
            }

            var result = await parentWindow.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "选择 ffmpeg.exe",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("可执行文件") { Patterns = ["*.exe"] },
                        FilePickerFileTypes.All
                    ]
                });
            if (result.Count == 0)
            {
                return;
            }

            var selectedPath = result[0].Path.LocalPath;
            FfmpegStatus = "正在验证 ffmpeg...";

            var valid = await _ffmpegService.ValidatePathAsync(selectedPath);
            if (valid)
            {
                _ffmpegService.CustomPath = selectedPath;
                await _settingsStore.SetSettingAsync("ffmpeg_custom_path", selectedPath);
                await CheckFfmpegAsync();
            }
            else
            {
                FfmpegStatus = $"无效路径: {selectedPath}";
                FfmpegReady = false;
            }
        }
        catch (Exception ex)
        {
            FfmpegStatus = $"选择 ffmpeg 失败: {ex.Message}";
        }
    }

    #endregion

    private static string ToSourceText(FfmpegRuntimeSource source) => source switch
    {
        FfmpegRuntimeSource.Custom => "自定义路径",
        FfmpegRuntimeSource.Managed => "内置托管版本",
        FfmpegRuntimeSource.Plugin => "插件目录",
        FfmpegRuntimeSource.Path => "系统 PATH",
        _ => "未检测到",
    };

    #region 输出目录管理

    /// <summary>
    /// 浏览选择默认输出目录
    /// </summary>
    private async Task BrowseOutputDirAsync()
    {
        try
        {
            var parentWindow = GetParentWindow();
            if (parentWindow is null)
            {
                return;
            }

            var result = await parentWindow.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "选择默认下载输出目录",
                    AllowMultiple = false
                });
            if (result.Count > 0)
            {
                DefaultOutputDirectory = result[0].Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            FfmpegStatus = $"选择文件夹失败: {ex.Message}";
        }
    }

    #endregion

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
}
