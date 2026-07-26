using MySmallTools.Business.SecretVideoPlayer.Library;
using MySmallTools.Business.SecretVideoPlayer.Playback;
using MySmallTools.ViewModels.SecretVideoPlayer.Playback;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 播放控件的兼容外壳；播放状态、命令和呈现协调由 Playback 功能包统一实现。
/// </summary>
public sealed class VideoPlayerControlViewModel : PlaybackCoordinatorViewModel
{
    public VideoPlayerControlViewModel(
        ISecureVideoPlaybackSession session,
        IPlaybackSurfaceSession surfaceSession,
        IPlaybackPlatformStatus platformStatus,
        IPlaybackBackendInitializer backendInitializer,
        IPlaybackPreferenceStore? preferenceStore = null)
        : base(
            session,
            surfaceSession,
            platformStatus,
            backendInitializer,
            preferenceStore)
    {
    }
}
