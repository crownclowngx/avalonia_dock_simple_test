using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.ContentSources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiliDownloader.ViewModels.BiliDownloader;

public enum DownloadCreationMode { QuickUrl, PersonalSource }

/// <summary>来源阶段的轻量编排 VM；不负责分页细节，也不负责下载配置与提交。</summary>
public partial class DownloadSourceWorkflowViewModel : ObservableObject
{
    public DownloadSourceWorkflowViewModel(
        VideoParseViewModel quickUrl,
        IContentSourceProviderRegistry registry,
        IFavoriteSourceDiscoveryService favorites,
        VideoParseResultFactory resultFactory,
        Action<VideoParseResult> onResolved)
    {
        QuickUrl = quickUrl;
        Browser = new ContentSourceBrowserViewModel(registry, resultFactory, onResolved);
        Picker = new ContentSourcePickerViewModel(registry, favorites, OpenDescriptorAsync);
    }

    public VideoParseViewModel QuickUrl { get; }
    public ContentSourcePickerViewModel Picker { get; }
    public ContentSourceBrowserViewModel Browser { get; }
    [ObservableProperty] private DownloadCreationMode _mode;
    [ObservableProperty] private bool _isBrowsing;
    public bool IsQuickUrl => Mode == DownloadCreationMode.QuickUrl;
    public bool IsPersonalSource => Mode == DownloadCreationMode.PersonalSource;

    public void SetInitialMode(DownloadCreationMode mode)
    {
        Mode = mode;
        IsBrowsing = false;
    }

    partial void OnModeChanged(DownloadCreationMode value)
    {
        OnPropertyChanged(nameof(IsQuickUrl));
        OnPropertyChanged(nameof(IsPersonalSource));
        IsBrowsing = false;
    }

    [RelayCommand] private void UseQuickUrl() => Mode = DownloadCreationMode.QuickUrl;
    [RelayCommand] private void UsePersonalSource() => Mode = DownloadCreationMode.PersonalSource;
    [RelayCommand] private void BackToSources() => IsBrowsing = false;

    private async Task OpenDescriptorAsync(ContentSourceDescriptor descriptor)
    {
        IsBrowsing = true;
        await Browser.OpenAsync(descriptor);
    }
}

internal sealed class UnavailableFavoriteSourceDiscoveryService : IFavoriteSourceDiscoveryService
{
    public Task<IReadOnlyList<ContentSourceDescriptor>> GetMyFoldersAsync(CancellationToken cancellationToken) =>
        throw new ContentSourceException(ContentSourceErrorCode.UnknownProvider, "收藏夹来源尚未注册。");
}
