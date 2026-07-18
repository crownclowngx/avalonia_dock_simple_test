using Avalonia.Controls;
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
    private bool _settingsLoaded;

    [ObservableProperty]
    private bool _ffmpegReady;

    [ObservableProperty]
    private string _ffmpegPath = "";

    [ObservableProperty]
    private string _ffmpegStatus = "检测中...";

    [ObservableProperty]
    private string _defaultOutputDirectory = "";

    public IAsyncRelayCommand BrowseFfmpegCommand { get; }
    public IAsyncRelayCommand BrowseOutputDirCommand { get; }

    public SchedulerSettingsViewModel(ISettingsRepository settingsStore)
    {
        _settingsStore = settingsStore;

        BrowseFfmpegCommand = new AsyncRelayCommand(BrowseFfmpegAsync);
        BrowseOutputDirCommand = new AsyncRelayCommand(BrowseOutputDirAsync);

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
            FfmpegService.CustomPath = savedFfmpeg;

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

    #region ffmpeg 管理

    /// <summary>
    /// 检测 ffmpeg 是否就绪
    /// </summary>
    public async Task CheckFfmpegAsync()
    {
        await Task.Run(() =>
        {
            var path = FfmpegService.ResolveFfmpegPath();
            FfmpegReady = path != null;
            FfmpegPath = path ?? "";
            FfmpegStatus = FfmpegReady
                ? $"ffmpeg 就绪: {path}"
                : "ffmpeg 未找到，请将 ffmpeg.exe 放入工具目录或手动浏览选择";
        });
    }

    /// <summary>
    /// 浏览选择 ffmpeg.exe 路径
    /// </summary>
    private async Task BrowseFfmpegAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 ffmpeg.exe",
                Filters = new List<FileDialogFilter>
                {
                    new() { Name = "可执行文件", Extensions = { "exe" } },
                    new() { Name = "所有文件", Extensions = { "*" } }
                }
            };

            var parentWindow = GetParentWindow();
            if (parentWindow == null) return;

            var result = await dialog.ShowAsync(parentWindow);
            if (result == null || result.Length == 0) return;

            var selectedPath = result[0];
            FfmpegStatus = "正在验证 ffmpeg...";

            var valid = await FfmpegService.ValidatePathAsync(selectedPath);
            if (valid)
            {
                FfmpegService.CustomPath = selectedPath;
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

    #region 输出目录管理

    /// <summary>
    /// 浏览选择默认输出目录
    /// </summary>
    private async Task BrowseOutputDirAsync()
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择默认下载输出目录"
            };

            var parentWindow = GetParentWindow();
            if (parentWindow != null)
            {
                var result = await dialog.ShowAsync(parentWindow);
                if (!string.IsNullOrEmpty(result))
                    DefaultOutputDirectory = result;
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
