using System.ComponentModel;
using System.Runtime.Serialization;
using Avalonia;
using Avalonia.Controls;
using LibVLCSharp.Shared;
using MySmallTools.Business.SecretVideoPlayer;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
// 添加 CommunityToolkit.Mvvm 命名空间
using CommunityToolkit.Mvvm.Input;
using MySmallTools.Constants.SecretVideoPlayer;
using Ursa.Controls;
using TimeChangedEventArgs = MySmallTools.Business.SecretVideoPlayer.TimeChangedEventArgs;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 加密视频播放器视图模型
/// </summary>
public partial class SecretVideoPlayerViewModel : Document, IDisposable
{
    private readonly object _syncLock = new object();
    
    private readonly SecureVideoPlayer _player;
    private readonly DispatcherTimer _positionTimer;
    private bool _disposed = false;

    public bool IsPlaying => CurrentState == PlayerStateEnum.Playing;
    public bool IsPaused => CurrentState == PlayerStateEnum.Paused;
    [ObservableProperty] private string _filePath = string.Empty;

    [ObservableProperty] private string _password = string.Empty;

    [ObservableProperty] private string _statusMessage = "请选择加密视频文件";

    [ObservableProperty] private string _currentTime = "00:00:00";

    [ObservableProperty] private string _totalTime = "00:00:00";

    [ObservableProperty] private double _position = 0;

    [ObservableProperty] private double _volume = 50;
    
    [ObservableProperty] private bool _isLoading = false;

    [ObservableProperty] private bool _isSeekable = false;

    [ObservableProperty] private string _bufferInfo = string.Empty;
    
    [ObservableProperty] private PlayerStateEnum _currentState = PlayerStateEnum.Stopped;
    
    [ObservableProperty] private bool _isSliderBeingDragged = false;
    
    public MediaPlayer MediaPlayer => _player?.GetMediaPlayer();

    
    public SecretVideoPlayerViewModel()
    {
        _player = new SecureVideoPlayer();

        // 订阅播放器事件
        _player.PlaybackStateChanged += OnPlaybackStateChanged;
        _player.TimeChanged += OnTimeChanged;
        _player.PositionChanged += OnPositionChanged;
        _player.LengthChanged += OnLengthChanged;
        _player.ErrorOccurred += OnErrorOccurred;
        _player.BufferStatisticsUpdated += OnBufferStatisticsUpdated;
        _player.SetVolume(50);
        // 位置更新定时器
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _positionTimer.Tick += (s, e) => UpdatePosition();
    }
    
    partial void OnCurrentStateChanged(PlayerStateEnum value)
    {
        // 通知UI IsPlaying和IsPaused属性发生了变化
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
    }

    partial void OnVolumeChanged(double value){
        _player.SetVolume((int)value);
    }
    #region Commands

    [RelayCommand(CanExecute = nameof(CanLoadVideo))]
    private async Task LoadVideoAsync()
    {
        if (string.IsNullOrEmpty(FilePath) || string.IsNullOrEmpty(Password))
        {
            StatusMessage = "请输入文件路径和密码";
            return;
        }

        if (!File.Exists(FilePath))
        {
            StatusMessage = "文件不存在";
            return;
        }

        IsLoading = true;
        StatusMessage = "正在加载视频文件...";

        try
        {
            var success = await _player.LoadEncryptedVideoAsync(FilePath, Password);
            GC.Collect(GC.MaxGeneration);
            if (success)
            {
                StatusMessage = "视频文件加载成功";
                UpdateVideoInfo();
                // 关键修复：加载成功后立即更新命令状态
                Dispatcher.UIThread.Post(() =>
                {
                    // 强制更新命令的 CanExecute 状态
                    PlayCommand.NotifyCanExecuteChanged();
                    PauseCommand.NotifyCanExecuteChanged();
                    StopCommand.NotifyCanExecuteChanged();

                    StatusMessage = "视频加载完成，可以开始播放";
                });
            }
            else
            {
                StatusMessage = "加载失败，请检查文件和密码";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            // 更新加载命令状态
            LoadVideoCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task Play()
    {
        if (await _player.Play())
        {
            _positionTimer.Start();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private Task Pause()
    {
        _player.Pause();
        _positionTimer.Stop();
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task Stop()
    {
        _player.Stop();
        _positionTimer.Stop();
        Position = 0;
        CurrentTime = "00:00:00";
        return Task.CompletedTask;
    }
    // 使用RelayCommand代替直接方法调用
    [RelayCommand]
    private void StartSliderDrag()
    {
        IsSliderBeingDragged = true;
        PausePositionUpdates();
    }

    [RelayCommand]
    private void EndSliderDrag()
    {
        if (IsSliderBeingDragged)
        {
            IsSliderBeingDragged = false;
            // 由于Avalonia的绑定是双向的，Position属性已经更新
            // 这里可以直接使用Position的值
            SeekToPosition(Position);
            ResumePositionUpdates();
        }
    }

    #endregion

    #region Methods
    // 修改UpdatePosition方法，在拖拽时不更新位置
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
    /// 跳转到指定位置（用于拖拽跳转）
    /// </summary>
    public void SeekToPosition(double positionPercent)
    {
        _player.SetPosition((float)(positionPercent / 100.0));
        _position = positionPercent;
    }

    /// <summary>
    /// 暂停位置更新（拖拽时使用）
    /// </summary>
    public void PausePositionUpdates()
    {
        _positionTimer.Stop();
    }

    /// <summary>
    /// 恢复位置更新（拖拽结束后使用）
    /// </summary>
    public void ResumePositionUpdates()
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
    /// 检查是否可以加载视频
    /// </summary>
    private bool CanLoadVideo()
    {
        return !IsLoading;
    }

    /// <summary>
    /// 检查是否可以播放
    /// </summary>
    private bool CanPlay()
    {
        return !IsLoading && CurrentState != PlayerStateEnum.Playing && _player?.GetMediaPlayer()?.Media != null;
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

    private void OnBufferStatisticsUpdated(object? sender, BufferStatistics e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            BufferInfo = $"缓存: {e.CachedBlocks}/{e.MaxCacheBlocks} | " +
                         $"命中率: {e.HitRate:P1} | " +
                         $"内存: {e.TotalMemoryUsage / 1024 / 1024:F1}MB";
        });
    }

    #endregion


    public void Dispose()
    {
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 取消所有事件订阅
                _player.PlaybackStateChanged -= OnPlaybackStateChanged;
                _player.TimeChanged -= OnTimeChanged;
                _player.PositionChanged -= OnPositionChanged;
                _player.LengthChanged -= OnLengthChanged;
                _player.ErrorOccurred -= OnErrorOccurred;
                _player.BufferStatisticsUpdated -= OnBufferStatisticsUpdated;

                // 释放定时器资源
                _positionTimer?.Stop();
                if (_positionTimer != null)
                {
                    _positionTimer.Tick -= (s, e) => UpdatePosition();
                }

                // 释放播放器资源
                _player?.Dispose();
            }

            _disposed = true;
        }
    }
    
    public override bool OnClose()
    {
        var response = Stop();
        response.Wait();
        _player.CleanupCurrentMedia();
        return base.OnClose();
    }
}