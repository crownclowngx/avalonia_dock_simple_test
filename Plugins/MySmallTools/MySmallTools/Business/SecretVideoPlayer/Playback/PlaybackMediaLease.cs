using LibVLCSharp.Shared;
using MySmallTools.Business.SecretVideoPlayer.Container;

namespace MySmallTools.Business.SecretVideoPlayer.Playback;

/// <summary>G3 压力门禁使用的确定性资源快照。</summary>
public readonly record struct PlaybackResourceSnapshot(
    int LiveLeases,
    int LivePlayers,
    int LiveMediaInputs,
    int LiveEncryptedStreams,
    int ActiveSurfaceRestores,
    int CachedPlaintextChunks);

/// <summary>为 G3 脱敏压力门禁公开的只读资源计数，不暴露媒体路径或内容。</summary>
public static class SecurePlaybackDiagnostics
{
    public static PlaybackResourceSnapshot CaptureResources()
    {
        var playback = PlaybackResourceDiagnostics.Capture();
        var streams = EncryptedStreamResourceDiagnostics.Capture();
        return playback with
        {
            LiveEncryptedStreams = streams.LiveStreams,
            CachedPlaintextChunks = streams.CachedPlaintextChunks
        };
    }

    public static IReadOnlyList<long> CaptureRecentChunkReads() =>
        EncryptedStreamResourceDiagnostics.CaptureRecentChunkReads();

    public static void ClearRecentChunkReads() =>
        EncryptedStreamResourceDiagnostics.ClearRecentChunkReads();
}

internal static class PlaybackResourceDiagnostics
{
    private static int _liveLeases;
    private static int _livePlayers;
    private static int _liveMediaInputs;
    private static int _activeSurfaceRestores;

    public static PlaybackResourceSnapshot Capture() => new(
        Volatile.Read(ref _liveLeases),
        Volatile.Read(ref _livePlayers),
        Volatile.Read(ref _liveMediaInputs),
        0,
        Volatile.Read(ref _activeSurfaceRestores),
        0);

    public static void LeaseCreated() => Interlocked.Increment(ref _liveLeases);
    public static void LeaseDisposed() => Interlocked.Decrement(ref _liveLeases);
    public static void PlayerCreated() => Interlocked.Increment(ref _livePlayers);
    public static void PlayerDisposed() => Interlocked.Decrement(ref _livePlayers);
    public static void InputCreated() => Interlocked.Increment(ref _liveMediaInputs);
    public static void InputDisposed() => Interlocked.Decrement(ref _liveMediaInputs);
    public static void SurfaceRestoreStarted() => Interlocked.Increment(ref _activeSurfaceRestores);
    public static void SurfaceRestoreFinished() => Interlocked.Decrement(ref _activeSurfaceRestores);
}

internal sealed class PlaybackOperationException(
    PlaybackFailure failure,
    Exception? innerException = null) : Exception(failure.Message, innerException)
{
    public PlaybackFailure Failure { get; } = failure;
}

internal interface IPlaybackMediaLease : IDisposable
{
    long Generation { get; }
    MediaPlayer? NativePlayer { get; }
    long PositionMs { get; }
    long DurationMs { get; }
    bool IsSeekable { get; }
    bool HasVideo { get; }
    bool HasAudio { get; }
    int VideoTrackCount { get; }
    int AudioTrackCount { get; }
    bool IsPlaying { get; }
    bool IsPaused { get; }
    int Volume { get; }

    event Action<IPlaybackMediaLease, PlaybackState>? StateChanged;
    event EventHandler? PositionChanged;
    event Action<IPlaybackMediaLease, PlaybackFailure>? Failed;

    void PrepareForPlayback();
    void RequestStop();
    void Stop();
    void SetPause(bool paused);
    bool Play();
    void SetVolume(int volume);
    void SetVideoOutputHandle(nint handle);
    Task SeekAsync(long positionMs, bool waitForFrame, CancellationToken cancellationToken);
    Task<bool> RestoreSurfaceAsync(
        long positionMs,
        bool restorePaused,
        CancellationToken cancellationToken);
}

internal interface IPlaybackMediaLeaseFactory
{
    Task<IPlaybackMediaLease> CreateAsync(
        long generation,
        string filePath,
        string password,
        CancellationToken cancellationToken);
}

/// <summary>每个 Document 独占的 LibVLC 适配器和媒体 Lease 工厂。</summary>
internal sealed class LibVlcPlaybackMediaLeaseFactory : IPlaybackMediaLeaseFactory, IDisposable
{
    private readonly LibVLC _libVlc;
    private int _disposed;

    public LibVlcPlaybackMediaLeaseFactory(LibVlcRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.EnsureInitialized();
        _libVlc = new LibVLC();
    }

    public async Task<IPlaybackMediaLease> CreateAsync(
        long generation,
        string filePath,
        string password,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return await PlaybackMediaLease.CreateAsync(
                _libVlc,
                generation,
                filePath,
                password,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _libVlc.Dispose();
        }
    }
}

/// <summary>
/// 独占一代媒体的原生播放器、Media、MediaInput 和认证随机读取流。
/// 只有本对象可以决定它们的停止与释放顺序。
/// </summary>
internal sealed class PlaybackMediaLease : IPlaybackMediaLease
{
    private readonly SeekableStreamMediaInput _input;
    private readonly Media _media;
    private readonly MediaPlayer _player;
    private PlaybackFailure? _firstFailure;
    private int _failurePublished;
    private int _disposeState;

    private PlaybackMediaLease(
        long generation,
        SeekableStreamMediaInput input,
        Media media,
        MediaPlayer player)
    {
        Generation = generation;
        _input = input;
        _media = media;
        _player = player;

        PlaybackResourceDiagnostics.LeaseCreated();
        PlaybackResourceDiagnostics.InputCreated();
        PlaybackResourceDiagnostics.PlayerCreated();
        Subscribe();
    }

    public long Generation { get; }
    public MediaPlayer? NativePlayer => _player;
    public long PositionMs => Math.Max(0, _player.Time);
    public long DurationMs => Math.Max(0, _player.Length);
    public bool IsSeekable => _player.IsSeekable;
    public bool HasVideo => _player.VideoTrackCount > 0;
    public bool HasAudio => _player.AudioTrackCount > 0;
    public int VideoTrackCount => _player.VideoTrackCount;
    public int AudioTrackCount => _player.AudioTrackCount;
    public bool IsPlaying => _player.IsPlaying || _player.State == VLCState.Playing;
    public bool IsPaused => _player.State == VLCState.Paused;
    public int Volume => _player.Volume;

    public event Action<IPlaybackMediaLease, PlaybackState>? StateChanged;
    public event EventHandler? PositionChanged;
    public event Action<IPlaybackMediaLease, PlaybackFailure>? Failed;

    public static async Task<PlaybackMediaLease> CreateAsync(
        LibVLC libVlc,
        long generation,
        string filePath,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(libVlc);
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrEmpty(password))
        {
            throw new PlaybackOperationException(
                new PlaybackFailure(PlaybackFailureCode.InvalidRequest, "文件路径和密码不能为空。"));
        }

        SeekableStreamMediaInput? input = null;
        Media? media = null;
        MediaPlayer? player = null;
        try
        {
            var stream = SeekableEncryptedVideoStream.Open(filePath, password);
            input = new SeekableStreamMediaInput(stream);
            media = new Media(libVlc, input);

            var parsedStatus = await media
                // MediaInput 在 LibVLC 中使用回调 MRL，不属于普通 file:// 本地路径；
                // ParseNetwork 才会实际驱动 Open/Read/Seek 回调完成预解析。
                .Parse(MediaParseOptions.ParseNetwork, 15_000, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (parsedStatus is MediaParsedStatus.Failed or MediaParsedStatus.Timeout)
            {
                if (input.TryTakeLastFailure(out var inputFailure) && inputFailure is not null)
                {
                    throw new PlaybackOperationException(inputFailure);
                }

                throw new PlaybackOperationException(PlaybackFailureMapper.ParseFailed());
            }

            if (parsedStatus == MediaParsedStatus.Skipped &&
                input.TryTakeLastFailure(out var skippedInputFailure) &&
                skippedInputFailure is not null)
            {
                throw new PlaybackOperationException(skippedInputFailure);
            }

            // LibVLC 3.0.21 对 MediaInput 回调 MRL 会返回 Skipped。它不能被当成解析成功，
            // 但也不能机械判为失败；G3 在这种情况下由实际 Play/轨道/Seek 门禁完成解码验证。

            player = new MediaPlayer(libVlc)
            {
                Media = media
            };

            var lease = new PlaybackMediaLease(generation, input, media, player);
            input = null;
            media = null;
            player = null;
            return lease;
        }
        catch (PlaybackOperationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PlaybackOperationException(PlaybackFailureMapper.MapLoad(ex), ex);
        }
        finally
        {
            player?.Dispose();
            media?.Dispose();
            input?.Dispose();
        }
    }

    public void PrepareForPlayback() => _input.PrepareForPlayback();

    public void RequestStop() => _input.RequestStop();

    public void Stop()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        _input.RequestStop();
        _player.Stop();
    }

    public void SetPause(bool paused)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        _player.SetPause(paused);
    }

    public bool Play()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        _input.PrepareForPlayback();
        return _player.Play();
    }

    public void SetVolume(int volume)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        _player.Volume = Math.Clamp(volume, 0, 100);
    }

    public void SetVideoOutputHandle(nint handle)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        _player.Hwnd = handle;
    }

    public async Task SeekAsync(
        long positionMs,
        bool waitForFrame,
        CancellationToken cancellationToken)
    {
        var operations = new LibVlcVideoSurfaceRestoreOperations(_player);
        var inputFailed = new TaskCompletionSource<PlaybackFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFailure(IPlaybackMediaLease _, PlaybackFailure failure) =>
            inputFailed.TrySetResult(failure);
        Failed += OnFailure;

        try
        {
            Task seekTask;

            // LibVLC 3.x does not consistently publish TimeChanged while a custom
            // MediaInput is paused. A frame-confirmed paused seek therefore uses
            // the same Play -> vout -> Seek -> explicit Pause sequence as a
            // surface restore. SetPause(true) keeps this operation idempotent.
            if (waitForFrame && IsPaused)
            {
                seekTask = RestorePausedSeekAsync();
            }
            else
            {
                seekTask = operations.SeekAsync(positionMs, waitForFrame, cancellationToken);
            }

            var completed = await Task
                .WhenAny(seekTask, inputFailed.Task)
                .ConfigureAwait(false);
            if (completed == inputFailed.Task)
            {
                throw new PlaybackOperationException(
                    await inputFailed.Task.ConfigureAwait(false));
            }

            await seekTask.ConfigureAwait(false);
        }
        finally
        {
            Failed -= OnFailure;
        }

        async Task RestorePausedSeekAsync()
        {
            var restored = await VideoSurfaceRestoreSequence
                .ExecuteAsync(operations, positionMs, restorePaused: true, cancellationToken)
                .ConfigureAwait(false);
            if (!restored)
            {
                throw new PlaybackOperationException(new PlaybackFailure(
                    PlaybackFailureCode.DecodeFailed,
                    "The media decoder could not resume for seeking."));
            }
        }
    }

    public Task<bool> RestoreSurfaceAsync(
        long positionMs,
        bool restorePaused,
        CancellationToken cancellationToken)
    {
        _input.PrepareForPlayback();
        return VideoSurfaceRestoreSequence.ExecuteAsync(
            new LibVlcVideoSurfaceRestoreOperations(_player),
            positionMs,
            restorePaused,
            cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        try
        {
            _input.RequestStop();
            try
            {
                _player.Stop();
            }
            catch
            {
                // 释放路径继续解除所有托管/原生所有权，错误由发起操作的路径报告。
            }

            _player.Hwnd = nint.Zero;
            _player.Media = null;
            Unsubscribe();
            _player.Dispose();
            PlaybackResourceDiagnostics.PlayerDisposed();
            _media.Dispose();
            _input.Dispose();
            PlaybackResourceDiagnostics.InputDisposed();
        }
        finally
        {
            PlaybackResourceDiagnostics.LeaseDisposed();
            Volatile.Write(ref _disposeState, 2);
        }
    }

    private void Subscribe()
    {
        _player.Playing += OnPlaying;
        _player.Paused += OnPaused;
        _player.Stopped += OnStopped;
        _player.EndReached += OnEnded;
        _player.TimeChanged += OnPositionChanged;
        _player.PositionChanged += OnPositionChanged;
        _player.LengthChanged += OnPositionChanged;
        _player.SeekableChanged += OnPositionChanged;
        _player.EncounteredError += OnEncounteredError;
        _input.Failed += OnInputFailed;
    }

    private void Unsubscribe()
    {
        _player.Playing -= OnPlaying;
        _player.Paused -= OnPaused;
        _player.Stopped -= OnStopped;
        _player.EndReached -= OnEnded;
        _player.TimeChanged -= OnPositionChanged;
        _player.PositionChanged -= OnPositionChanged;
        _player.LengthChanged -= OnPositionChanged;
        _player.SeekableChanged -= OnPositionChanged;
        _player.EncounteredError -= OnEncounteredError;
        _input.Failed -= OnInputFailed;
    }

    private void OnPlaying(object? sender, EventArgs e) =>
        StateChanged?.Invoke(this, PlaybackState.Playing);

    private void OnPaused(object? sender, EventArgs e) =>
        StateChanged?.Invoke(this, PlaybackState.Paused);

    private void OnStopped(object? sender, EventArgs e)
    {
        // With LibVLC 3.0.21 and a MediaInput callback source, natural EOF may
        // emit Stopped without EndReached.  The demuxer has nevertheless
        // completed when its reported position is inside the final 500 ms.
        var state = DurationMs > 0 && PositionMs >= Math.Max(0, DurationMs - 500)
            ? PlaybackState.Ended
            : PlaybackState.Stopped;
        StateChanged?.Invoke(this, state);
    }

    private void OnEnded(object? sender, EventArgs e) =>
        StateChanged?.Invoke(this, PlaybackState.Ended);

    private void OnPositionChanged(object? sender, EventArgs e) =>
        PositionChanged?.Invoke(this, EventArgs.Empty);

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        var inputFailure = _input.TryTakeLastFailure(out var captured)
            ? captured
            : null;
        PublishFailure(inputFailure ?? PlaybackFailureMapper.DecodeFailed());
    }

    private void OnInputFailed(PlaybackFailure failure)
    {
        _input.TryTakeLastFailure(out _);
        PublishFailure(failure);
    }

    private void PublishFailure(PlaybackFailure failure)
    {
        Interlocked.CompareExchange(ref _firstFailure, failure, null);
        if (Interlocked.Exchange(ref _failurePublished, 1) == 0)
        {
            Failed?.Invoke(this, _firstFailure!);
        }
    }
}
