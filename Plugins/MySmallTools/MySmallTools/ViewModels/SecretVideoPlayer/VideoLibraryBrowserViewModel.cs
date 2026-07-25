using MySmallTools.Business.SecretVideoPlayer.Library;
using MySmallTools.ViewModels.SecretVideoPlayer.Library;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 媒体库浏览器的兼容外壳；目录会话和可见投影由 Library 功能包统一实现。
/// </summary>
public sealed class VideoLibraryBrowserViewModel : LibraryBrowserCoordinatorViewModel
{
    public VideoLibraryBrowserViewModel(
        IVideoLibraryScanner scanner,
        IVideoLibrarySettingsStore? settingsStore = null,
        IPlaybackHistoryStore? historyStore = null,
        IVideoLibraryCatalogSession? catalog = null)
        : base(scanner, settingsStore, historyStore, catalog)
    {
    }
}
