using MySmallTools.Business.SecretVideoPlayer.Library;
using MySmallTools.ViewModels.SecretVideoPlayer.Library;
using MyAvaloniaManagement.PluginSdk;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 媒体库 Document 的兼容外壳；播放、历史、布局和浏览协调位于 Library 功能包。
/// </summary>
public sealed class SecretVideoLibraryViewModel : LibraryDocumentCoordinatorViewModel, IPluginDocument
{
    private string _title = "加密视频库播放器";

    public SecretVideoLibraryViewModel(
        VideoLibraryBrowserViewModel browser,
        VideoPlayerControlViewModel playerViewModel,
        IDocumentLifetime documentLifetime,
        IPlaybackHistoryStore? historyStore = null,
        IVideoLibrarySettingsStore? settingsStore = null,
        PlaybackHistoryCoordinator? historyCoordinator = null,
        ISecretVideoUserDataDiagnostics? userDataDiagnostics = null)
        : base(
            browser,
            playerViewModel,
            documentLifetime,
            historyStore,
            settingsStore,
            historyCoordinator,
            userDataDiagnostics)
    {
    }

    public DocumentPresentationState Presentation => new(_title);

    public event EventHandler? PresentationChanged;

    public ValueTask InitializeAsync(
        DocumentActivationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var title = string.IsNullOrWhiteSpace(context.Title) ? "加密视频库播放器" : context.Title;
        if (!string.Equals(_title, title, StringComparison.Ordinal))
        {
            _title = title;
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        return ValueTask.CompletedTask;
    }
}
