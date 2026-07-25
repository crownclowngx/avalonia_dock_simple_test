using LibVLCSharp.Shared;

namespace MySmallTools.Business.SecretVideoPlayer.Playback;

/// <summary>
/// SECVID03 播放应用服务。该类型只负责编排用户意图、候选媒体和 UI 快照，
/// LibVLC 原生播放器由 <see cref="IPlaybackPlayerHost"/> 长期持有，
/// 每个视频独占的文件、密钥和缓存则由 <see cref="IPlaybackMediaSource"/> 持有。
/// </summary>
/// <remarks>
/// G3.1 的核心约束是：普通播放命令不得在 Avalonia UI 线程执行可能阻塞的
/// LibVLC 操作。所有 MediaPlayer 控制都提交给单消费者原生调度器。
/// Dock 销毁 HWND 前的 Stop 是唯一保留的同步安全屏障，因为异步返回会让
/// 原生 vout 在窗口销毁后继续访问失效句柄。
/// </remarks>
internal sealed class SecureVideoPlayer :
    ISecureVideoPlaybackSession,
    ILibVlcVideoOutputSource
{
    private static readonly float[] SupportedRates = [0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f];

    private readonly IPlaybackPlayerHost _playerHost;
    private readonly IPlaybackMediaSourceFactory _mediaSourceFactory;
    private readonly IPlaybackNativeDispatcher _nativeDispatcher;
    private readonly IPlaybackResourceReaper _resourceReaper;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _snapshotSync = new();

    private IPlaybackMediaSource? _currentSource;
    private SurfaceRecoverySnapshot? _pendingSurfaceRecovery;
    private CancellationTokenSource? _surfaceRestoreCancellation;
    private CancellationTokenSource? _mediaSwitchCancellation;
    private VideoSurfaceToken _surface;
    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Empty;
    private PlaybackControlSnapshot _controls = PlaybackControlSnapshot.Empty;
    private float _desiredRate = 1.0f;
    private long _playingControlsRefreshGeneration;
    private long _nextMediaGeneration;
    private long _intentRevision;
    private int _disposeState;

    private readonly record struct SurfaceRecoverySnapshot(
        long MediaGeneration,
        long IntentRevision,
        long PositionMs,
        PlaybackState State,
        float Rate,
        int? AudioTrackId,
        int? SubtitleTrackId);

    public SecureVideoPlayer(
        IPlaybackPlayerHost playerHost,
        IPlaybackMediaSourceFactory mediaSourceFactory,
        IPlaybackNativeDispatcher nativeDispatcher,
        IPlaybackResourceReaper resourceReaper)
    {
        _playerHost = playerHost ?? throw new ArgumentNullException(nameof(playerHost));
        _mediaSourceFactory = mediaSourceFactory ??
                              throw new ArgumentNullException(nameof(mediaSourceFactory));
        _nativeDispatcher = nativeDispatcher ??
                            throw new ArgumentNullException(nameof(nativeDispatcher));
        _resourceReaper = resourceReaper ??
                          throw new ArgumentNullException(nameof(resourceReaper));

        _playerHost.StateChanged += OnHostStateChanged;
        _playerHost.PositionChanged += OnHostPositionChanged;
        _playerHost.Failed += OnHostFailed;
        if (_playerHost is LazyPlaybackBackend lazyBackend)
        {
            lazyBackend.Created += OnBackendCreated;
        }
        _playerHost.SetVolume(50);
    }

    public event EventHandler<PlaybackChangedEventArgs>? Changed;

    /// <summary>
    /// 单播放器架构下该事件不会因媒体切换触发。保留事件是为了维持原有输出端口兼容，
    /// 只有未来真正替换 Document 级 PlayerHost 时才需要通知 View。
    /// </summary>
    // G3.1 后 PlayerHost 在 Document 生命周期内不再变化。保留该事件只是为了兼容
    // ILibVlcVideoOutputSource 的既有契约；普通换片不会再通知 View 重绑 MediaPlayer/HWND。
    public event EventHandler? OutputChanged;

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

    /// <summary>
    /// Document 生命周期内返回同一个 MediaPlayer，使 VideoView 和 HWND 不必随视频切换重绑。
    /// </summary>
    public MediaPlayer? MediaPlayer =>
        Volatile.Read(ref _disposeState) == 0 ? _playerHost.NativePlayer : null;

    public Task<PlaybackOperationResult> LoadAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default) =>
        SwitchMediaAsync(filePath, password, startPlayback: false, cancellationToken);

    public Task<PlaybackOperationResult> LoadAndPlayAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default) =>
        SwitchMediaAsync(filePath, password, startPlayback: true, cancellationToken);

    private async Task<PlaybackOperationResult> SwitchMediaAsync(
        string filePath,
        string password,
        bool startPlayback,
        CancellationToken cancellationToken)
    {
        using var diagnostics = PlaybackPerformanceDiagnostics.Begin(
            startPlayback ? "media-switch-and-play" : "media-switch");
        var intent = BeginUserIntent(cancelPendingLoad: false);
        var requestCancellation = CreateOperationCancellation(cancellationToken);
        var previousRequest = Interlocked.Exchange(
            ref _mediaSwitchCancellation,
            requestCancellation);
        TryCancel(previousRequest);
        var token = requestCancellation.Token;
        IPlaybackMediaSource? candidate = null;

        PublishActivity(PlaybackActivity.PreparingCandidate);
        try
        {
            var generation = Interlocked.Increment(ref _nextMediaGeneration);

            // 惰性 backend 只把构造推迟到首次用户加载，不把构造推入 Task.Run。
            // 这样仍沿用 G3 已验证的 UI/STA 构造线程；真正昂贵的 PBKDF2、容器打开
            // 和 Media.Parse 随后进入后台，而所有控制命令继续由 NativeDispatcher 串行化。
            if (_playerHost is LazyPlaybackBackend lazyBackend)
            {
                token.ThrowIfCancellationRequested();
                lazyBackend.EnsureCreatedForPlayback();
            }

            // Open 会同步执行 PBKDF2，必须连同 Parse 一起移出 UI 线程。
            // 候选阶段不占用播放器操作门，因此旧视频可以继续播放，新 Load 也能取消本候选。
            candidate = await Task.Run(
                    () => _mediaSourceFactory.CreateAsync(
                        generation,
                        filePath,
                        password,
                        token),
                    token)
                .ConfigureAwait(false);
            diagnostics.Mark("prepare-candidate");

            token.ThrowIfCancellationRequested();
            if (intent != Volatile.Read(ref _intentRevision))
            {
                return Cancelled();
            }

            await _operationGate.WaitAsync(token).ConfigureAwait(false);
            var operationGateHeld = true;
            try
            {
                token.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                if (intent != Volatile.Read(ref _intentRevision))
                {
                    return Cancelled();
                }

                var oldSource = _currentSource;
                PublishActivity(PlaybackActivity.StoppingCurrent);

                // Stop、Media setter 和 Attach 都可能进入原生等待，统一在后台消费者中执行。
                // 旧 Source 此时仍由会话持有，若 Attach 失败可重新挂回，避免提前破坏当前媒体。
                await _nativeDispatcher.InvokeAsync(
                        "commit-media",
                        () =>
                        {
                            if (oldSource is not null)
                            {
                                oldSource.RequestStop();
                                _playerHost.Stop();
                            }

                            _playerHost.Detach();
                            // Stop 与 Attach 属于同一原生事务，但 UI 阶段仍需精确可见。
                            // 事件允许从后台发布，ViewModel 会统一 marshal 回 UI Dispatcher。
                            PublishActivity(PlaybackActivity.AttachingCandidate);
                            try
                            {
                                _playerHost.Attach(candidate!);
                            }
                            catch
                            {
                                if (oldSource is not null)
                                {
                                    _playerHost.Attach(oldSource);
                                }
                                throw;
                            }
                        },
                        token)
                    .ConfigureAwait(false);
                diagnostics.Mark("stop-detach-attach");

                var committed = candidate;
                candidate = null;
                _currentSource = committed;
                committed.Failed += OnSourceFailed;

                // 轨道 ID 只属于当前媒体代次，因此提交新媒体后立即丢弃旧集合。
                // 倍速则是当前 Document 的非敏感偏好，需要在新媒体启动前重新应用。
                var controlFailure = await InitializeControlsForCurrentMediaAsync(
                        committed,
                        token)
                    .ConfigureAwait(false);
                PublishCurrent(
                    PlaybackState.Ready,
                    PlaybackActivity.AttachingCandidate,
                    controlFailure);

                if (startPlayback)
                {
                    PublishActivity(PlaybackActivity.StartingPlayback);
                    bool started;
                    try
                    {
                        started = await _nativeDispatcher.InvokeAsync(
                                "start-new-media",
                                () =>
                                {
                                    committed.PrepareForPlayback();
                                    return _playerHost.Play();
                                },
                                token)
                            .ConfigureAwait(false);
                        diagnostics.Mark("start-playback");
                    }
                    catch
                    {
                        // Media setter 已提交后，candidate 就已成为播放器当前媒体。
                        // 因此 Play 的同步异常或取消都必须执行补偿事务，否则 oldSource
                        // 会失去所有权并保持文件句柄，candidate 也会错误地留在当前会话。
                        await RollBackFailedStartAsync(oldSource, committed)
                            .ConfigureAwait(false);
                        throw;
                    }

                    if (!started)
                    {
                        await RollBackFailedStartAsync(oldSource, committed)
                            .ConfigureAwait(false);
                        return Fail(
                            PlaybackFailureCode.DecodeFailed,
                            "媒体解码器未能启动播放。",
                            publish: true);
                    }

                    // 部分容器只有解码真正启动后才报告完整轨道，因此启动后再次刷新。
                    await RefreshControlsForCurrentMediaAsync(committed, token)
                        .ConfigureAwait(false);
                    PublishCurrent(
                        PlaybackState.Playing,
                        PlaybackActivity.Idle,
                        controlFailure);
                }
                else
                {
                    PublishCurrent(
                        PlaybackState.Ready,
                        PlaybackActivity.Idle,
                        controlFailure);
                }

                if (oldSource is not null)
                {
                    oldSource.Failed -= OnSourceFailed;
                    PublishActivity(PlaybackActivity.ReleasingOldMedia);

                    // 回收队列容量只有 1。队列满时 EnqueueAsync 会形成背压，但此时新媒体
                    // 已经提交成功，不应继续占着播放器操作门；先释放后，Play/Pause/Seek
                    // 仍可控制新媒体，只有本次切换调用自身等待回收器接管旧 Source。
                    _operationGate.Release();
                    operationGateHeld = false;
                    await _resourceReaper.EnqueueAsync(
                            oldSource,
                            waitForCompletion: false,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    if (intent == Volatile.Read(ref _intentRevision) &&
                        Snapshot.Activity == PlaybackActivity.ReleasingOldMedia)
                    {
                        PublishActivity(PlaybackActivity.Idle);
                    }
                }

                return PlaybackOperationResult.Succeeded();
            }
            finally
            {
                if (operationGateHeld)
                {
                    _operationGate.Release();
                }
            }
        }
        catch (PlaybackOperationException ex)
        {
            if (intent == Volatile.Read(ref _intentRevision))
            {
                PublishFailure(ex.Failure);
            }
            return PlaybackOperationResult.Failed(ex.Failure);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            var failure = new PlaybackFailure(
                PlaybackFailureCode.Cancelled,
                "操作已取消。");
            if (intent == Volatile.Read(ref _intentRevision))
            {
                PublishFailure(failure);
            }
            return PlaybackOperationResult.Failed(failure);
        }
        catch (Exception ex)
        {
            var failure = PlaybackFailureMapper.MapLoad(ex);
            if (intent == Volatile.Read(ref _intentRevision))
            {
                PublishFailure(failure);
            }
            return PlaybackOperationResult.Failed(failure);
        }
        finally
        {
            if (candidate is not null)
            {
                await ReapCandidateSafelyAsync(candidate).ConfigureAwait(false);
            }

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
        BeginUserIntent(cancelPendingLoad: true);
        using var linked = CreateOperationCancellation(cancellationToken);
        PublishWaitingIfBusy();
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var source = _currentSource;
            if (source is null)
            {
                return Fail(
                    PlaybackFailureCode.InvalidRequest,
                    "请先加载视频。",
                    publish: true);
            }

            var started = await _nativeDispatcher.InvokeAsync(
                    "play",
                    () =>
                    {
                        source.PrepareForPlayback();
                        return _playerHost.Play();
                    },
                    linked.Token)
                .ConfigureAwait(false);
            if (!started)
            {
                return Fail(
                    PlaybackFailureCode.DecodeFailed,
                    "媒体解码器未能启动播放。",
                    publish: true);
            }

            await RefreshControlsForCurrentMediaAsync(source, linked.Token)
                .ConfigureAwait(false);
            PublishCurrent(PlaybackState.Playing, PlaybackActivity.Idle);
            return PlaybackOperationResult.Succeeded();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (Exception ex)
        {
            var failure = PlaybackFailureMapper.MapMediaInput(ex);
            PublishCurrent(PlaybackState.Faulted, PlaybackActivity.Idle, failure);
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
        BeginUserIntent(cancelPendingLoad: true);
        using var linked = CreateOperationCancellation(cancellationToken);
        PublishWaitingIfBusy();
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_currentSource is null)
            {
                return PlaybackOperationResult.Succeeded();
            }

            await _nativeDispatcher.InvokeAsync(
                    "pause",
                    () => _playerHost.SetPause(true),
                    linked.Token)
                .ConfigureAwait(false);
            PublishCurrent(PlaybackState.Paused, PlaybackActivity.Idle);
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
        BeginUserIntent(cancelPendingLoad: true);
        using var linked = CreateOperationCancellation(cancellationToken);

        // 先发布活动状态，让 UI 立即停止计时并显示反馈；真正的 Pause/Stop 随后在后台执行。
        PublishActivity(PlaybackActivity.Stopping);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var source = _currentSource;
            if (source is null)
            {
                PublishEmpty(_surface.Generation);
                return PlaybackOperationResult.Succeeded();
            }

            await _nativeDispatcher.InvokeAsync(
                    "stop",
                    () =>
                    {
                        // Pause 让用户尽快看到静止画面，但它不释放 vout，不能替代后续 Stop。
                        _playerHost.SetPause(true);
                        source.RequestStop();
                        _playerHost.Stop();
                    },
                    linked.Token)
                .ConfigureAwait(false);
            diagnostics.Mark("pause-stop");
            PublishCurrent(PlaybackState.Stopped, PlaybackActivity.Idle);
            return PlaybackOperationResult.Succeeded();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (Exception ex)
        {
            var failure = PlaybackFailureMapper.MapMediaInput(ex);
            PublishCurrent(PlaybackState.Faulted, PlaybackActivity.Idle, failure);
            return PlaybackOperationResult.Failed(failure);
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
        BeginUserIntent(cancelPendingLoad: true);
        using var linked = CreateOperationCancellation(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(
            linked.Token,
            timeout.Token);
        PublishWaitingIfBusy();
        await _operationGate.WaitAsync(bounded.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_currentSource is null || !_playerHost.IsSeekable)
            {
                return Fail(
                    PlaybackFailureCode.InvalidRequest,
                    "当前媒体不支持随机定位。",
                    publish: false);
            }

            var maximum = Math.Max(0, _playerHost.DurationMs - 250);
            var target = Math.Clamp(positionMs, 0, maximum);
            await _nativeDispatcher.InvokeAsync(
                    "seek",
                    async token =>
                    {
                        await _playerHost.SeekAsync(target, waitForFrame, token)
                            .ConfigureAwait(false);
                        return true;
                    },
                    bounded.Token)
                .ConfigureAwait(false);
            PublishCurrent(Snapshot.State, PlaybackActivity.Idle);
            return PlaybackOperationResult.Succeeded();
        }
        catch (PlaybackOperationException ex)
        {
            PublishCurrent(PlaybackState.Faulted, PlaybackActivity.Idle, ex.Failure);
            return PlaybackOperationResult.Failed(ex.Failure);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            var failure = new PlaybackFailure(
                PlaybackFailureCode.DecodeFailed,
                "媒体定位未能在允许时间内完成。");
            PublishCurrent(PlaybackState.Faulted, PlaybackActivity.Idle, failure);
            return PlaybackOperationResult.Failed(failure);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Cancelled();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PlaybackOperationResult> SeekRelativeAsync(
        long deltaMs,
        CancellationToken cancellationToken = default)
    {
        BeginUserIntent(cancelPendingLoad: true);
        using var linked = CreateOperationCancellation(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(
            linked.Token,
            timeout.Token);
        PublishWaitingIfBusy();
        await _operationGate.WaitAsync(bounded.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_currentSource is null || !_playerHost.IsSeekable)
            {
                return ControlFailure(
                    PlaybackFailureCode.InvalidRequest,
                    "当前媒体不支持随机定位。",
                    publish: false);
            }

            // 必须在操作门内读取当前位置。若快捷键连按，每一条命令会看到上一条已经
            // 完成后的实际位置，而不会反复基于 UI 中同一份旧快照计算目标。
            var maximum = Math.Max(0, _playerHost.DurationMs - 250);
            var target = Math.Clamp(
                SaturatingAdd(_playerHost.PositionMs, deltaMs),
                0,
                maximum);
            var waitForFrame = Snapshot.State == PlaybackState.Paused;
            await _nativeDispatcher.InvokeAsync(
                    "seek-relative",
                    async token =>
                    {
                        await _playerHost.SeekAsync(target, waitForFrame, token)
                            .ConfigureAwait(false);
                        return true;
                    },
                    bounded.Token)
                .ConfigureAwait(false);
            PublishCurrent(Snapshot.State, PlaybackActivity.Idle);
            return PlaybackOperationResult.Succeeded();
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return ControlFailure(
                PlaybackFailureCode.DecodeFailed,
                "媒体定位未能在允许时间内完成。",
                publish: true);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (Exception ex)
        {
            var failure = PlaybackFailureMapper.MapMediaInput(ex);
            PublishCurrent(PlaybackState.Faulted, PlaybackActivity.Idle, failure);
            return PlaybackOperationResult.Failed(failure);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PlaybackOperationResult> SetRateAsync(
        float rate,
        CancellationToken cancellationToken = default)
    {
        if (!SupportedRates.Any(candidate => Math.Abs(candidate - rate) < 0.0001f))
        {
            return ControlFailure(
                PlaybackFailureCode.InvalidRequest,
                "请选择播放器支持的倍速。",
                publish: false);
        }

        BeginUserIntent(cancelPendingLoad: true);
        using var linked = CreateOperationCancellation(cancellationToken);
        PublishWaitingIfBusy();
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_currentSource is null)
            {
                return ControlFailure(
                    PlaybackFailureCode.InvalidRequest,
                    "请先加载视频。",
                    publish: false);
            }

            var changed = await _nativeDispatcher.InvokeAsync(
                    "set-rate",
                    () => _playerHost.SetRate(rate),
                    linked.Token)
                .ConfigureAwait(false);
            if (!changed)
            {
                // 失败时不把用户请求保存为 Document 偏好，否则之后每次换片都会重复失败。
                // 当前媒体继续保持原速度和可播放状态。
                return ControlFailure(
                    PlaybackFailureCode.ControlUnavailable,
                    "当前媒体无法使用所选倍速。",
                    publish: true);
            }

            _desiredRate = rate;
            UpdateControls(_controls with { Rate = rate });
            PublishCurrent(Snapshot.State, PlaybackActivity.Idle);
            return PlaybackOperationResult.Succeeded();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch
        {
            return ControlFailure(
                PlaybackFailureCode.ControlUnavailable,
                "当前媒体无法使用所选倍速。",
                publish: true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task<PlaybackOperationResult> SelectAudioTrackAsync(
        int trackId,
        CancellationToken cancellationToken = default) =>
        SelectTrackAsync(trackId, subtitle: false, cancellationToken);

    public Task<PlaybackOperationResult> SelectSubtitleTrackAsync(
        int trackId,
        CancellationToken cancellationToken = default) =>
        SelectTrackAsync(trackId, subtitle: true, cancellationToken);

    private async Task<PlaybackOperationResult> SelectTrackAsync(
        int trackId,
        bool subtitle,
        CancellationToken cancellationToken)
    {
        BeginUserIntent(cancelPendingLoad: true);
        using var linked = CreateOperationCancellation(cancellationToken);
        PublishWaitingIfBusy();
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_currentSource is null)
            {
                return ControlFailure(
                    PlaybackFailureCode.InvalidRequest,
                    "请先加载视频。",
                    publish: false);
            }

            var options = subtitle ? _controls.SubtitleTracks : _controls.AudioTracks;
            if (!options.Any(option => option.Id == trackId))
            {
                return ControlFailure(
                    PlaybackFailureCode.InvalidRequest,
                    subtitle ? "所选字幕轨已不可用。" : "所选音轨已不可用。",
                    publish: false);
            }

            var changed = await _nativeDispatcher.InvokeAsync(
                    subtitle ? "select-subtitle-track" : "select-audio-track",
                    () => subtitle
                        ? _playerHost.SetSubtitleTrack(trackId)
                        : _playerHost.SetAudioTrack(trackId),
                    linked.Token)
                .ConfigureAwait(false);
            if (!changed)
            {
                return ControlFailure(
                    PlaybackFailureCode.ControlUnavailable,
                    subtitle ? "字幕轨切换失败，已保留原选择。" : "音轨切换失败，已保留原选择。",
                    publish: true);
            }

            UpdateControls(subtitle
                ? _controls with { SelectedSubtitleTrackId = trackId }
                : _controls with { SelectedAudioTrackId = trackId });
            PublishCurrent(Snapshot.State, PlaybackActivity.Idle);
            return PlaybackOperationResult.Succeeded();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch
        {
            return ControlFailure(
                PlaybackFailureCode.ControlUnavailable,
                subtitle ? "字幕轨切换失败，已保留原选择。" : "音轨切换失败，已保留原选择。",
                publish: true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PlaybackOperationResult> ReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        BeginUserIntent(cancelPendingLoad: true);
        using var linked = CreateOperationCancellation(cancellationToken);
        PublishActivity(PlaybackActivity.ReleasingOldMedia);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var source = _currentSource;
            if (source is null)
            {
                PublishEmpty(_surface.Generation);
                return PlaybackOperationResult.Succeeded();
            }

            await _nativeDispatcher.InvokeAsync(
                    "release-media",
                    () =>
                    {
                        source.RequestStop();
                        _playerHost.Stop();
                        _playerHost.Detach();
                    },
                    linked.Token)
                .ConfigureAwait(false);

            _currentSource = null;
            source.Failed -= OnSourceFailed;
            PublishEmpty(_surface.Generation, PlaybackActivity.ReleasingOldMedia);

            // 显式 Release 的调用方可能马上编辑或删除文件，因此必须等待文件句柄真正关闭。
            await _resourceReaper.EnqueueAsync(
                    source,
                    waitForCompletion: true,
                    linked.Token)
                .ConfigureAwait(false);
            PublishEmpty(_surface.Generation);
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

        var clamped = Math.Clamp(volume, 0, 100);
        lock (_snapshotSync)
        {
            _snapshot = _snapshot with { Volume = clamped };
        }

        // 音量 setter 通常很快，但仍属于原生操作。这里采用异步提交，避免滑块拖动调用 UI 线程原生代码。
        _ = SetVolumeCoreAsync(clamped);
        return true;
    }

    private async Task SetVolumeCoreAsync(int volume)
    {
        try
        {
            await _nativeDispatcher.InvokeAsync(
                    "set-volume",
                    () => _playerHost.SetVolume(volume),
                    _lifetimeCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
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

        // NativeControlHost 的销毁回调不能 await。这里保留 G3 的同步屏障：
        // 只有 Stop 返回后才能让 Avalonia 销毁 HWND，否则旧 vout 可能访问失效句柄。
        _operationGate.Wait();
        try
        {
            if (Volatile.Read(ref _disposeState) != 0 || surface != _surface)
            {
                return;
            }

            var source = _currentSource;
            if (source is not null &&
                Snapshot.State is PlaybackState.Playing or PlaybackState.Paused)
            {
                _pendingSurfaceRecovery = new SurfaceRecoverySnapshot(
                    source.Generation,
                    Volatile.Read(ref _intentRevision),
                    _playerHost.PositionMs,
                    Snapshot.State,
                    _controls.Rate,
                    _controls.SelectedAudioTrackId,
                    _controls.SelectedSubtitleTrackId);
                source.RequestStop();
                _playerHost.Stop();
            }
            else
            {
                _pendingSurfaceRecovery = null;
            }

            _playerHost.SetVideoOutputHandle(nint.Zero);
            _surface = default;
            PublishCurrent(
                Snapshot.State,
                _pendingSurfaceRecovery is null
                    ? PlaybackActivity.Idle
                    : PlaybackActivity.WaitingForPlayer);
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
                new PlaybackFailure(
                    PlaybackFailureCode.InvalidRequest,
                    "视频输出句柄无效。"));
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token,
            _lifetimeCancellation.Token);
        var restoreCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        var previous = Interlocked.Exchange(
            ref _surfaceRestoreCancellation,
            restoreCancellation);
        TryCancelAndDispose(previous);

        await _operationGate.WaitAsync(restoreCancellation.Token).ConfigureAwait(false);
        var recoveryStarted = false;
        try
        {
            ThrowIfDisposed();
            _surface = surface;
            _playerHost.SetVideoOutputHandle(surface.Handle);
            var source = _currentSource;
            if (source is null)
            {
                PublishEmpty(surface.Generation);
                return PlaybackOperationResult.Succeeded();
            }

            var recovery = _pendingSurfaceRecovery;
            _pendingSurfaceRecovery = null;
            if (recovery is null ||
                recovery.Value.MediaGeneration != source.Generation ||
                recovery.Value.IntentRevision != Volatile.Read(ref _intentRevision))
            {
                PublishCurrent(Snapshot.State, PlaybackActivity.Idle);
                return PlaybackOperationResult.Succeeded();
            }

            PlaybackResourceDiagnostics.SurfaceRestoreStarted();
            recoveryStarted = true;
            source.PrepareForPlayback();
            var restored = await _nativeDispatcher.InvokeAsync(
                    "restore-surface",
                    token => _playerHost.RestoreSurfaceAsync(
                        recovery.Value.PositionMs,
                        recovery.Value.State == PlaybackState.Paused,
                        token),
                    restoreCancellation.Token)
                .ConfigureAwait(false);
            if (!restored)
            {
                var failure = PlaybackFailureMapper.SurfaceRestoreFailed();
                PublishCurrent(PlaybackState.Stopped, PlaybackActivity.Idle, failure);
                return PlaybackOperationResult.Failed(failure);
            }

            var controlFailure = await RestoreControlsAfterSurfaceAsync(
                    source,
                    recovery.Value,
                    restoreCancellation.Token)
                .ConfigureAwait(false);
            var restoredState = recovery.Value.State == PlaybackState.Paused
                ? PlaybackState.Paused
                : PlaybackState.Playing;
            PublishCurrent(restoredState, PlaybackActivity.Idle, controlFailure);
            return PlaybackOperationResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            if (timeout.IsCancellationRequested)
            {
                var failure = PlaybackFailureMapper.SurfaceRestoreFailed();
                PublishCurrent(PlaybackState.Stopped, PlaybackActivity.Idle, failure);
                return PlaybackOperationResult.Failed(failure);
            }

            return Cancelled();
        }
        catch (Exception)
        {
            var failure = PlaybackFailureMapper.SurfaceRestoreFailed();
            PublishCurrent(PlaybackState.Stopped, PlaybackActivity.Idle, failure);
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
            var source = _currentSource;
            _currentSource = null;
            if (source is not null)
            {
                source.Failed -= OnSourceFailed;
                source.RequestStop();
                try
                {
                    _playerHost.Stop();
                }
                catch
                {
                }
                _playerHost.Detach();
                source.Dispose();
            }

            _playerHost.StateChanged -= OnHostStateChanged;
            _playerHost.PositionChanged -= OnHostPositionChanged;
            _playerHost.Failed -= OnHostFailed;
            if (_playerHost is LazyPlaybackBackend lazyBackend)
            {
                lazyBackend.Created -= OnBackendCreated;
            }
            lock (_snapshotSync)
            {
                _snapshot = PlaybackSnapshot.Empty with
                {
                    State = PlaybackState.Disposed,
                    Activity = PlaybackActivity.Idle
                };
            }
        }
        finally
        {
            Changed = null;
            OutputChanged = null;
            Volatile.Write(ref _disposeState, 2);
            _lifetimeCancellation.Dispose();
            _operationGate.Release();
        }
    }

    private void OnBackendCreated(object? sender, EventArgs e) =>
        OutputChanged?.Invoke(this, EventArgs.Empty);

    private async Task RollBackFailedStartAsync(
        IPlaybackMediaSource? oldSource,
        IPlaybackMediaSource failedSource)
    {
        try
        {
            await _nativeDispatcher.InvokeAsync(
                    "rollback-media",
                    () =>
                    {
                        failedSource.RequestStop();
                        _playerHost.Stop();
                        _playerHost.Detach();
                        if (oldSource is not null)
                        {
                            _playerHost.Attach(oldSource);
                        }
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // 若补偿事务自身在 Stop 阶段失败，failedSource 仍可能被原生线程使用，
            // 绝不能强制释放它。此时把它保留为当前 Source；oldSource 已经在提交时
            // 与播放器解绑，可安全交给 Reaper，避免为了“可回滚”而永久遗失文件所有权。
            _currentSource = failedSource;
            if (oldSource is not null)
            {
                oldSource.Failed -= OnSourceFailed;
                await _resourceReaper.EnqueueAsync(
                        oldSource,
                        waitForCompletion: false,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            throw;
        }

        failedSource.Failed -= OnSourceFailed;
        _currentSource = oldSource;
        await _resourceReaper.EnqueueAsync(
                failedSource,
                waitForCompletion: false,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task ReapCandidateSafelyAsync(IPlaybackMediaSource candidate)
    {
        try
        {
            await _resourceReaper.EnqueueAsync(
                    candidate,
                    waitForCompletion: false,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            candidate.Dispose();
        }
    }

    private void OnHostStateChanged(long generation, PlaybackState state)
    {
        var source = Volatile.Read(ref _currentSource);
        if (source is null || source.Generation != generation)
        {
            return;
        }

        var activity = Snapshot.Activity;
        if (state == PlaybackState.Stopped &&
            activity is PlaybackActivity.StoppingCurrent or
                PlaybackActivity.AttachingCandidate)
        {
            return;
        }

        PublishCurrent(state, activity);

        // Play() 返回成功只表示 LibVLC 接受了启动命令，此时解复用器未必已经公开
        // 完整轨道。真正收到首个 Playing 事件后再刷新一次，才能稳定发现 MP4 的
        // 多音轨和内嵌字幕。用媒体代次做一次性门禁，避免位置/缓冲导致的重复
        // Playing 事件持续重建轨道快照。
        if (state == PlaybackState.Playing &&
            Interlocked.Exchange(
                ref _playingControlsRefreshGeneration,
                generation) != generation)
        {
            _ = RefreshControlsAfterPlayingAsync(source);
        }
    }

    private async Task RefreshControlsAfterPlayingAsync(IPlaybackMediaSource source)
    {
        try
        {
            await _operationGate.WaitAsync(_lifetimeCancellation.Token)
                .ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(_currentSource, source))
                {
                    return;
                }

                await RefreshControlsForCurrentMediaAsync(
                        source,
                        _lifetimeCancellation.Token)
                    .ConfigureAwait(false);
                PublishCurrent(Snapshot.State, Snapshot.Activity);
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch
        {
            // 轨道发现属于增强控制；失败时保留媒体的播放状态和空轨道快照。
            // 原生异常文本可能带私有路径，因此不能向 UI 或日志透传。
        }
    }

    private void OnHostPositionChanged(long generation)
    {
        var source = Volatile.Read(ref _currentSource);
        if (source is not null && source.Generation == generation)
        {
            PublishCurrent(Snapshot.State, Snapshot.Activity);
        }
    }

    private void OnHostFailed(long generation, PlaybackFailure failure)
    {
        var source = Volatile.Read(ref _currentSource);
        if (source is not null && source.Generation == generation)
        {
            _ = HandlePlaybackFailureAsync(source, failure);
        }
    }

    private void OnSourceFailed(
        IPlaybackMediaSource source,
        PlaybackFailure failure)
    {
        if (ReferenceEquals(Volatile.Read(ref _currentSource), source) &&
            Snapshot.Activity is not PlaybackActivity.StoppingCurrent and
                not PlaybackActivity.AttachingCandidate)
        {
            _ = HandlePlaybackFailureAsync(source, failure);
        }
    }

    private async Task HandlePlaybackFailureAsync(
        IPlaybackMediaSource source,
        PlaybackFailure failure)
    {
        try
        {
            await _operationGate.WaitAsync(_lifetimeCancellation.Token)
                .ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(_currentSource, source))
                {
                    return;
                }

                await _nativeDispatcher.InvokeAsync(
                        "stop-after-failure",
                        () =>
                        {
                            source.RequestStop();
                            _playerHost.Stop();
                        },
                        _lifetimeCancellation.Token)
                    .ConfigureAwait(false);
                PublishCurrent(PlaybackState.Faulted, PlaybackActivity.Idle, failure);
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private long BeginUserIntent(bool cancelPendingLoad)
    {
        ThrowIfDisposed();
        _pendingSurfaceRecovery = null;
        CancelSurfaceRestore();
        if (cancelPendingLoad)
        {
            TryCancel(Volatile.Read(ref _mediaSwitchCancellation));
        }
        return Interlocked.Increment(ref _intentRevision);
    }

    private CancellationTokenSource CreateOperationCancellation(
        CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);

    private void PublishWaitingIfBusy()
    {
        if (_operationGate.CurrentCount == 0)
        {
            PublishActivity(PlaybackActivity.WaitingForPlayer);
        }
    }

    private void PublishActivity(PlaybackActivity activity)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        PlaybackSnapshot snapshot;
        lock (_snapshotSync)
        {
            snapshot = _snapshot with
            {
                Activity = activity,
                IsTransitioning = IsBlockingActivity(activity)
            };
            _snapshot = snapshot;
        }
        Changed?.Invoke(this, new PlaybackChangedEventArgs(snapshot));
    }

    private void PublishCurrent(
        PlaybackState state,
        PlaybackActivity activity,
        PlaybackFailure? failure = null)
    {
        var source = Volatile.Read(ref _currentSource);
        if (source is null || Volatile.Read(ref _disposeState) != 0)
        {
            PublishEmpty(_surface.Generation, activity, failure);
            return;
        }

        var snapshot = new PlaybackSnapshot(
            source.Generation,
            state,
            IsBlockingActivity(activity),
            _playerHost.PositionMs,
            _playerHost.DurationMs,
            _playerHost.IsSeekable,
            true,
            _surface.Generation,
            Snapshot.Volume,
            _playerHost.HasVideo,
            _playerHost.HasAudio,
            _playerHost.VideoTrackCount,
            _playerHost.AudioTrackCount,
            _controls,
            activity);
        lock (_snapshotSync)
        {
            _snapshot = snapshot;
        }
        Changed?.Invoke(this, new PlaybackChangedEventArgs(snapshot, failure));
    }

    private void PublishEmpty(
        long surfaceGeneration = 0,
        PlaybackActivity activity = PlaybackActivity.Idle,
        PlaybackFailure? failure = null)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        var snapshot = PlaybackSnapshot.Empty with
        {
            SurfaceGeneration = surfaceGeneration,
            Volume = Snapshot.Volume,
            Controls = PlaybackControlSnapshot.Empty with { Rate = _desiredRate },
            Activity = activity,
            IsTransitioning = IsBlockingActivity(activity)
        };
        lock (_snapshotSync)
        {
            // Release/切换文件夹后旧媒体轨道 ID 已经失效，字段和公开快照必须同时清空。
            _controls = snapshot.Controls;
            _snapshot = snapshot;
        }
        Changed?.Invoke(this, new PlaybackChangedEventArgs(snapshot, failure));
    }

    private void PublishFailure(PlaybackFailure failure)
    {
        var state = _currentSource is null
            ? PlaybackState.Empty
            : Snapshot.State;
        PublishCurrent(state, PlaybackActivity.Idle, failure);
    }

    private PlaybackOperationResult Fail(
        PlaybackFailureCode code,
        string message,
        bool publish)
    {
        var failure = new PlaybackFailure(code, message);
        if (publish)
        {
            PublishCurrent(PlaybackState.Faulted, PlaybackActivity.Idle, failure);
        }
        return PlaybackOperationResult.Failed(failure);
    }

    private PlaybackOperationResult ControlFailure(
        PlaybackFailureCode code,
        string message,
        bool publish)
    {
        var failure = new PlaybackFailure(code, message);
        if (publish)
        {
            // 日常控制失败不代表媒体、解密流或解码器已经失效，因此必须保留稳定状态。
            // 把这类失败发布成 Faulted 会让一个无效字幕 ID 错误地终止整段视频。
            PublishCurrent(Snapshot.State, PlaybackActivity.Idle, failure);
        }
        return PlaybackOperationResult.Failed(failure);
    }

    private async Task<PlaybackFailure?> InitializeControlsForCurrentMediaAsync(
        IPlaybackMediaSource source,
        CancellationToken cancellationToken)
    {
        PlaybackFailure? failure = null;
        var appliedRate = _desiredRate;
        await _nativeDispatcher.InvokeAsync(
                "initialize-media-controls",
                () =>
                {
                    if (!_playerHost.SetRate(_desiredRate))
                    {
                        // 合法倍速仍可能被特定解复用器拒绝。回退到 1.0 后继续提交媒体，
                        // 不能让体验型控制破坏已经认证成功的安全播放事务。
                        _desiredRate = 1.0f;
                        appliedRate = 1.0f;
                        _playerHost.SetRate(1.0f);
                        failure = new PlaybackFailure(
                            PlaybackFailureCode.ControlUnavailable,
                            "新媒体不支持之前选择的倍速，已恢复为 1.0 倍。");
                    }

                    return true;
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!ReferenceEquals(_currentSource, source))
        {
            return null;
        }

        UpdateControls(PlaybackControlSnapshot.Empty with { Rate = appliedRate });
        await RefreshControlsForCurrentMediaAsync(source, cancellationToken)
            .ConfigureAwait(false);
        return failure;
    }

    private async Task RefreshControlsForCurrentMediaAsync(
        IPlaybackMediaSource source,
        CancellationToken cancellationToken)
    {
        var captured = await _nativeDispatcher.InvokeAsync(
                "refresh-media-controls",
                () =>
                {
                    var audio = _playerHost.GetAudioTracks();
                    var subtitles = _playerHost.GetSubtitleTracks();
                    int? audioId = audio.Any(option => option.Id == _playerHost.AudioTrack)
                        ? _playerHost.AudioTrack
                        : null;
                    int? subtitleId = subtitles.Any(option => option.Id == _playerHost.SubtitleTrack)
                        ? _playerHost.SubtitleTrack
                        : subtitles.Any(option => option.Id == -1) ? -1 : null;
                    return new PlaybackControlSnapshot(
                        _desiredRate,
                        audio,
                        audioId,
                        subtitles,
                        subtitleId);
                },
                cancellationToken)
            .ConfigureAwait(false);

        // 原生命令完成时可能已经有更新的媒体接管会话；旧轨道绝不能污染新代次。
        if (ReferenceEquals(_currentSource, source))
        {
            UpdateControls(captured);
        }
    }

    private async Task<PlaybackFailure?> RestoreControlsAfterSurfaceAsync(
        IPlaybackMediaSource source,
        SurfaceRecoverySnapshot recovery,
        CancellationToken cancellationToken)
    {
        PlaybackFailure? failure = null;
        await _nativeDispatcher.InvokeAsync(
                "restore-media-controls",
                () =>
                {
                    var restoredRate = _playerHost.SetRate(recovery.Rate);
                    var audio = _playerHost.GetAudioTracks();
                    var subtitles = _playerHost.GetSubtitleTracks();
                    var audioId = recovery.AudioTrackId;
                    var subtitleId = recovery.SubtitleTrackId;

                    if (audioId.HasValue &&
                        (!audio.Any(option => option.Id == audioId.Value) ||
                         !_playerHost.SetAudioTrack(audioId.Value)))
                    {
                        audioId = audio.Any(option => option.Id == _playerHost.AudioTrack)
                            ? _playerHost.AudioTrack
                            : null;
                        failure = new PlaybackFailure(
                            PlaybackFailureCode.ControlUnavailable,
                            "视频表面恢复后无法恢复原音轨，已使用媒体默认音轨。");
                    }

                    if (subtitleId.HasValue &&
                        (!subtitles.Any(option => option.Id == subtitleId.Value) ||
                         !_playerHost.SetSubtitleTrack(subtitleId.Value)))
                    {
                        subtitleId = subtitles.Any(option => option.Id == _playerHost.SubtitleTrack)
                            ? _playerHost.SubtitleTrack
                            : subtitles.Any(option => option.Id == -1) ? -1 : null;
                        failure ??= new PlaybackFailure(
                            PlaybackFailureCode.ControlUnavailable,
                            "视频表面恢复后无法恢复原字幕轨，已使用媒体默认字幕设置。");
                    }

                    if (!restoredRate)
                    {
                        _desiredRate = 1.0f;
                        _playerHost.SetRate(1.0f);
                        failure ??= new PlaybackFailure(
                            PlaybackFailureCode.ControlUnavailable,
                            "视频表面恢复后无法恢复原倍速，已恢复为 1.0 倍。");
                    }

                    UpdateControls(new PlaybackControlSnapshot(
                        restoredRate ? recovery.Rate : 1.0f,
                        audio,
                        audioId,
                        subtitles,
                        subtitleId));
                    return true;
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ReferenceEquals(_currentSource, source) ? failure : null;
    }

    private void UpdateControls(PlaybackControlSnapshot controls)
    {
        lock (_snapshotSync)
        {
            _controls = controls;
            _snapshot = _snapshot with { Controls = controls };
        }
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right)
        {
            return long.MaxValue;
        }
        if (right < 0 && left < long.MinValue - right)
        {
            return long.MinValue;
        }
        return left + right;
    }

    private static bool IsBlockingActivity(PlaybackActivity activity) =>
        activity is not PlaybackActivity.Idle and
            not PlaybackActivity.ReleasingOldMedia;

    private static PlaybackOperationResult Cancelled() =>
        PlaybackOperationResult.Failed(
            new PlaybackFailure(PlaybackFailureCode.Cancelled, "操作已取消。"));

    private void CancelSurfaceRestore()
    {
        var cancellation = Interlocked.Exchange(
            ref _surfaceRestoreCancellation,
            null);
        TryCancelAndDispose(cancellation);
    }

    private static void TryCancelAndDispose(
        CancellationTokenSource? cancellation)
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
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeState) != 0,
            this);
}
