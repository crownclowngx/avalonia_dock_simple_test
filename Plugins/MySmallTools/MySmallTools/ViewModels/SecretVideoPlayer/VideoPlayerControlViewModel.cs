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

    /// <summary>仅由 VideoPlayerControl 原生输出适配器读取。</summary>
    public MediaPlayer? MediaPlayer => _disposed ? null : _outputSource.MediaPlayer;

    /// <summary>供宿主状态展示和 G3 脱敏集成门禁读取的只读快照。</summary>
    public PlaybackSnapshot PlaybackSnapshot => _session.Snapshot;

    public VideoPlayerControlViewModel(
        ISecureVideoPlaybackSession session,
        ILibVlcVideoOutputSource outputSource)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _outputSource = outputSource ?? throw new ArgumentNullException(nameof(outputSource));
        _session.Changed += OnPlaybackChanged;
        _outputSource.OutputChanged += OnNativeOutputChanged;
        _session.SetVolume(50);

        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _positionTimer.Tick += OnPositionTimerTick;
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
        StatusMessage = "正在验证并解析候选视频...";
        var load = await _session.LoadAsync(filePath, password, cancellationToken);
        if (!load.Success)
        {
            ApplyFailure(load);
            return false;
        }
        LastFailure = null;

        if (!startPlayback)
        {
            StatusMessage = "媒体加载完成，可以开始播放";
            return true;
        }

        var play = await _session.PlayAsync(cancellationToken);
        ApplyFailure(play);
        if (play.Success)
        {
            LastFailure = null;
        }
        return play.Success;
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

        // VideoView.Detach touches the old native MediaPlayer.  The notification
        // must therefore finish on the UI thread before the playback service is
        // allowed to dispose that player; posting it would race a stale HWND
        // detach against libvlc_media_player_release.
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
        else if (snapshot.IsTransitioning)
        {
            StatusMessage = "正在验证并切换媒体...";
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

        if (snapshot.State == PlaybackState.Playing)
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
        !IsMediaTransitioning &&
        IsVideoSurfaceReady &&
        CurrentState != PlayerStateEnum.Playing &&
        _session.Snapshot.HasMedia;

    private bool CanPause() =>
        !_disposed &&
        !IsMediaTransitioning &&
        CurrentState == PlayerStateEnum.Playing;

    private bool CanStop() =>
        !_disposed &&
        !IsMediaTransitioning &&
        CurrentState is PlayerStateEnum.Playing or PlayerStateEnum.Paused;

    private void NotifyCommandStates()
    {
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
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
