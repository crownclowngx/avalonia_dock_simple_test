using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using LibVLCSharp.Shared;
using MySmallTools.Business.SecretVideoPlayer;
using Avalonia.Threading;
using Dock.Model.Mvvm.Controls;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 加密视频播放器视图模型
/// </summary>
public class SecretVideoPlayerViewModel : Document, INotifyPropertyChanged, IDisposable
{
    private readonly SecureVideoPlayer _player;
    private readonly DispatcherTimer _positionTimer;
    
    private string _filePath = string.Empty;
    private string _password = string.Empty;
    private string _statusMessage = "请选择加密视频文件";
    private string _currentTime = "00:00:00";
    private string _totalTime = "00:00:00";
    private double _position = 0;
    private double _volume = 50;
    private bool _isPlaying = false;
    private bool _isPaused = false;
    private bool _isLoading = false;
    private bool _isSeekable = false;
    private string _bufferInfo = string.Empty;
    private bool _disposed = false;
    
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
        
        // 初始化命令
        LoadVideoCommand = new RelayCommand(async () => await LoadVideoAsync(), () => !_isLoading);
        PlayCommand = new RelayCommand(() => Play(), () => CanPlay());
        PauseCommand = new RelayCommand(() => Pause(), () => CanPause());
        StopCommand = new RelayCommand(() => Stop(), () => CanStop());
        
        // 位置更新定时器
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _positionTimer.Tick += (s, e) => UpdatePosition();
    }
    
    #region Properties
    
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }
    
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }
    
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
    
    public string CurrentTime
    {
        get => _currentTime;
        set => SetProperty(ref _currentTime, value);
    }
    
    public string TotalTime
    {
        get => _totalTime;
        set => SetProperty(ref _totalTime, value);
    }
    
    public double Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value) && _isSeekable)
            {
                _ = Task.Run(async () => await _player.SetPositionAsync((float)(value / 100.0)));
            }
        }
    }
    
    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value))
            {
                _player.SetVolume((int)value);
            }
        }
    }
    
    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }
    
    public bool IsPaused
    {
        get => _isPaused;
        set => SetProperty(ref _isPaused, value);
    }
    
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }
    
    public bool IsSeekable
    {
        get => _isSeekable;
        set => SetProperty(ref _isSeekable, value);
    }
    
    public string BufferInfo
    {
        get => _bufferInfo;
        set => SetProperty(ref _bufferInfo, value);
    }
    
    public MediaPlayer MediaPlayer => _player.GetMediaPlayer();
    
    #endregion
    
    #region Commands
    
    public ICommand LoadVideoCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    
    #endregion
    
    #region Methods
    
    /// <summary>
    /// 加载视频文件
    /// </summary>
    public async Task LoadVideoAsync()
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
            
            if (success)
            {
                StatusMessage = "视频文件加载成功";
                UpdateVideoInfo();
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
        }
    }
    
    /// <summary>
    /// 播放视频
    /// </summary>
    public void Play()
    {
        if (_player.Play())
        {
            _positionTimer.Start();
        }
    }
    
    /// <summary>
    /// 暂停播放
    /// </summary>
    public void Pause()
    {
        _player.Pause();
        _positionTimer.Stop();
    }
    
    /// <summary>
    /// 停止播放
    /// </summary>
    public void Stop()
    {
        _player.Stop();
        _positionTimer.Stop();
        Position = 0;
        CurrentTime = "00:00:00";
    }
    
    /// <summary>
    /// 设置播放位置
    /// </summary>
    public async Task SetPositionAsync(double positionPercent)
    {
        if (_isSeekable)
        {
            await _player.SetPositionAsync((float)(positionPercent / 100.0));
        }
    }
    
    /// <summary>
    /// 更新视频信息
    /// </summary>
    private void UpdateVideoInfo()
    {
        var info = _player.GetVideoInfo();
        if (info != null)
        {
            IsSeekable = info.IsSeekable;
            Volume = info.Volume;
            TotalTime = FormatTime(info.Duration);
        }
    }
    
    /// <summary>
    /// 更新播放位置
    /// </summary>
    private void UpdatePosition()
    {
        var info = _player.GetVideoInfo();
        if (info != null && info.Duration > 0)
        {
            Position = (double)info.Position / info.Duration * 100;
            CurrentTime = FormatTime(info.Position);
        }
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
        return !_isLoading && !_isPlaying && _player.GetMediaPlayer().Media != null;
    }
    
    /// <summary>
    /// 检查是否可以暂停
    /// </summary>
    private bool CanPause()
    {
        return _isPlaying;
    }
    
    /// <summary>
    /// 检查是否可以停止
    /// </summary>
    private bool CanStop()
    {
        return _isPlaying || _isPaused;
    }
    
    #endregion
    
    #region Event Handlers
    
    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = e.State == PlaybackState.Playing;
            IsPaused = e.State == PlaybackState.Paused;
            
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
            ((RelayCommand)PlayCommand).RaiseCanExecuteChanged();
            ((RelayCommand)PauseCommand).RaiseCanExecuteChanged();
            ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
        });
    }
    
    private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CurrentTime = FormatTime(e.Time);
        });
    }
    
    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Position = e.Position * 100;
        });
    }
    
    private void OnLengthChanged(object? sender, LengthChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            TotalTime = FormatTime(e.Length);
        });
    }
    
    private void OnErrorOccurred(object? sender, string e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = e;
        });
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
    
    #region INotifyPropertyChanged
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    
    #endregion
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _positionTimer?.Stop();
            _player?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// 简单的命令实现
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }
    
    public event EventHandler? CanExecuteChanged;
    
    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }
    
    public void Execute(object? parameter)
    {
        _execute();
    }
    
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}