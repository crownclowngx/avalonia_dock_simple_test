using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using MySmallTools.Business.SecretVideoPlayer.Playback;
using MySmallTools.Constants.SecretVideoPlayer;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 播放器展示模型。只把用户意图和原生表面通知转交给播放会话，不编排 LibVLC 生命周期。
/// </summary>
public partial class VideoPlayerControlViewModel : ObservableObject, IDisposable
{
    private readonly ISecureVideoPlaybackSession _session;
    private readonly ILibVlcVideoOutputSource _outputSource;
    private readonly IPlaybackDeploymentProbe _deploymentProbe;
    private readonly IPlaybackBackendInitializer _backendInitializer;
    private readonly DispatcherTimer _positionTimer;
    private CancellationTokenSource? _surfaceAttachmentCancellation;
    private VideoSurfaceToken _surface;
    private bool _disposed;

    public event EventHandler? NativeOutputChanged;

    public bool IsPlaying => CurrentState == PlayerStateEnum.Playing;
    public bool IsPaused => CurrentState == PlayerStateEnum.Paused;

    [ObservableProperty] private string _currentTime = "00:00:00";
    [ObservableProperty] private string _totalTime = "00:00:00";
    [ObservableProperty] private double _position;
    [ObservableProperty] private double _volume = 50;
    [ObservableProperty] private bool _isSeekable;
    [ObservableProperty] private string _bufferInfo = string.Empty;
    [ObservableProperty] private PlayerStateEnum _currentState = PlayerStateEnum.Stopped;
    [ObservableProperty] private bool _isSliderBeingDragged;
    [ObservableProperty] private string _statusMessage = "播放器就绪";
    [ObservableProperty] private bool _isVideoSurfaceReady;
    [ObservableProperty] private bool _isMediaTransitioning;
    [ObservableProperty] private PlaybackFailure? _lastFailure;
    [ObservableProperty] private bool _isPlaybackAvailable;
    [ObservableProperty] private string _deploymentIssueText = string.Empty;
    [ObservableProperty] private string _deploymentCheckedPath = string.Empty;
    [ObservableProperty] private string _deploymentSuggestedAction = string.Empty;

    /// <summary>仅由 VideoPlayerControl 原生输出适配器读取。</summary>
    public MediaPlayer? MediaPlayer => _disposed ? null : _outputSource.MediaPlayer;

    /// <summary>供宿主状态展示和 G3 脱敏集成门禁读取的只读快照。</summary>
    public PlaybackSnapshot PlaybackSnapshot => _session.Snapshot;

    public VideoPlayerControlViewModel(
        ISecureVideoPlaybackSession session,
        ILibVlcVideoOutputSource outputSource,
        IPlaybackDeploymentProbe deploymentProbe,
        IPlaybackBackendInitializer backendInitializer)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _outputSource = outputSource ?? throw new ArgumentNullException(nameof(outputSource));
        _deploymentProbe = deploymentProbe ??
                           throw new ArgumentNullException(nameof(deploymentProbe));
        _backendInitializer = backendInitializer ??
                              throw new ArgumentNullException(nameof(backendInitializer));
        _session.Changed += OnPlaybackChanged;
        _outputSource.OutputChanged += OnNativeOutputChanged;
        _session.SetVolume(50);

        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _positionTimer.Tick += OnPositionTimerTick;
        RefreshDeploymentStatus();
    }

    partial void OnCurrentStateChanged(PlayerStateEnum value)
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        NotifyCommandStates();
    }

    partial void OnVolumeChanged(double value)
    {
        if (!_disposed && !IsMediaTransitioning)
        {
            _session.SetVolume((int)value);
        }
    }

    partial void OnIsVideoSurfaceReadyChanged(bool value) => NotifyCommandStates();
    partial void OnIsMediaTransitioningChanged(bool value) => NotifyCommandStates();
    partial void OnIsPlaybackAvailableChanged(bool value) => NotifyCommandStates();

    [RelayCommand]
    private void RetryDeploymentCheck() => RefreshDeploymentStatus();

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        var result = await _session.PlayAsync();
        ApplyFailure(result);
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseAsync()
    {
        var result = await _session.PauseAsync();
        ApplyFailure(result);
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        StatusMessage = "正在停止...";
        var result = await _session.StopAsync();
        ApplyFailure(result);
    }

    [RelayCommand]
    private void StartSliderDrag()
    {
        IsSliderBeingDragged = true;
        _positionTimer.Stop();
    }

    [RelayCommand]
    private async Task EndSliderDragAsync()
    {
        if (!IsSliderBeingDragged)
        {
            return;
        }

        IsSliderBeingDragged = false;
        var snapshot = _session.Snapshot;
        var requested = snapshot.DurationMs <= 0
            ? 0
            : (long)Math.Round(snapshot.DurationMs * Math.Clamp(Position, 0, 100) / 100d);
        var result = await _session.SeekAsync(requested, waitForFrame: IsPaused);
        ApplyFailure(result);
        if (IsPlaying)
        {
            _positionTimer.Start();
        }
    }

    public Task<bool> LoadMediaAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken = default) =>
        SwitchMediaAsync(filePath, password, startPlayback: false, cancellationToken);

    public Task<bool> LoadAndPlayMediaAsync(string filePath, string password) =>
        LoadAndPlayMediaAsync(filePath, password, CancellationToken.None);

    public Task<bool> LoadAndPlayMediaAsync(
        string filePath,
        string password,
        CancellationToken cancellationToken) =>
        SwitchMediaAsync(filePath, password, startPlayback: true, cancellationToken);

    public async Task CleanupMediaAsync(CancellationToken cancellationToken = default)
    {
        StatusMessage = "正在清理当前视频...";
        var result = await _session.ReleaseAsync(cancellationToken);
        if (result.Success)
        {
            StatusMessage = "播放器就绪";
        }
        else
        {
            ApplyFailure(result);
        }
    }

    /// <summary>供宿主快捷操作和集成验收使用的毫秒 Seek 入口。</summary>
    public async Task<PlaybackOperationResult> SeekMediaAsync(
        long positionMs,
        bool waitForFrame = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _session.SeekAsync(positionMs, waitForFrame, cancellationToken);
        ApplyFailure(result);
        return result;
    }

    /// <summary>
    /// 接收 EmbeddedVideoSurface 的真实 HWND 代次通知。
    /// 丢失通知在 DestroyNativeControlCore 返回前同步完成旧 vout 停止。
    /// </summary>
    public void SetVideoSurface(VideoSurfaceToken? surface)
    {
        if (_disposed)
        {
            return;
        }

        if (surface is null)
        {
            var previous = _surface;
            _surface = default;
            IsVideoSurfaceReady = false;
            CancelSurfaceAttachment();
            if (previous.IsValid)
            {
                _session.DetachSurface(previous);
            }
            return;
        }

        if (!surface.Value.IsValid || surface.Value == _surface)
        {
            return;
        }

        _surface = surface.Value;
        IsVideoSurfaceReady = true;
        CancelSurfaceAttachment();
        var cancellation = new CancellationTokenSource();
        _surfaceAttachmentCancellation = cancellation;
        _ = AttachSurfaceAsync(surface.Value, cancellation);
    }

    private async Task<bool> SwitchMediaAsync(
        string filePath,
        string password,
        bool startPlayback,
        CancellationToken cancellationToken)
    {
        if (!IsPlaybackAvailable)
        {
            RefreshDeploymentStatus();
            if (!IsPlaybackAvailable)
            {
                var unavailableResult = PlaybackOperationResult.Failed(
                    PlaybackFailureMapper.MapDeployment(_deploymentProbe.Check()));
                ApplyFailure(unavailableResult);
                return false;
            }
        }

        // 自动播放必须作为一个完整的业务意图交给播放服务。若 ViewModel 自己执行
        // LoadAsync -> PlayAsync，两个调用之间可能插入 Stop 或另一条 Load，造成旧意图
        // 意外启动新媒体；组合接口用同一个代次令牌保证“认证、提交、启动”不可被拆开。
        var result = startPlayback
            ? await _session.LoadAndPlayAsync(filePath, password, cancellationToken)
            : await _session.LoadAsync(filePath, password, cancellationToken);
        ApplyFailure(result);
        if (result.Success)
        {
            LastFailure = null;
        }
        return result.Success;
    }

    private async Task AttachSurfaceAsync(
        VideoSurfaceToken surface,
        CancellationTokenSource cancellation)
    {
        try
        {
            var result = await _session.AttachAndRestoreSurfaceAsync(
                surface,
                cancellation.Token);
            if (!_disposed && !cancellation.IsCancellationRequested && !result.Success)
            {
                ApplyFailure(result);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_surfaceAttachmentCancellation, cancellation))
            {
                _surfaceAttachmentCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed ||
                e.Snapshot.MediaGeneration < _session.Snapshot.MediaGeneration)
            {
                return;
            }

            ApplySnapshot(e.Snapshot, e.Failure);
        });
    }

    private void OnNativeOutputChanged(object? sender, EventArgs e)
    {
        void NotifyView()
        {
            if (_disposed)
            {
                return;
            }

            OnPropertyChanged(nameof(MediaPlayer));
            NativeOutputChanged?.Invoke(this, EventArgs.Empty);
        }

        // VideoView.Detach 会访问旧的原生 MediaPlayer，因此通知必须在播放服务允许释放
        // Player 之前，于 UI 线程同步完成。若这里只 Post 后立即返回，旧 HWND 的 Detach
        // 就可能与 libvlc_media_player_release 竞态，表现为低概率原生崩溃而非托管异常。
        if (Dispatcher.UIThread.CheckAccess())
        {
            NotifyView();
            return;
        }

        Dispatcher.UIThread.InvokeAsync(NotifyView).GetAwaiter().GetResult();
    }

    private void ApplySnapshot(PlaybackSnapshot snapshot, PlaybackFailure? failure)
    {
        IsMediaTransitioning = snapshot.IsTransitioning;
        IsSeekable = snapshot.IsSeekable && !snapshot.IsTransitioning;
        Volume = snapshot.Volume;
        TotalTime = FormatTime(snapshot.DurationMs);
        if (!IsSliderBeingDragged)
        {
            Position = snapshot.DurationMs <= 0
                ? 0
                : Math.Clamp((double)snapshot.PositionMs / snapshot.DurationMs * 100d, 0, 100);
            CurrentTime = FormatTime(snapshot.PositionMs);
        }

        CurrentState = snapshot.State switch
        {
            PlaybackState.Playing => PlayerStateEnum.Playing,
            PlaybackState.Paused => PlayerStateEnum.Paused,
            PlaybackState.Ended => PlayerStateEnum.Ended,
            PlaybackState.Faulted => PlayerStateEnum.Error,
            _ => PlayerStateEnum.Stopped
        };

        if (failure is not null)
        {
            LastFailure = failure;
            StatusMessage = failure.Message;
        }
        else if (snapshot.Activity != PlaybackActivity.Idle)
        {
            // Activity 描述“当前正在做什么”，State 描述“播放器最终处于什么状态”。
            // 两者分离后，即使旧视频仍在 Playing，也可以明确告诉用户候选视频正在后台解析。
            StatusMessage = snapshot.Activity switch
            {
                PlaybackActivity.PreparingCandidate => "正在验证并解析新视频…",
                PlaybackActivity.WaitingForPlayer => "播放器正在完成上一操作…",
                PlaybackActivity.StoppingCurrent => "正在停止当前视频…",
                PlaybackActivity.AttachingCandidate => "正在切换媒体…",
                PlaybackActivity.StartingPlayback => "正在启动新视频…",
                PlaybackActivity.Stopping => "正在停止并释放解码资源…",
                PlaybackActivity.ReleasingOldMedia => "新视频已启动，正在后台清理旧资源…",
                _ => StatusMessage
            };
        }
        else
        {
            StatusMessage = snapshot.State switch
            {
                PlaybackState.Empty => "播放器就绪",
                PlaybackState.Ready => "媒体加载完成，可以开始播放",
                PlaybackState.Playing => "正在播放",
                PlaybackState.Paused => "已暂停",
                PlaybackState.Stopped => "已停止",
                PlaybackState.Ended => "播放完成",
                PlaybackState.Faulted => "播放错误",
                PlaybackState.Disposed => "播放器已关闭",
                _ => StatusMessage
            };
        }

        // 切换或停止过程中停止位置轮询，避免 UI 定时器继续向已排队的原生操作施压；
        // Dispatcher 本身仍保持响应，待活动回到 Idle 后会按最终状态自动恢复轮询。
        if (snapshot.State == PlaybackState.Playing && !snapshot.IsTransitioning)
        {
            _positionTimer.Start();
        }
        else
        {
            _positionTimer.Stop();
        }
        NotifyCommandStates();
    }

    private void ApplyFailure(PlaybackOperationResult result)
    {
        if (!result.Success && result.Failure is not null &&
            result.Failure.Code != PlaybackFailureCode.Cancelled)
        {
            LastFailure = result.Failure;
            StatusMessage = result.Failure.Message;
            if (result.Failure.Code == PlaybackFailureCode.DeploymentUnavailable)
            {
                var deployment = _deploymentProbe.Check();
                IsPlaybackAvailable = false;
                DeploymentIssueText =
                    $"[{result.Failure.DiagnosticCode ?? "DEPLOYMENT_UNAVAILABLE"}] {result.Failure.Message}";
                DeploymentCheckedPath = deployment.RuntimeDirectory;
                DeploymentSuggestedAction = result.Failure.SuggestedAction ??
                    "请重新部署插件并重启宿主。";
            }
        }
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (!_disposed && !IsSliderBeingDragged)
        {
            ApplySnapshot(_session.Snapshot, null);
        }
    }

    private bool CanPlay() =>
        !_disposed &&
        IsPlaybackAvailable &&
        !IsMediaTransitioning &&
        IsVideoSurfaceReady &&
        CurrentState != PlayerStateEnum.Playing &&
        _session.Snapshot.HasMedia;

    private bool CanPause() =>
        !_disposed &&
        IsPlaybackAvailable &&
        !IsMediaTransitioning &&
        CurrentState == PlayerStateEnum.Playing;

    private bool CanStop() =>
        !_disposed &&
        IsPlaybackAvailable &&
        !IsMediaTransitioning &&
        CurrentState is PlayerStateEnum.Playing or PlayerStateEnum.Paused;

    private void NotifyCommandStates()
    {
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private void RefreshDeploymentStatus()
    {
        if (_disposed)
        {
            return;
        }

        var result = _deploymentProbe.Check();
        IsPlaybackAvailable = result.IsReady;
        if (result.IsReady)
        {
            try
            {
                // G3 的 100 次真实 HWND 生命周期证明：MediaPlayer 必须在 View 首次绑定前
                // 准备好，不能等到线程池中的媒体解析阶段再动态替换原生输出。
                // 因此这里采用“自检门控的页面启动初始化”：坏部署仍可打开诊断页，
                // 完整部署则在页面绑定前恢复 G3 已验证的原生对象构造顺序。
                _backendInitializer.Initialize();
                DeploymentIssueText = string.Empty;
                DeploymentCheckedPath = string.Empty;
                DeploymentSuggestedAction = string.Empty;
                if (_session.Snapshot.State == PlaybackState.Empty)
                {
                    StatusMessage = "播放器部署自检通过";
                }
            }
            catch (PlaybackDeploymentException ex)
            {
                var failure = PlaybackFailureMapper.MapDeployment(ex.Result);
                IsPlaybackAvailable = false;
                DeploymentIssueText = $"[{failure.DiagnosticCode}] {failure.Message}";
                DeploymentCheckedPath = ex.Result.RuntimeDirectory;
                DeploymentSuggestedAction = failure.SuggestedAction ?? "请重新部署插件并重启宿主。";
                StatusMessage = failure.Message;
            }
            return;
        }

        // 探针刻意聚合全部问题，UI 也不能退化成只显示第一项，否则用户修复一个文件后
        // 还要反复重检才能发现下一个缺失项。路径和建议去重后逐行展示，既保留定位信息，
        // 又避免多个 codec 问题重复刷出同一个“重新部署完整目录”提示。
        DeploymentIssueText = string.Join(
            Environment.NewLine,
            result.Issues.Select(issue => $"[{issue.Code}] {issue.Summary}"));
        DeploymentCheckedPath = string.Join(
            Environment.NewLine,
            result.Issues.Select(issue => issue.CheckedPath)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        DeploymentSuggestedAction = string.Join(
            Environment.NewLine,
            result.Issues.Select(issue => issue.SuggestedAction)
                .Distinct(StringComparer.Ordinal));
        StatusMessage = result.Issues[0].Summary;
    }

    private void CancelSurfaceAttachment()
    {
        var cancellation = _surfaceAttachmentCancellation;
        _surfaceAttachmentCancellation = null;
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string FormatTime(long timeMs) =>
        TimeSpan.FromMilliseconds(Math.Max(0, timeMs)).ToString(@"hh\:mm\:ss");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelSurfaceAttachment();
        _positionTimer.Stop();
        _positionTimer.Tick -= OnPositionTimerTick;
        _session.Changed -= OnPlaybackChanged;
        _outputSource.OutputChanged -= OnNativeOutputChanged;
        NativeOutputChanged = null;
        GC.SuppressFinalize(this);
    }
}
