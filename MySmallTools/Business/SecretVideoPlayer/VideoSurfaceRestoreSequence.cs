using LibVLCSharp.Shared;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 可替换的视频表面恢复操作，便于验证 vout 重建顺序而不依赖真实窗口。
/// </summary>
internal interface IVideoSurfaceRestoreOperations
{
    long Length { get; }
    bool Play();
    Task WaitForVideoOutputAsync(CancellationToken cancellationToken);
    Task SeekAsync(long positionMs, bool waitForFrame, CancellationToken cancellationToken);
    void Pause();
}

internal static class VideoSurfaceRestoreSequence
{
    public static async Task<bool> ExecuteAsync(
        IVideoSurfaceRestoreOperations operations,
        long positionMs,
        bool restorePaused,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!operations.Play())
        {
            return false;
        }

        await operations.WaitForVideoOutputAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var maximumPosition = Math.Max(0, operations.Length - 250);
        var targetPosition = Math.Clamp(positionMs, 0, maximumPosition);
        await operations.SeekAsync(targetPosition, restorePaused, cancellationToken);

        if (restorePaused)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operations.Pause();
        }

        return true;
    }
}

/// <summary>
/// 基于 LibVLC MediaPlayer 事件实现真实的输出和首帧等待。
/// </summary>
internal sealed class LibVlcVideoSurfaceRestoreOperations(MediaPlayer player) : IVideoSurfaceRestoreOperations
{
    public long Length => player.Length;

    public bool Play() => player.Play();

    public async Task WaitForVideoOutputAsync(CancellationToken cancellationToken)
    {
        var outputReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void TryComplete()
        {
            if ((player.IsPlaying || player.State == VLCState.Playing) && player.VoutCount > 0)
            {
                outputReady.TrySetResult();
            }
        }

        void OnPlaying(object? sender, EventArgs args) => TryComplete();
        void OnVout(object? sender, MediaPlayerVoutEventArgs args) => TryComplete();

        player.Playing += OnPlaying;
        player.Vout += OnVout;
        try
        {
            TryComplete();
            await outputReady.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            player.Playing -= OnPlaying;
            player.Vout -= OnVout;
        }
    }

    public async Task SeekAsync(long positionMs, bool waitForFrame, CancellationToken cancellationToken)
    {
        if (!waitForFrame)
        {
            player.Time = positionMs;
            return;
        }

        var frameReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seekIssued = 0;

        void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs args)
        {
            if (Volatile.Read(ref seekIssued) != 0)
            {
                frameReady.TrySetResult();
            }
        }

        player.TimeChanged += OnTimeChanged;
        try
        {
            player.Time = positionMs;
            // 在设置 Time 返回后才接受 TimeChanged，避免把 seek 前正在播放的旧帧误判为目标帧。
            Volatile.Write(ref seekIssued, 1);
            await frameReady.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            player.TimeChanged -= OnTimeChanged;
        }
    }

    public void Pause() => player.SetPause(true);
}
