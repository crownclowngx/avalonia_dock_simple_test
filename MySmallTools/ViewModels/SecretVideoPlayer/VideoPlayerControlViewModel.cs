using System.ComponentModel;
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
    /// MediaPlayer 实例，用于绑定到 VideoView
    /// </summary>
    public MediaPlayer? MediaPlayer => _player?.GetMediaPlayer();

    #endregion

    #region Constructor

    public VideoPlayerControlViewModel()
    {
        _player = new SecureVideoPlayer();

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

    #endregion

    #region Commands

    /// <summary>
    /// 播放命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (await _player.Play())
        {
            _positionTimer.Start();
        }
    }

    /// <summary>
    /// 暂停命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPause))]
    private Task PauseAsync()
    {
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
    /// 清理当前媒体
    /// </summary>
    public void CleanupMedia()
    {
        _positionTimer.Stop();
        _player?.CleanupCurrentMedia();
        Position = 0;
        CurrentTime = "00:00:00";
        TotalTime = "00:00:00";
        StatusMessage = "播放器就绪";
        CurrentState = PlayerStateEnum.Stopped;
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
        return CurrentState != PlayerStateEnum.Playing && _player?.GetMediaPlayer()?.Media != null;
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

    #endregion

    #region Event Handlers

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
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

            // 更新命令状态
            PlayCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => { CurrentTime = FormatTime(e.Time); });
    }

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => { Position = e.Position * 100; });
    }

    private void OnLengthChanged(object? sender, LengthChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => { TotalTime = FormatTime(e.Length); });
    }

    private void OnErrorOccurred(object? sender, string e)
    {
        Dispatcher.UIThread.Post(() => { StatusMessage = e; });
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
            if (disposing)
            {
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

                // 释放播放器资源
                _player?.Dispose();
            }

            _disposed = true;
        }
    }

    #endregion
}