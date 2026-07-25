using MySmallTools.Business.SecretVideoPlayer.Library;
using MySmallTools.ViewModels.SecretVideoPlayer.Library;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 媒体库 Document 的兼容外壳；播放、历史、布局和浏览协调位于 Library 功能包。
/// </summary>
public sealed class SecretVideoLibraryViewModel : LibraryDocumentCoordinatorViewModel
{
    public SecretVideoLibraryViewModel(
        VideoLibraryBrowserViewModel browser,
        VideoPlayerControlViewModel playerViewModel,
        IPlaybackHistoryStore? historyStore = null,
        IVideoLibrarySettingsStore? settingsStore = null,
        PlaybackHistoryCoordinator? historyCoordinator = null,
        ISecretVideoUserDataDiagnostics? userDataDiagnostics = null)
        : base(
            browser,
            playerViewModel,
            historyStore,
            settingsStore,
            historyCoordinator,
            userDataDiagnostics)
    {
    }
}
