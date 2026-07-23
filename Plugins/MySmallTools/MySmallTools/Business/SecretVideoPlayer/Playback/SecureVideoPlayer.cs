using LibVLCSharp.Shared;
using MySmallTools.Business.SecretVideoPlayer.Container;

namespace MySmallTools.Business.SecretVideoPlayer.Playback;

/// <summary>
/// 基于“认证随机读取流”的 SECVID03 安全视频播放器。
/// </summary>
/// <remarks>
/// 播放链路固定为 SECVID03 → <see cref="SeekableEncryptedVideoStream"/> →
/// <see cref="SeekableStreamMediaInput"/> → LibVLC Media → Avalonia VideoView。
/// 该类不持有完整视频明文，只管理当前 Media、MediaInput 及 LibVLC 对象的生命周期。
/// </remarks>
public sealed class SecureVideoPlayer : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Media? _currentMedia;
    private SeekableStreamMediaInput? _mediaInput;
    private int _disposeState;

    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;
    public event EventHandler<TimeChangedEventArgs>? TimeChanged;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    public event EventHandler<LengthChangedEventArgs>? LengthChanged;
    public event EventHandler<SeekableChangedEventArgs>? SeekableChanged;
    public event EventHandler<string>? ErrorOccurred;

    public SecureVideoPlayer(LibVlcRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        // 必须先用插件内的绝对路径初始化 Core，再创建任何 LibVLC/MediaPlayer 实例。
        runtime.EnsureInitialized();
        _libVlc = new LibVLC();
        _player = new MediaPlayer(_libVlc);
        SubscribeToPlayerEvents();
    }

    private void SubscribeToPlayerEvents()
    {
        _player.Playing += (_, _) => PlaybackStateChanged?.Invoke(this, new(PlaybackState.Playing));
        _player.Paused += (_, _) => PlaybackStateChanged?.Invoke(this, new(PlaybackState.Paused));
        _player.Stopped += (_, _) => PlaybackStateChanged?.Invoke(this, new(PlaybackState.Stopped));
        _player.EndReached += (_, _) => PlaybackStateChanged?.Invoke(this, new(PlaybackState.Ended));
        _player.TimeChanged += (_, e) => TimeChanged?.Invoke(this, new(e.Time));
        _player.PositionChanged += (_, e) => PositionChanged?.Invoke(this, new(e.Position));
        _player.LengthChanged += (_, e) => LengthChanged?.Invoke(this, new(e.Length));
        _player.SeekableChanged += (_, e) => SeekableChanged?.Invoke(this, new(e.Seekable != 0));
        _player.EncounteredError += (_, _) =>
        {
            // 原生事件本身不携带托管解密异常，优先转发 MediaInput 保存的认证/读取错误。
            var detail = _mediaInput?.LastError?.Message;
            ErrorOccurred?.Invoke(this, detail is null ? "播放失败。" : $"播放失败: {detail}");
            PlaybackStateChanged?.Invoke(this, new(PlaybackState.Error));
        };
    }

    /// <summary>
    /// 验证 SECVID03 密码并把随机读取媒体绑定到播放器，保留原有公共方法签名。
    /// </summary>
    /// <remarks>
    /// 此处的“加载”只包含 PBKDF2、固定头认证和 LibVLC 媒体解析，不执行完整视频解密。
    /// 因此首帧等待时间和内存占用不会随视频总大小线性增长。
    /// </remarks>
    public async Task<bool> LoadEncryptedVideoAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var diagnostics = PlaybackPerformanceDiagnostics.Begin("media-switch");
        ThrowIfDisposed();
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        var operationToken = operationCancellation.Token;
        if (!File.Exists(filePath))
        {
            ErrorOccurred?.Invoke(this, "文件不存在。");
            return false;
        }

        await _operationGate.WaitAsync(operationToken).ConfigureAwait(false);
        SeekableStreamMediaInput? newInput = null;
        Media? newMedia = null;
        try
        {
            ThrowIfDisposed();

            // 旧媒体的 Stop 一旦开始就必须完整结束；调用方取消只会阻止后续候选媒体提交。
            await Task.Run(() => CleanupCurrentMediaCore(diagnostics)).ConfigureAwait(false);
            operationToken.ThrowIfCancellationRequested();

            (newInput, newMedia) = await Task.Run(() =>
            {
                var stream = SeekableEncryptedVideoStream.Open(filePath, password);
                var input = new SeekableStreamMediaInput(stream);
                try
                {
                    return (input, new Media(_libVlc, input));
                }
                catch
                {
                    input.Dispose();
                    throw;
                }
            }, operationToken).ConfigureAwait(false);
            diagnostics.Mark("open-auth");

            await newMedia
                .Parse(MediaParseOptions.ParseLocal, -1, operationToken)
                .ConfigureAwait(false);
            diagnostics.Mark("parse");
            operationToken.ThrowIfCancellationRequested();

            _player.Media = newMedia;
            _mediaInput = newInput;
            _currentMedia = newMedia;
            newInput = null;
            newMedia = null;
            diagnostics.Mark("attach");
            return true;
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"加载失败: {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                newMedia?.Dispose();
            }
            finally
            {
                try
                {
                    newInput?.Dispose();
                }
                finally
                {
                    _operationGate.Release();
                }
            }
        }
    }

    public async Task<bool> Play(CancellationToken cancellationToken = default)
    {
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        var operationToken = operationCancellation.Token;
        await _operationGate.WaitAsync(operationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_player.Media is null)
            {
                return false;
            }

            _mediaInput?.PrepareForPlayback();
            if (_currentMedia is { IsParsed: false })
            {
                await _currentMedia
                    .Parse(MediaParseOptions.ParseLocal, -1, operationToken)
                    .ConfigureAwait(false);
            }

            return _player.Play();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"播放失败: {ex.Message}");
            return false;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// 当前是否仍有可用于重新创建视频输出的媒体。
    /// </summary>
    public bool HasMedia => Volatile.Read(ref _disposeState) == 0 && _currentMedia is not null;

    /// <summary>
    /// 当前原生播放器位置。未开始播放时 LibVLC 可能返回负值，这里统一归零。
    /// </summary>
    public long PlaybackTime => Volatile.Read(ref _disposeState) != 0 ? 0 : Math.Max(0, _player.Time);

    /// <summary>
    /// 当前是否处于暂停状态。
    /// </summary>
    public bool IsPaused => Volatile.Read(ref _disposeState) == 0 && _player.State == VLCState.Paused;

    /// <summary>
    /// 在 Avalonia 销毁旧 HWND 前同步停止播放器，使旧 vout 完整退出。
    /// </summary>
    /// <remarks>
    /// MediaPlayer.Stop 是同步调用，不能从 LibVLC 回调线程调用。本方法只允许由 Avalonia
    /// NativeControlHost 的表面销毁通知在 UI 线程调用，并且不会解除当前 Media。
    /// </remarks>
    public void StopForVideoSurfaceTransition()
    {
        using var diagnostics = PlaybackPerformanceDiagnostics.Begin("dock-surface-stop");
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        _operationGate.Wait();
        try
        {
            if (Volatile.Read(ref _disposeState) == 0 && _currentMedia is not null)
            {
                StopCore(prepareForReplay: true, diagnostics);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// 在新 HWND 已经绑定后重新创建 vout，并恢复原来的位置和播放/暂停状态。
    /// </summary>
    public async Task<bool> RestoreVideoSurfaceAsync(
        long positionMs,
        bool restorePaused,
        CancellationToken cancellationToken)
    {
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        var operationToken = operationCancellation.Token;
        await _operationGate.WaitAsync(operationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_currentMedia is null)
            {
                return false;
            }

            _mediaInput?.PrepareForPlayback();
            var operations = new LibVlcVideoSurfaceRestoreOperations(_player);
            return await VideoSurfaceRestoreSequence.ExecuteAsync(
                    operations,
                    positionMs,
                    restorePaused,
                    operationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Pause()
    {
        if (Volatile.Read(ref _disposeState) == 0) _player.Pause();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        using var diagnostics = PlaybackPerformanceDiagnostics.Begin("user-stop");
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        await _operationGate
            .WaitAsync(operationCancellation.Token)
            .ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await Task.Run(() => StopCore(prepareForReplay: true, diagnostics)).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void SetPosition(float position)
    {
        if (Volatile.Read(ref _disposeState) == 0 && _player.Media is not null) _player.Position = Math.Clamp(position, 0, 1);
    }

    public void SetTime(long timeMs)
    {
        if (Volatile.Read(ref _disposeState) == 0 && _player.Media is not null) _player.Time = Math.Max(0, timeMs);
    }

    public bool SetVolume(int volume)
    {
        if (Volatile.Read(ref _disposeState) != 0) return false;
        _player.Volume = Math.Clamp(volume, 0, 100);
        return true;
    }

    public VideoInfo? GetVideoInfo()
    {
        if (Volatile.Read(ref _disposeState) != 0 || _player.Media is null) return null;
        return new VideoInfo
        {
            Duration = _player.Length,
            Position = _player.Time,
            Volume = _player.Volume,
            IsSeekable = _player.IsSeekable,
            HasVideo = _player.VideoTrackCount > 0,
            HasAudio = _player.AudioTrackCount > 0,
            VideoTrackCount = _player.VideoTrackCount,
            AudioTrackCount = _player.AudioTrackCount
        };
    }

    public MediaPlayer GetMediaPlayer() => _player;

    public async Task CleanupCurrentMediaAsync(CancellationToken cancellationToken = default)
    {
        using var diagnostics = PlaybackPerformanceDiagnostics.Begin("media-cleanup");
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        await _operationGate
            .WaitAsync(operationCancellation.Token)
            .ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await Task.Run(() => CleanupCurrentMediaCore(diagnostics)).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        using var diagnostics = PlaybackPerformanceDiagnostics.Begin("player-dispose");
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            return;
        }

        _lifetimeCancellation.Cancel();
        _operationGate.Wait();
        try
        {
            try
            {
                CleanupCurrentMediaCore(diagnostics);
            }
            finally
            {
                try
                {
                    _player.Dispose();
                }
                finally
                {
                    // LibVLC 实例由本播放器独占；Core.Initialize 是进程级初始化，但 LibVLC 对象仍必须正常释放。
                    _libVlc.Dispose();
                }
            }
        }
        finally
        {
            Volatile.Write(ref _disposeState, 2);
            _lifetimeCancellation.Dispose();
            _operationGate.Release();
        }
    }

    private void StopCore(
        bool prepareForReplay,
        PlaybackPerformanceDiagnostics? diagnostics = null)
    {
        if (_currentMedia is null)
        {
            diagnostics?.Mark("stop-no-media");
            return;
        }

        var input = _mediaInput;
        input?.RequestStop();
        _player.Stop();
        diagnostics?.Mark("stop");
        if (prepareForReplay)
        {
            input?.PrepareForPlayback();
        }
    }

    private void CleanupCurrentMediaCore(PlaybackPerformanceDiagnostics? diagnostics = null)
    {
        if (_currentMedia is null && _mediaInput is null)
        {
            return;
        }

        // Stop 成功返回后，LibVLC 的读取线程已经退出，才能解除并释放 Media/Input。
        StopCore(prepareForReplay: false, diagnostics);
        _player.Media = null;

        var media = _currentMedia;
        var input = _mediaInput;
        _currentMedia = null;
        _mediaInput = null;

        try
        {
            media?.Dispose();
        }
        finally
        {
            input?.Dispose();
        }
        diagnostics?.Mark("release");
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
}
