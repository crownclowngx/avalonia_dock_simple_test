using LibVLCSharp.Shared;

namespace MySmallTools.Business.SecretVideoPlayer.Playback;

/// <summary>播放器对界面公开的稳定生命周期状态。</summary>
public enum PlaybackState
{
    Empty,
    Ready,
    Stopped,
    Playing,
    Paused,
    Ended,
    Faulted,
    Disposed
}

/// <summary>
/// 描述播放器当前正在执行的非稳定活动。
/// <see cref="PlaybackState"/> 表示已经提交的播放状态，本枚举则用于告诉界面
/// “为什么当前命令需要等待”，避免把耗时的原生 Stop 或媒体切换表现成 UI 假死。
/// </summary>
public enum PlaybackActivity
{
    Idle,
    PreparingCandidate,
    WaitingForPlayer,
    StoppingCurrent,
    AttachingCandidate,
    StartingPlayback,
    Stopping,
    ReleasingOldMedia
}

/// <summary>播放器失败的稳定分类。用户界面不得根据异常文本推断失败原因。</summary>
public enum PlaybackFailureCode
{
    InvalidRequest,
    InvalidFormat,
    AuthenticationFailed,
    CorruptedContent,
    InputUnavailable,
    ParseFailed,
    DecodeFailed,
    SurfaceRestoreFailed,
    Cancelled,
    Unknown
}

/// <summary>可公开到界面和脱敏验收报告的播放失败。</summary>
public sealed record PlaybackFailure(PlaybackFailureCode Code, string Message);

/// <summary>播放操作的统一返回值。</summary>
public readonly record struct PlaybackOperationResult(
    bool Success,
    PlaybackFailure? Failure = null)
{
    public static PlaybackOperationResult Succeeded() => new(true);

    public static PlaybackOperationResult Failed(PlaybackFailure failure) =>
        new(false, failure ?? throw new ArgumentNullException(nameof(failure)));
}

/// <summary>一次原生视频表面的不可伪造代次和 HWND。</summary>
public readonly record struct VideoSurfaceToken(long Generation, nint Handle)
{
    public bool IsValid => Generation > 0 && Handle != nint.Zero;
}

/// <summary>播放会话当前的原子只读快照。</summary>
public sealed record PlaybackSnapshot(
    long MediaGeneration,
    PlaybackState State,
    bool IsTransitioning,
    long PositionMs,
    long DurationMs,
    bool IsSeekable,
    bool HasMedia,
    long SurfaceGeneration,
    int Volume,
    bool HasVideo,
    bool HasAudio,
    int VideoTrackCount,
    int AudioTrackCount,
    PlaybackActivity Activity)
{
    public static PlaybackSnapshot Empty { get; } = new(
        0,
        PlaybackState.Empty,
        false,
        0,
        0,
        false,
        false,
        0,
        50,
        false,
        false,
        0,
        0,
        PlaybackActivity.Idle);
}

/// <summary>携带媒体代次的统一播放通知。</summary>
public sealed class PlaybackChangedEventArgs(
    PlaybackSnapshot snapshot,
    PlaybackFailure? failure = null) : EventArgs
{
    public PlaybackSnapshot Snapshot { get; } =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public PlaybackFailure? Failure { get; } = failure;
}

/// <summary>
/// ViewModel 使用的窄播放端口。密码只存在于 LoadAsync 调用参数和同步调用链中。
/// </summary>
public interface ISecureVideoPlaybackSession : IDisposable
{
    event EventHandler<PlaybackChangedEventArgs>? Changed;

    PlaybackSnapshot Snapshot { get; }

    Task<PlaybackOperationResult> LoadAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在同一次媒体切换事务中完成候选验证、提交和启动播放。
    /// 该组合入口避免 ViewModel 在 Load 与 Play 之间观察到可被其他操作插入的中间状态。
    /// </summary>
    Task<PlaybackOperationResult> LoadAndPlayAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default);

    Task<PlaybackOperationResult> PlayAsync(CancellationToken cancellationToken = default);

    Task<PlaybackOperationResult> PauseAsync(CancellationToken cancellationToken = default);

    Task<PlaybackOperationResult> StopAsync(CancellationToken cancellationToken = default);

    Task<PlaybackOperationResult> SeekAsync(
        long positionMs,
        bool waitForFrame = false,
        CancellationToken cancellationToken = default);

    Task<PlaybackOperationResult> ReleaseAsync(CancellationToken cancellationToken = default);

    bool SetVolume(int volume);

    /// <summary>
    /// 在 NativeControlHost 销毁 HWND 前同步停止旧 vout 并保存一次性恢复快照。
    /// </summary>
    void DetachSurface(VideoSurfaceToken surface);

    /// <summary>绑定新 HWND，并在请求仍有效时恢复播放或暂停状态。</summary>
    Task<PlaybackOperationResult> AttachAndRestoreSurfaceAsync(
        VideoSurfaceToken surface,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 仅供 Avalonia/LibVLC 视频输出适配器使用的原生输出端口。
/// 业务 ViewModel 的播放行为不依赖该接口。
/// </summary>
public interface ILibVlcVideoOutputSource
{
    event EventHandler? OutputChanged;

    MediaPlayer? MediaPlayer { get; }
}
