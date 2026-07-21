using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using BiliDownloader.Models;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 下载配置子 ViewModel：负责清晰度/音频选择、输出目录、分组文件夹、序号前缀
/// </summary>
public partial class DownloadConfigViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepository;

    public ObservableCollection<BiliQualityOption> QualityOptions { get; } = new();

    [ObservableProperty]
    private BiliQualityOption? _selectedQuality;

    public ObservableCollection<BiliQualityOption> AudioQualityOptions { get; } = new();

    [ObservableProperty]
    private BiliQualityOption? _selectedAudioQuality;

    [ObservableProperty]
    private bool _useGroupFolder;

    [ObservableProperty]
    private bool _addIndexToTitle = true;

    [ObservableProperty]
    private bool _isMultiVideo;

    [ObservableProperty]
    private string _outputDirectory = "";

    /// <summary>是否下载弹幕</summary>
    [ObservableProperty]
    private bool _downloadDanmaku;

    /// <summary>是否下载字幕</summary>
    [ObservableProperty]
    private bool _downloadSubtitle;

    /// <summary>是否下载封面图</summary>
    [ObservableProperty]
    private bool _downloadCover;

    public IRelayCommand SelectFolderCommand { get; }

    public DownloadConfigViewModel(ISettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
        SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync);
        _ = InitDefaultOutputDirectoryAsync();
    }

    /// <summary>
    /// 由主 VM 在解析成功后调用，填充清晰度选项
    /// </summary>
    public void PopulateQualities(
        List<BiliQualityOption> qualities,
        BiliQualityOption? selectedQuality,
        List<BiliQualityOption> audioQualities,
        BiliQualityOption? selectedAudioQuality,
        bool isMultiVideo)
    {
        QualityOptions.Clear();
        foreach (var q in qualities)
            QualityOptions.Add(q);
        SelectedQuality = selectedQuality;

        AudioQualityOptions.Clear();
        foreach (var a in audioQualities)
            AudioQualityOptions.Add(a);
        SelectedAudioQuality = selectedAudioQuality;

        IsMultiVideo = isMultiVideo;
        UseGroupFolder = isMultiVideo;
    }

    private async Task SelectFolderAsync()
    {
        try
        {
#pragma warning disable CS0618 // 现有 Avalonia 文件夹对话框迁移不属于 G0；此处保持用户交互行为不变。
            var dialog = new OpenFolderDialog
            {
                Title = "选择下载输出目录"
            };
#pragma warning restore CS0618

            var parentWindow = GetParentWindow();
            if (parentWindow != null)
            {
                var result = await dialog.ShowAsync(parentWindow);
                if (!string.IsNullOrEmpty(result))
                {
                    OutputDirectory = result;
                }
            }
        }
        catch (Exception ex)
        {
            // 忽略文件夹选择失败
            System.Diagnostics.Debug.WriteLine($"选择文件夹失败: {ex.Message}");
        }
    }

    private async Task InitDefaultOutputDirectoryAsync()
    {
        try
        {
            await _settingsRepository.InitAsync();
            var savedDir = await _settingsRepository.GetSettingAsync("default_output_dir");
            if (!string.IsNullOrEmpty(savedDir))
            {
                OutputDirectory = savedDir;
                return;
            }
        }
        catch { /* 忽略 */ }

        // 回退默认值：程序根目录/视频下载
        var appDir = Path.GetDirectoryName(typeof(DownloadConfigViewModel).Assembly.Location) ?? "";
        OutputDirectory = Path.Combine(appDir, "视频下载");
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
}
