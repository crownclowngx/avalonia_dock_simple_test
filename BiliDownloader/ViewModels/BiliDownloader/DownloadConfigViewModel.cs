using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using BiliDownloader.Models;
using BiliDownloader.Services;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 下载配置子 ViewModel：负责清晰度/音频选择、输出目录、分组文件夹、序号前缀
/// </summary>
public partial class DownloadConfigViewModel : ObservableObject
{
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

    public IRelayCommand SelectFolderCommand { get; }

    public DownloadConfigViewModel()
    {
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
            var dialog = new OpenFolderDialog
            {
                Title = "选择下载输出目录"
            };

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
            var store = new DownloadTaskStore();
            await store.InitAsync();
            var savedDir = await store.GetSettingAsync("default_output_dir");
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
