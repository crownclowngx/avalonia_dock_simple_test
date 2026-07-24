using LibVLCSharp.Shared;

namespace MySmallTools.Business.SecretVideoPlayer.Playback;

/// <summary>
/// SECVID03 播放应用服务。负责串行化用例、提交候选媒体、拒绝过期请求和维护表面恢复快照。
/// LibVLC 资源的具体释放顺序由 <see cref="PlaybackMediaLease"/> 独占。
/// </summary>
internal sealed class SecureVideoPlayer :
    ISecureVideoPlaybackSession,
    ILibVlcVideoOutputSource
{
    private readonly IPlaybackMediaLeaseFactory _mediaLeaseFactory;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _snapshotSync = new();

    private IPlaybackMediaLease? _currentLease;
    private SurfaceRecoverySnapshot? _pendingSurfaceRecovery;
    private CancellationTokenSource? _surfaceRestoreCancellation;
    private CancellationTokenSource? _mediaSwitchCancellation;
    private VideoSurfaceToken _surface;
    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Empty;
    private long _nextMediaGeneration;
    private long _intentRevision;
    private int _disposeState;

    private readonly record struct SurfaceRecoverySnapshot(
        long MediaGeneration,
        long IntentRevision,
        long PositionMs,
        PlaybackState State);

    public event EventHandler<PlaybackChangedEventArgs>? Changed;
    public event EventHandler? OutputChanged;

    public SecureVideoPlayer(IPlaybackMediaLeaseFactory mediaLeaseFactory)
    {
        _mediaLeaseFactory = mediaLeaseFactory ??
            throw new ArgumentNullException(nameof(mediaLeaseFactory));
    }

    public PlaybackSnapshot Snapshot
    {
        get
        {
            lock (_snapshotSync)
            {
                return _snapshot;
            }
        }
    }

    public MediaPlayer? MediaPlayer =>
        Volatile.Read(ref _disposeState) == 0 ? _currentLease?.NativePlayer : null;

    public async Task<PlaybackOperationResult> LoadAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var diagnostics = PlaybackPerformanceDiagnostics.Begin("media-switch");
        var intent = BeginUserIntent();
        var requestCancellation = CreateOperationCancellation(cancellationToken);
        var previousRequest = Interlocked.Exchange(
            ref _mediaSwitchCancellation,
            requestCancellation);
        TryCancel(previousRequest);
        var token = requestCancellation.Token;

        await _operationGate.WaitAsync().ConfigureAwait(false);
        IPlaybackMediaLease? candidate = null;
        try
        {
            token.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            PublishCurrent(isTransitioning: true);

            var generation = Interlocked.Increment(ref _nextMediaGeneration);
            // Opening SECVID03 performs the intentionally expensive PBKDF2
            // before LibVLC reaches its asynchronous parse API. Keep that work
            // off the UI thread and yield early enough for a newer Load request
            // to cancel this candidate before it can commit.
            candidate = await Task.Run(
                    () => _mediaLeaseFactory.CreateAsync(
                        generation,
                        filePath,
                        password,
                        token),
                    token)
                .ConfigureAwait(false);
            diagnostics.Mark("open-auth-parse");

            token.ThrowIfCancellationRequested();
            if (intent != Volatile.Read(ref _intentRevision))
            {
                return PlaybackOperationResult.Failed(
                    new PlaybackFailure(PlaybackFailureCode.Cancelled, "操作已被更新的用户请求取代。"));
            }

            var previous = _currentLease;
            if (previous is not null)
            {
                Unsubscribe(previous);
                previous.SetVideoOutputHandle(nint.Zero);
                previous.Stop();
            }

            _currentLease = candidate;
            candidate = null;
            Subscribe(_currentLease);

            if (_surface.IsValid)
            {
                _currentLease.SetVideoOutputHandle(_surface.Handle);
            }

            _pendingSurfaceRecovery = null;
            OutputChanged?.Invoke(this, EventArgs.Empty);
            Publish(_currentLease, PlaybackState.Ready, isTransitioning: false);
            diagnostics.Mark("commit");

            previous?.Dispose();
            diagnostics.Mark("release-old");
            return PlaybackOperationResult.Succeeded();
        }
        catch (PlaybackOperationException ex)
        {
            if (intent == Volatile.Read(ref _intentRevision))
            {
                PublishCurrent(isTransitioning: false, ex.Failure);
            }
            return PlaybackOperationResult.Failed(ex.Failure);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            var failure = new PlaybackFailure(PlaybackFailureCode.Cancelled, "操作已取消。");
            if (intent == Volatile.Read(ref _intentRevision))
            {
                PublishCurrent(isTransitioning: false, failure);
            }
            return PlaybackOperationResult.Failed(failure);
        }
        catch (Exception ex)
        {
            var failure = PlaybackFailureMapper.MapLoad(ex);
            if (intent == Volatile.Read(ref _intentRevision))
            {
                PublishCurrent(isTransitioning: false, failure);
            }
            return PlaybackOperationResult.Failed(failure);
        }
        finally
        {
            candidate?.Dispose();
            _operationGate.Release();
            Interlocked.CompareExchange(
                ref _mediaSwitchCancellation,
                null,
                requestCancellation);
            requestCancellation.Dispose();
        }
    }

    public async Task<PlaybackOperationResult> PlayAsync(
        CancellationToken cancellationToken = default)
    {
        BeginUserIntent();
        using var linked = CreateOperationCancellation(cancellationToken);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_currentLease is null)
            {
                return Fail(
                    PlaybackFailureCode.InvalidRequest,
                    "请先加载视频。",
                    publish: true);
            }

            if (!_surface.IsValid)
            {
                return Fail(
                    PlaybackFailureCode.SurfaceRestoreFailed,
                    "视频输出表面尚未准备完成。",
                    publish: true);
            }

            if (!_currentLease.Play())
            {
                return Fail(
                    PlaybackFailureCode.DecodeFailed,
                    "LibVLC 无法开始播放该媒体。",
                    publish: true);
            }

            return PlaybackOperationResult.Succeeded();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (Exception ex)
        {
            var failure = PlaybackFailureMapper.MapMediaInput(ex);
            PublishCurrent(false, failure, PlaybackState.Faulted);
            return PlaybackOperationResult.Failed(failure);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PlaybackOperationResult> PauseAsync(
        CancellationToken cancellationToken = default)
    {
        BeginUserIntent();
        using var linked = CreateOperationCancellation(cancellationToken);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_currentLease is null)
            {
                return Fail(
                    PlaybackFailureCode.InvalidRequest,
                    "当前没有可暂停的媒体。",
                    publish: false);
            }

            _currentLease.SetPause(true);
            Publish(_currentLease, PlaybackState.Paused, false);
            return PlaybackOperationResult.Succeeded();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PlaybackOperationResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        using var diagnostics = PlaybackPerformanceDiagnostics.Begin("user-stop");
        BeginUserIntent();
        using var linked = CreateOperationCancellation(cancellationToken);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_currentLease is null)
            {
                return PlaybackOperationResult.Succeeded();
            }

            await Task.Run(_currentLease.Stop).ConfigureAwait(false);
            diagnostics.Mark("stop");
            Publish(_currentLease, PlaybackState.Stopped, false);
            return PlaybackOperationResult.Succeeded();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PlaybackOperationResult> SeekAsync(
        long positionMs,
        bool waitForFrame = false,
        CancellationToken cancellationToken = default)
    {
        BeginUserIntent();
        using var linked = CreateOperationCancellation(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(
            linked.Token,
            timeout.Token);
        await _operationGate.WaitAsync(bounded.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_currentLease is null || !_currentLease.IsSeekable)
            {
                return Fail(
                    PlaybackFailureCode.InvalidRequest,
                    "当前媒体不支持随机定位。",
                    publish: false);
            }

            var maximum = Math.Max(0, _currentLease.DurationMs - 250);
            var target = Math.Clamp(positionMs, 0, maximum);
            await _currentLease
                .SeekAsync(target, waitForFrame, bounded.Token)
                .ConfigureAwait(false);
            PublishCurrent(false);
            return PlaybackOperationResult.Succeeded();
        }
        catch (PlaybackOperationException ex)
        {
            PublishCurrent(false, ex.Failure, PlaybackState.Faulted);
            return PlaybackOperationResult.Failed(ex.Failure);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            var failure = new PlaybackFailure(
                PlaybackFailureCode.DecodeFailed,
                "The media seek did not complete within the allowed time.");
            PublishCurrent(false, failure, PlaybackState.Faulted);
            return PlaybackOperationResult.Failed(failure);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (Exception ex)
        {
            var failure = PlaybackFailureMapper.MapMediaInput(ex);
            PublishCurrent(false, failure, PlaybackState.Faulted);
            return PlaybackOperationResult.Failed(failure);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PlaybackOperationResult> ReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        BeginUserIntent();
        using var linked = CreateOperationCancellation(cancellationToken);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ReleaseCurrentCore();
            PublishEmpty();
            return PlaybackOperationResult.Succeeded();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public bool SetVolume(int volume)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return false;
        }

        var lease = _currentLease;
        if (lease is null)
        {
            lock (_snapshotSync)
            {
                _snapshot = _snapshot with { Volume = Math.Clamp(volume, 0, 100) };
            }
            return true;
        }

        lease.SetVolume(volume);
        PublishCurrent(false);
        return true;
    }

    public void DetachSurface(VideoSurfaceToken surface)
    {
        if (Volatile.Read(ref _disposeState) != 0 ||
            !surface.IsValid ||
            surface != _surface)
        {
            return;
        }

        CancelSurfaceRestore();
        _operationGate.Wait();
        try
        {
            if (Volatile.Read(ref _disposeState) != 0 || surface != _surface)
            {
                return;
            }

            var lease = _currentLease;
            if (lease is not null &&
                Snapshot.State is PlaybackState.Playing or PlaybackState.Paused)
            {
                _pendingSurfaceRecovery = new SurfaceRecoverySnapshot(
                    lease.Generation,
                    Volatile.Read(ref _intentRevision),
                    lease.PositionMs,
                    Snapshot.State);
                lease.Stop();
            }
            else
            {
                _pendingSurfaceRecovery = null;
            }

            if (lease is not null)
            {
                lease.SetVideoOutputHandle(nint.Zero);
            }

            _surface = default;
            PublishCurrent(_pendingSurfaceRecovery is not null);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PlaybackOperationResult> AttachAndRestoreSurfaceAsync(
        VideoSurfaceToken surface,
        CancellationToken cancellationToken = default)
    {
        if (!surface.IsValid)
        {
            return PlaybackOperationResult.Failed(
                new PlaybackFailure(PlaybackFailureCode.InvalidRequest, "视频输出句柄无效。"));
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token,
            _lifetimeCancellation.Token);
        var restoreCancellation = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        var previous = Interlocked.Exchange(ref _surfaceRestoreCancellation, restoreCancellation);
        TryCancelAndDispose(previous);

        await _operationGate.WaitAsync(restoreCancellation.Token).ConfigureAwait(false);
        var recoveryStarted = false;
        try
        {
            ThrowIfDisposed();
            _surface = surface;
            var lease = _currentLease;
            if (lease is null)
            {
                PublishEmpty(surface.Generation);
                return PlaybackOperationResult.Succeeded();
            }

            lease.SetVideoOutputHandle(surface.Handle);
            OutputChanged?.Invoke(this, EventArgs.Empty);

            var recovery = _pendingSurfaceRecovery;
            _pendingSurfaceRecovery = null;
            if (recovery is null ||
                recovery.Value.MediaGeneration != lease.Generation ||
                recovery.Value.IntentRevision != Volatile.Read(ref _intentRevision))
            {
                PublishCurrent(false);
                return PlaybackOperationResult.Succeeded();
            }

            PlaybackResourceDiagnostics.SurfaceRestoreStarted();
            recoveryStarted = true;
            var restored = await lease.RestoreSurfaceAsync(
                    recovery.Value.PositionMs,
                    recovery.Value.State == PlaybackState.Paused,
                    restoreCancellation.Token)
                .ConfigureAwait(false);
            if (!restored)
            {
                var failure = PlaybackFailureMapper.SurfaceRestoreFailed();
                Publish(lease, PlaybackState.Stopped, false, failure);
                return PlaybackOperationResult.Failed(failure);
            }

            var restoredState = recovery.Value.State == PlaybackState.Paused
                ? PlaybackState.Paused
                : PlaybackState.Playing;
            Publish(lease, restoredState, false);
            return PlaybackOperationResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            if (timeout.IsCancellationRequested)
            {
                var failure = PlaybackFailureMapper.SurfaceRestoreFailed();
                PublishCurrent(false, failure, PlaybackState.Stopped);
                return PlaybackOperationResult.Failed(failure);
            }

            return Cancelled();
        }
        catch (Exception)
        {
            var failure = PlaybackFailureMapper.SurfaceRestoreFailed();
            PublishCurrent(false, failure, PlaybackState.Stopped);
            return PlaybackOperationResult.Failed(failure);
        }
        finally
        {
            if (recoveryStarted)
            {
                PlaybackResourceDiagnostics.SurfaceRestoreFinished();
            }
            _operationGate.Release();
            if (Interlocked.CompareExchange(
                    ref _surfaceRestoreCancellation,
                    null,
                    restoreCancellation) == restoreCancellation)
            {
                restoreCancellation.Dispose();
            }
        }
    }

    public static PlaybackResourceSnapshot CaptureResourceSnapshot() =>
        SecurePlaybackDiagnostics.CaptureResources();

    public void Dispose()
    {
        using var diagnostics = PlaybackPerformanceDiagnostics.Begin("player-dispose");
        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
        {
            return;
        }

        _lifetimeCancellation.Cancel();
        TryCancel(Interlocked.Exchange(ref _mediaSwitchCancellation, null));
        CancelSurfaceRestore();
        _operationGate.Wait();
        try
        {
            ReleaseCurrentCore();
            lock (_snapshotSync)
            {
                _snapshot = PlaybackSnapshot.Empty with { State = PlaybackState.Disposed };
            }
        }
        finally
        {
            Volatile.Write(ref _disposeState, 2);
            _lifetimeCancellation.Dispose();
            _operationGate.Release();
        }
    }

    private long BeginUserIntent()
    {
        ThrowIfDisposed();
        _pendingSurfaceRecovery = null;
        CancelSurfaceRestore();
        return Interlocked.Increment(ref _intentRevision);
    }

    private CancellationTokenSource CreateOperationCancellation(CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);

    private void Subscribe(IPlaybackMediaLease lease)
    {
        lease.StateChanged += OnLeaseStateChanged;
        lease.PositionChanged += OnLeasePositionChanged;
        lease.Failed += OnLeaseFailed;
    }

    private void Unsubscribe(IPlaybackMediaLease lease)
    {
        lease.StateChanged -= OnLeaseStateChanged;
        lease.PositionChanged -= OnLeasePositionChanged;
        lease.Failed -= OnLeaseFailed;
    }

    private void OnLeaseStateChanged(IPlaybackMediaLease lease, PlaybackState state)
    {
        if (!ReferenceEquals(_currentLease, lease))
        {
            return;
        }

        // Surface transition Stop is an implementation detail; the user-visible state is restored later.
        if (state == PlaybackState.Stopped &&
            _pendingSurfaceRecovery is { MediaGeneration: var generation } &&
            generation == lease.Generation)
        {
            return;
        }

        Publish(lease, state, false);
    }

    private void OnLeasePositionChanged(object? sender, EventArgs e)
    {
        if (sender is IPlaybackMediaLease lease && ReferenceEquals(_currentLease, lease))
        {
            Publish(lease, Snapshot.State, Snapshot.IsTransitioning);
        }
    }

    private void OnLeaseFailed(IPlaybackMediaLease lease, PlaybackFailure failure)
    {
        if (!ReferenceEquals(_currentLease, lease))
        {
            return;
        }

        try
        {
            lease.Stop();
        }
        catch
        {
            // 根因 failure 已经保存，不用停止异常覆盖认证/读取失败。
        }

        Publish(lease, PlaybackState.Faulted, false, failure);
    }

    private void Publish(
        IPlaybackMediaLease lease,
        PlaybackState state,
        bool isTransitioning,
        PlaybackFailure? failure = null)
    {
        if (!ReferenceEquals(_currentLease, lease) ||
            Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        var snapshot = new PlaybackSnapshot(
            lease.Generation,
            state,
            isTransitioning,
            lease.PositionMs,
            lease.DurationMs,
            lease.IsSeekable,
            true,
            _surface.Generation,
            lease.Volume,
            lease.HasVideo,
            lease.HasAudio,
            lease.VideoTrackCount,
            lease.AudioTrackCount);
        lock (_snapshotSync)
        {
            _snapshot = snapshot;
        }

        Changed?.Invoke(this, new PlaybackChangedEventArgs(snapshot, failure));
    }

    private void PublishCurrent(
        bool isTransitioning,
        PlaybackFailure? failure = null,
        PlaybackState? state = null)
    {
        var lease = _currentLease;
        if (lease is null)
        {
            var snapshot = Snapshot with
            {
                State = state ?? PlaybackState.Empty,
                IsTransitioning = isTransitioning,
                SurfaceGeneration = _surface.Generation
            };
            lock (_snapshotSync)
            {
                _snapshot = snapshot;
            }
            Changed?.Invoke(this, new PlaybackChangedEventArgs(snapshot, failure));
            return;
        }

        Publish(lease, state ?? Snapshot.State, isTransitioning, failure);
    }

    private void PublishEmpty(long surfaceGeneration = 0)
    {
        var snapshot = PlaybackSnapshot.Empty with
        {
            SurfaceGeneration = surfaceGeneration,
            Volume = Snapshot.Volume
        };
        lock (_snapshotSync)
        {
            _snapshot = snapshot;
        }
        Changed?.Invoke(this, new PlaybackChangedEventArgs(snapshot));
    }

    private PlaybackOperationResult Fail(
        PlaybackFailureCode code,
        string message,
        bool publish)
    {
        var failure = new PlaybackFailure(code, message);
        if (publish)
        {
            PublishCurrent(false, failure, PlaybackState.Faulted);
        }
        return PlaybackOperationResult.Failed(failure);
    }

    private static PlaybackOperationResult Cancelled() =>
        PlaybackOperationResult.Failed(
            new PlaybackFailure(PlaybackFailureCode.Cancelled, "操作已取消。"));

    private void ReleaseCurrentCore()
    {
        var lease = _currentLease;
        _currentLease = null;
        _pendingSurfaceRecovery = null;
        if (lease is null)
        {
            return;
        }

        Unsubscribe(lease);
        lease.SetVideoOutputHandle(nint.Zero);
        OutputChanged?.Invoke(this, EventArgs.Empty);
        lease.Dispose();
    }

    private void CancelSurfaceRestore()
    {
        var cancellation = Interlocked.Exchange(ref _surfaceRestoreCancellation, null);
        TryCancelAndDispose(cancellation);
    }

    private static void TryCancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
}
