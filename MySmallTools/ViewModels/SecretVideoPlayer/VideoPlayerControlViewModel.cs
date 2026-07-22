using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using MySmallTools.Business.SecretVideoPlayer;
using MySmallTools.Constants.SecretVideoPlayer;
using TimeChangedEventArgs = MySmallTools.Business.SecretVideoPlayer.TimeChangedEventArgs;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 视频播放器控件的 ViewModel
/// 负责处理播放、暂停、停止、进度控制等播放相关功能
/// </summary>
public partial class VideoPlayerControlViewModel : ObservableObject, IDisposable
{
    private readonly object _syncLock = new object();
    private readonly SecureVideoPlayer _player;
    private readonly DispatcherTimer _positionTimer;
    private readonly VideoSurfaceRecoveryPolicy _surfaceRecoveryPolicy;
    private CancellationTokenSource? _surfaceRecoveryCancellation;
    private VideoSurfaceRecoveryRequest? _activeSurfaceRecovery;
    private long _mediaGeneration;
    private int _pendingSurfaceTransitionStops;
    private bool _disposed = false;

    #region Properties

    /// <summary>
    /// 是否正在播放
    /// </summary>
    public bool IsPlaying => CurrentState == PlayerStateEnum.Playing;

    /// <summary>
    /// 是否已暂停
    /// </summary>
    public bool IsPaused => CurrentState == PlayerStateEnum.Paused;

    /// <summary>
    /// 当前播放时间显示
    /// </summary>
    [ObservableProperty] private string _currentTime = "00:00:00";

    /// <summary>
    /// 总时长显示
    /// </summary>
    [ObservableProperty] private string _totalTime = "00:00:00";

    /// <summary>
    /// 播放进度（0-100）
    /// </summary>
    [ObservableProperty] private double _position = 0;

    /// <summary>
    /// 音量（0-100）
    /// </summary>
    [ObservableProperty] private double _volume = 50;

    /// <summary>
    /// 是否可以拖拽进度条
    /// </summary>
    [ObservableProperty] private bool _isSeekable = false;

    /// <summary>
    /// 缓存信息显示
    /// </summary>
    [ObservableProperty] private string _bufferInfo = string.Empty;

    /// <summary>
    /// 当前播放状态
    /// </summary>
    [ObservableProperty] private PlayerStateEnum _currentState = PlayerStateEnum.Stopped;

    /// <summary>
    /// 是否正在拖拽进度条
    /// </summary>
    [ObservableProperty] private bool _isSliderBeingDragged = false;

    /// <summary>
    /// 播放状态消息
    /// </summary>
    [ObservableProperty] private string _statusMessage = "播放器就绪";

    /// <summary>
    /// Avalonia 原生视频子窗口是否已经创建并绑定到当前 MediaPlayer。
    /// </summary>
    [ObservableProperty] private bool _isVideoSurfaceReady;

    /// <summary>
    /// MediaPlayer 实例，用于绑定到 VideoView
    /// </summary>
    public MediaPlayer? MediaPlayer => _disposed ? null : _player.GetMediaPlayer();

    #endregion

    #region Constructor

    public VideoPlayerControlViewModel(
        SecureVideoPlayer player,
        VideoSurfaceRecoveryPolicy surfaceRecoveryPolicy)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _surfaceRecoveryPolicy = surfaceRecoveryPolicy ??
            throw new ArgumentNullException(nameof(surfaceRecoveryPolicy));

        // 订阅播放器事件
        _player.PlaybackStateChanged += OnPlaybackStateChanged;
        _player.TimeChanged += OnTimeChanged;
        _player.PositionChanged += OnPositionChanged;
        _player.LengthChanged += OnLengthChanged;
        _player.ErrorOccurred += OnErrorOccurred;
        _player.SetVolume(50);

        // 位置更新定时器
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _positionTimer.Tick += (s, e) => UpdatePosition();
    }

    #endregion

    #region Property Changed Handlers

    partial void OnCurrentStateChanged(PlayerStateEnum value)
    {
        // 通知UI IsPlaying和IsPaused属性发生了变化
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
    }

    partial void OnVolumeChanged(double value)
    {
        _player?.SetVolume((int)value);
    }

    partial void OnIsVideoSurfaceReadyChanged(bool value)
    {
        PlayCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Commands

    /// <summary>
    /// 播放命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        // 用户主动播放意味着接受当前状态，不应继承之前尚未消费的自动恢复请求。
        CancelSurfaceRecovery();
        await StartPlaybackAsync(isAutomaticResume: false);
    }

    /// <summary>
    /// 暂停命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPause))]
    private Task PauseAsync()
    {
        CancelSurfaceRecovery();
        _player.Pause();
        _positionTimer.Stop();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync()
    {
        CancelSurfaceRecovery();
        _player.Stop();
        _positionTimer.Stop();
        Position = 0;
        CurrentTime = "00:00:00";
        return Task.CompletedTask;
    }

    /// <summary>
    /// 开始拖拽进度条命令
    /// </summary>
    [RelayCommand]
    private void StartSliderDrag()
    {
        IsSliderBeingDragged = true;
        PausePositionUpdates();
    }

    /// <summary>
    /// 结束拖拽进度条命令
    /// </summary>
    [RelayCommand]
    private void EndSliderDrag()
    {
        if (IsSliderBeingDragged)
        {
            IsSliderBeingDragged = false;
            SeekToPosition(Position);
            ResumePositionUpdates();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 加载媒体文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="password">解密密码</param>
    /// <returns>是否加载成功</returns>
    public async Task<bool> LoadMediaAsync(string filePath, string password)
    {
        CancelSurfaceRecovery();
        _mediaGeneration++;
        try
        {
            var success = await _player.LoadEncryptedVideoAsync(filePath, password);
            if (success)
            {
                UpdateVideoInfo();
                // 更新命令状态
                Dispatcher.UIThread.Post(() =>
                {
                    PlayCommand.NotifyCanExecuteChanged();
                    PauseCommand.NotifyCanExecuteChanged();
                    StopCommand.NotifyCanExecuteChanged();
                    StatusMessage = "媒体加载完成，可以开始播放";
                });
                return true;
            }
            else
            {
                StatusMessage = "媒体加载失败";
                return false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 加载加密媒体并在验证成功后立即开始播放。
    /// </summary>
    /// <remarks>
    /// 文件夹视频库使用该入口表达一次完整的“播放所选项”操作；单文件播放器仍可单独调用
    /// <see cref="LoadMediaAsync"/>，保持加载后由用户手动播放的原有行为。
    /// </remarks>
    public async Task<bool> LoadAndPlayMediaAsync(string filePath, string password)
    {
        if (!await LoadMediaAsync(filePath, password))
            return false;

        CancelSurfaceRecovery();
        return await StartPlaybackAsync(isAutomaticResume: false);
    }

    /// <summary>
    /// 清理当前媒体
    /// </summary>
    public void CleanupMedia()
    {
        CancelSurfaceRecovery();
        _mediaGeneration++;
        _positionTimer.Stop();
        _player?.CleanupCurrentMedia();
        Position = 0;
        CurrentTime = "00:00:00";
        TotalTime = "00:00:00";
        StatusMessage = "播放器就绪";
        CurrentState = PlayerStateEnum.Stopped;
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 接收 NativeControlHost 的同步表面状态通知。
    /// </summary>
    /// <remarks>
    /// 表面丢失时必须在该方法返回前 Stop LibVLC，使旧 vout 在调用方销毁 HWND 前完整退出。
    /// 表面恢复时只消费一次恢复快照，并还原播放中或暂停两种用户可见状态。
    /// </remarks>
    public void SetVideoSurfaceReady(bool isReady)
    {
        if (_disposed || IsVideoSurfaceReady == isReady)
        {
            return;
        }

        if (!isReady)
        {
            IsVideoSurfaceReady = false;
            BeginVideoSurfaceLoss(forcePlaying: false);
            return;
        }

        IsVideoSurfaceReady = true;
        var request = _surfaceRecoveryPolicy.ConsumeRecovery(_mediaGeneration);
        if (request is not null && _player.HasMedia)
        {
            _activeSurfaceRecovery = request;
            _ = RestoreAfterSurfaceRecreatedAsync(request.Value);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 更新播放位置
    /// </summary>
    private void UpdatePosition()
    {
        if (IsSliderBeingDragged) // 如果正在拖拽，不更新位置
            return;

        var info = _player.GetVideoInfo();
        if (info == null || info.Duration <= 0)
        {
            return;
        }

        Position = (double)info.Position / info.Duration * 100;
        CurrentTime = FormatTime(info.Position);
    }

    /// <summary>
    /// 跳转到指定位置
    /// </summary>
    /// <param name="positionPercent">位置百分比</param>
    private void SeekToPosition(double positionPercent)
    {
        _player.SetPosition((float)(positionPercent / 100.0));
        Position = positionPercent;
    }

    /// <summary>
    /// 暂停位置更新
    /// </summary>
    private void PausePositionUpdates()
    {
        _positionTimer.Stop();
    }

    /// <summary>
    /// 恢复位置更新
    /// </summary>
    private void ResumePositionUpdates()
    {
        lock (_syncLock)
        {
            if (CurrentState == PlayerStateEnum.Playing && !_disposed)
            {
                _positionTimer.Start();
            }
        }
    }

    /// <summary>
    /// 更新视频信息
    /// </summary>
    private void UpdateVideoInfo()
    {
        var info = _player.GetVideoInfo();
        if (info == null)
        {
            return;
        }

        IsSeekable = info.IsSeekable;
        Volume = info.Volume;
        TotalTime = FormatTime(info.Duration);
    }

    /// <summary>
    /// 格式化时间显示
    /// </summary>
    private string FormatTime(long timeMs)
    {
        var time = TimeSpan.FromMilliseconds(timeMs);
        return time.ToString(@"hh\:mm\:ss");
    }

    /// <summary>
    /// 检查是否可以播放
    /// </summary>
    private bool CanPlay()
    {
        return IsVideoSurfaceReady &&
               CurrentState != PlayerStateEnum.Playing &&
               _player.HasMedia;
    }

    /// <summary>
    /// 检查是否可以暂停
    /// </summary>
    private bool CanPause()
    {
        return CurrentState == PlayerStateEnum.Playing;
    }

    /// <summary>
    /// 检查是否可以停止
    /// </summary>
    private bool CanStop()
    {
        return CurrentState == PlayerStateEnum.Playing || CurrentState == PlayerStateEnum.Paused;
    }

    /// <summary>
    /// 在确认 HWND 可用后启动播放，并处理播放准备期间表面再次丢失的竞争情况。
    /// </summary>
    private async Task<bool> StartPlaybackAsync(bool isAutomaticResume)
    {
        if (!IsVideoSurfaceReady)
        {
            StatusMessage = "视频输出表面尚未准备完成";
            return false;
        }

        var success = await _player.Play();
        if (!success)
        {
            if (isAutomaticResume)
            {
                StatusMessage = "视频表面恢复后自动续播失败，请手动播放";
            }
            return false;
        }

        // Media.Parse 可能产生异步等待；如果等待期间 Dock 又销毁了 HWND，
        // 立即暂停并登记下一次续播，绝不允许已启动的 vout 在零 HWND 上继续工作。
        if (!IsVideoSurfaceReady)
        {
            BeginVideoSurfaceLoss(forcePlaying: true);
            return false;
        }

        _positionTimer.Start();
        return true;
    }

    private void BeginVideoSurfaceLoss(bool forcePlaying)
    {
        var previousRecovery = _activeSurfaceRecovery;
        _activeSurfaceRecovery = null;
        CancelSurfaceRecoveryCancellationOnly();

        var isPlaying = previousRecovery?.PlaybackMode == VideoSurfacePlaybackMode.Playing ||
                        forcePlaying ||
                        CurrentState == PlayerStateEnum.Playing ||
                        _player.GetMediaPlayer().IsPlaying;
        var isPaused = previousRecovery?.PlaybackMode == VideoSurfacePlaybackMode.Paused ||
                       (!isPlaying && (CurrentState == PlayerStateEnum.Paused || _player.IsPaused));

        var request = _surfaceRecoveryPolicy.OnSurfaceLost(
            _mediaGeneration,
            previousRecovery?.PositionMs ?? _player.PlaybackTime,
            _player.HasMedia,
            isPlaying,
            isPaused);
        if (request is null)
        {
            return;
        }

        // Stop 必须发生在 EmbeddedVideoSurface 调用基类销毁旧 HWND 之前。
        // 它会同步等待旧 vout 退出，因此切回时 Play 一定创建绑定新 HWND 的输出。
        Interlocked.Increment(ref _pendingSurfaceTransitionStops);
        try
        {
            _player.StopForVideoSurfaceTransition();
        }
        catch
        {
            Interlocked.Decrement(ref _pendingSurfaceTransitionStops);
            throw;
        }

        _positionTimer.Stop();
        StatusMessage = request.Value.PlaybackMode == VideoSurfacePlaybackMode.Playing
            ? "视频输出表面正在重建，稍后自动继续播放"
            : "视频输出表面正在重建，稍后恢复暂停画面";
    }

    private async Task RestoreAfterSurfaceRecreatedAsync(VideoSurfaceRecoveryRequest request)
    {
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _surfaceRecoveryCancellation = cancellation;
        try
        {
            var restored = await _player.RestoreVideoSurfaceAsync(
                request.PositionMs,
                request.PlaybackMode == VideoSurfacePlaybackMode.Paused,
                cancellation.Token);

            if (!restored)
            {
                throw new InvalidOperationException("LibVLC 未能在新视频表面上启动播放。");
            }

            if (_disposed || cancellation.IsCancellationRequested ||
                !IsVideoSurfaceReady || request.MediaGeneration != _mediaGeneration)
            {
                return;
            }

            if (request.PlaybackMode == VideoSurfacePlaybackMode.Playing)
            {
                CurrentState = PlayerStateEnum.Playing;
                _positionTimer.Start();
                StatusMessage = "视频输出表面已恢复，继续播放";
            }
            else
            {
                CurrentState = PlayerStateEnum.Paused;
                _positionTimer.Stop();
                UpdatePosition();
                StatusMessage = "视频输出表面已恢复，保持暂停";
            }
        }
        catch (OperationCanceledException)
        {
            if (!_disposed &&
                _activeSurfaceRecovery?.RequestId == request.RequestId &&
                request.MediaGeneration == _mediaGeneration && IsVideoSurfaceReady)
            {
                HandleSurfaceRestoreFailure("等待视频输出或首帧超时");
            }
            // 其余取消来自再次切出、主动操作或媒体切换，新状态负责后续处理。
        }
        catch (Exception ex)
        {
            if (!_disposed && request.MediaGeneration == _mediaGeneration && IsVideoSurfaceReady)
            {
                HandleSurfaceRestoreFailure(ex.Message);
            }
        }
        finally
        {
            if (_activeSurfaceRecovery?.RequestId == request.RequestId)
            {
                _activeSurfaceRecovery = null;
            }

            if (ReferenceEquals(_surfaceRecoveryCancellation, cancellation))
            {
                _surfaceRecoveryCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelSurfaceRecoveryCancellationOnly()
    {
        var cancellation = _surfaceRecoveryCancellation;
        _surfaceRecoveryCancellation = null;
        cancellation?.Cancel();
    }

    private void CancelSurfaceRecovery()
    {
        _activeSurfaceRecovery = null;
        _surfaceRecoveryPolicy.Cancel();
        CancelSurfaceRecoveryCancellationOnly();
    }

    private void HandleSurfaceRestoreFailure(string detail)
    {
        Interlocked.Increment(ref _pendingSurfaceTransitionStops);
        try
        {
            _player.Stop();
        }
        catch
        {
            Interlocked.Decrement(ref _pendingSurfaceTransitionStops);
            throw;
        }
        _surfaceRecoveryPolicy.Cancel();
        _positionTimer.Stop();
        CurrentState = PlayerStateEnum.Stopped;
        StatusMessage = $"视频表面自动恢复失败，请手动播放: {detail}";
    }

    #endregion

    #region Event Handlers

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        // 每个表面重建 Stop 对应消费一个 Stopped 事件，即使原生事件稍晚到达，
        // 也不会被误认为用户主动停止并清除恢复快照。
        var isSurfaceTransitionStop = e.State == PlaybackState.Stopped && TryConsumeSurfaceTransitionStop();
        var mediaGeneration = Volatile.Read(ref _mediaGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || mediaGeneration != Volatile.Read(ref _mediaGeneration))
            {
                return;
            }

            if (e.State == PlaybackState.Stopped)
            {
                _surfaceRecoveryPolicy.OnPlaybackStopped(isSurfaceTransitionStop);
                if (isSurfaceTransitionStop)
                {
                    PlayCommand.NotifyCanExecuteChanged();
                    PauseCommand.NotifyCanExecuteChanged();
                    StopCommand.NotifyCanExecuteChanged();
                    return;
                }
            }

            CurrentState = e.State switch
            {
                PlaybackState.Playing => PlayerStateEnum.Playing,
                PlaybackState.Paused => PlayerStateEnum.Paused,
                PlaybackState.Stopped => PlayerStateEnum.Stopped,
                PlaybackState.Ended => PlayerStateEnum.Ended,
                PlaybackState.Error => PlayerStateEnum.Error,
                _ => CurrentState
            };

            StatusMessage = e.State switch
            {
                PlaybackState.Playing => "正在播放",
                PlaybackState.Paused => "已暂停",
                PlaybackState.Stopped => "已停止",
                PlaybackState.Ended => "播放完成",
                PlaybackState.Error => "播放错误",
                _ => StatusMessage
            };

            if (e.State is PlaybackState.Ended or PlaybackState.Error)
            {
                CancelSurfaceRecovery();
            }

            // 更新命令状态
            PlayCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        });
    }

    private bool TryConsumeSurfaceTransitionStop()
    {
        while (true)
        {
            var pending = Volatile.Read(ref _pendingSurfaceTransitionStops);
            if (pending <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref _pendingSurfaceTransitionStops,
                    pending - 1,
                    pending) == pending)
            {
                return true;
            }
        }
    }

    private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
    {
        var mediaGeneration = Volatile.Read(ref _mediaGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && mediaGeneration == Volatile.Read(ref _mediaGeneration))
            {
                CurrentTime = FormatTime(e.Time);
            }
        });
    }

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        var mediaGeneration = Volatile.Read(ref _mediaGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && mediaGeneration == Volatile.Read(ref _mediaGeneration))
            {
                Position = e.Position * 100;
            }
        });
    }

    private void OnLengthChanged(object? sender, LengthChangedEventArgs e)
    {
        var mediaGeneration = Volatile.Read(ref _mediaGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && mediaGeneration == Volatile.Read(ref _mediaGeneration))
            {
                TotalTime = FormatTime(e.Length);
            }
        });
    }

    private void OnErrorOccurred(object? sender, string e)
    {
        var mediaGeneration = Volatile.Read(ref _mediaGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && mediaGeneration == Volatile.Read(ref _mediaGeneration))
            {
                StatusMessage = e;
            }
        });
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            // 先使所有已投递回调失效，再取消异步恢复。Scope 会在本对象 Dispose 返回后
            // 继续释放 SecureVideoPlayer，因此这里绝不能再次释放注入的播放器。
            _disposed = true;
            _mediaGeneration++;
            if (disposing)
            {
                CancelSurfaceRecovery();
                // 取消所有事件订阅
                if (_player != null)
                {
                    _player.PlaybackStateChanged -= OnPlaybackStateChanged;
                    _player.TimeChanged -= OnTimeChanged;
                    _player.PositionChanged -= OnPositionChanged;
                    _player.LengthChanged -= OnLengthChanged;
                    _player.ErrorOccurred -= OnErrorOccurred;
                }

                // 释放定时器资源
                _positionTimer?.Stop();

            }
        }
    }

    #endregion
}
