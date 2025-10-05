using LibVLCSharp.Shared;
using System.Security.Cryptography;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 安全视频播放器 - 使用一次性解密方案
/// </summary>
public class SecureVideoPlayer : IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _player;
    private readonly SmartVideoEncryptor _encryptor;
    private static bool _isLibVlcInitialized = false;
    
    private FullVideoDecryptor? _decryptor;
    private Media? _currentMedia;
    private SeekableMemoryMediaInput? _seekableMemoryMediaInput;
    private string? _currentPassword;
    private bool _disposed;
    private VideoMetadata? _cachedMetadata;
    
    // 播放状态事件
    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;
    public event EventHandler<TimeChangedEventArgs>? TimeChanged;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    public event EventHandler<LengthChangedEventArgs>? LengthChanged;
    public event EventHandler<string>? ErrorOccurred;
    
    // 播放统计事件
    public event EventHandler<BufferStatistics>? BufferStatisticsUpdated;
    
    public SecureVideoPlayer()
    {
        try
        {
            // 确保LibVLC只初始化一次
            if (!_isLibVlcInitialized)
            {
                Core.Initialize();
                _isLibVlcInitialized = true;
            }
            _libVLC = new LibVLC();
            _player = new MediaPlayer(_libVLC);
            _encryptor = new SmartVideoEncryptor();
            // 订阅播放器事件
            SubscribeToPlayerEvents();
        }
        catch (Exception ex)
        {
            throw;
        }
    }
    
    /// <summary>
    /// 订阅播放器事件
    /// </summary>
    private void SubscribeToPlayerEvents()
    {
        _player.Playing += (s, e) => 
        {
            PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Playing));
        };
        
        _player.Paused += (s, e) => 
        {
            PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Paused));
        };
        
        _player.Stopped += (s, e) => 
        {
            PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Stopped));
        };
        
        _player.EndReached += (s, e) => 
        {
            PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Ended));
        };
        
        _player.TimeChanged += (s, e) => 
        {
            TimeChanged?.Invoke(this, new TimeChangedEventArgs(e.Time));
        };
        
        _player.PositionChanged += (s, e) => 
        {
            PositionChanged?.Invoke(this, new PositionChangedEventArgs(e.Position));
        };
        
        _player.LengthChanged += (s, e) => 
        {
            ErrorOccurred?.Invoke(this, $"事件: 媒体长度变化 - {e.Length}ms");
            LengthChanged?.Invoke(this, new LengthChangedEventArgs(e.Length));
        };
    }
    
    /// <summary>
    /// 加载加密视频文件
    /// </summary>
    public async Task<bool> LoadEncryptedVideoAsync(string filePath, string password)
    {
        try
        {
            
            if (_disposed) throw new ObjectDisposedException(nameof(SecureVideoPlayer));
            
            // 检查文件是否存在
            if (!File.Exists(filePath))
            {
                return false;
            }
            
            // 清理之前的资源
            CleanupCurrentMedia();
            
            // 验证文件是否为加密视频
            if (!_encryptor.IsEncryptedVideo(filePath))
            {
                return false;
            }
            
            // 创建解密器
            _decryptor = new FullVideoDecryptor(filePath, password);
            
            // 绑定解密进度事件
            _decryptor.ProgressChanged += (sender, args) =>
            {
                if (args.IsError)
                {
                    ErrorOccurred?.Invoke(this, args.Message);
                }
                else
                {
                    ErrorOccurred?.Invoke(this, args.Message);
                }
            };
            
            // 执行解密
            var decryptSuccess = await _decryptor.DecryptVideoAsync();
            
            if (!decryptSuccess)
            {
                return false;
            }
            
            // 获取解密后的流
            var decryptedStream = _decryptor.DecryptedStream;
            if (decryptedStream == null)
            {
                return false;
            }
            // 使用自定义的SeekableMemoryMediaInput来支持seeking
             _seekableMemoryMediaInput = new SeekableMemoryMediaInput((MemoryStream)decryptedStream);
            // 使用自定义MediaInput创建媒体（支持seeking）
            _currentMedia = new Media(_libVLC, _seekableMemoryMediaInput);
            // 设置媒体到播放器
            _player.Media = _currentMedia;
            // 尝试解析媒体
            _currentMedia.Parse(MediaParseOptions.ParseLocal);
            _currentPassword = password;
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            return false;
        }
        catch (Exception ex)
        {
            return false;
        }
    }
    

    
    /// <summary>
    /// 播放视频
    /// </summary>
    public async Task<bool> Play()
    {
        if (_disposed)
        {
            return false;
        }
        
        if (_player == null)
        {
            return false;
        }
        
        if (_player.Media == null)
        {
            return false;
        }
        
        try
        {
            // 检查媒体是否已解析
            if (_currentMedia != null && !_currentMedia.IsParsed)
            {
                _currentMedia.Parse(MediaParseOptions.ParseLocal);
                
                // 等待解析完成
                var parseStart = DateTime.Now;
                while (!_currentMedia.IsParsed && DateTime.Now - parseStart < TimeSpan.FromSeconds(5))
                {
                    await Task.Delay(100);
                }
            }
            var result = _player.Play();
            return result;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"播放失败: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 暂停播放
    /// </summary>
    public void Pause()
    {
        if (_disposed || _player == null) return;
        
        try
        {
            _player.Pause();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"暂停失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 停止播放
    /// </summary>
    public void Stop()
    {
        if (_disposed || _player == null) return;
        
        try
        {
            _player.Stop();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"停止失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 设置播放位置（0.0 - 1.0）
    /// </summary>
    public void SetPosition(float position)
    {
        if (_disposed || _player?.Media == null) return;
        
        try
        {
            _player.Position = position;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"设置播放位置失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 设置播放时间（毫秒）
    /// </summary>
    public void SetTime(long timeMs)
    {
        if (_disposed || _player?.Media == null) return;
        
        try
        {
            _player.Time = timeMs;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"设置播放时间失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 设置音量（0-100）
    /// </summary>
    public bool SetVolume(int volume)
    {
        if (_disposed || _player == null) return false;
        
        try
        {
            volume = Math.Clamp(volume, 0, 100);
            _player.Volume = volume;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"设置音量失败: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 获取视频信息
    /// </summary>
    public VideoInfo? GetVideoInfo()
    {
        if (_disposed) return null;
        
        try
        {
            var videoInfo = new VideoInfo();
            
            // 如果有缓存的元数据，优先使用
            if (_cachedMetadata != null)
            {
                videoInfo.Duration = _cachedMetadata.Duration;
                videoInfo.HasVideo = _cachedMetadata.VideoTrackCount > 0;
                videoInfo.HasAudio = _cachedMetadata.AudioTrackCount > 0;
                videoInfo.VideoTrackCount = _cachedMetadata.VideoTrackCount;
                videoInfo.AudioTrackCount = _cachedMetadata.AudioTrackCount;
                videoInfo.IsSeekable = false; // 加密视频通常是可寻址的
            }
            
            // 从播放器获取实时信息（如果可用）
            if (_player?.Media != null)
            {
                videoInfo.Position = _player.Time;
                videoInfo.Volume = _player.Volume;
                
                // 如果没有缓存的元数据，从播放器获取
                if (_cachedMetadata == null)
                {
                    videoInfo.Duration = _player.Length;
                    videoInfo.IsSeekable = _player.IsSeekable;
                    videoInfo.HasVideo = _player.VideoTrackCount > 0;
                    videoInfo.HasAudio = _player.AudioTrackCount > 0;
                    videoInfo.VideoTrackCount = _player.VideoTrackCount;
                    videoInfo.AudioTrackCount = _player.AudioTrackCount;
                }
            }
            
            return videoInfo;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// 获取详细的视频元数据（如果可用）
    /// </summary>
    public VideoMetadata? GetDetailedMetadata()
    {
        return _cachedMetadata;
    }
    
    /// <summary>
    /// 获取MediaPlayer实例（用于UI绑定）
    /// </summary>
    public MediaPlayer? GetMediaPlayer()
    {
        return _player;
    }
    
    /// <summary>
    /// 清理当前媒体资源
    /// </summary>
    public void CleanupCurrentMedia()
    {
        // 释放媒体资源
        if (_currentMedia != null)
        {
            _currentMedia.Dispose();
            _currentMedia = null;
        }
        // 释放解密器资源
        if (_decryptor != null)
        {
            _decryptor.Dispose();
            _decryptor = null;
        }
        _player.Stop();
        _player.Media?.Dispose();
        _seekableMemoryMediaInput?.Dispose();
        // 清空引用
        _currentPassword = null;
        _cachedMetadata = null;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
        
            // 清理媒体资源
            CleanupCurrentMedia();
        
            // 释放播放器和LibVLC实例
            if (_player != null)
            {
                _player.Dispose();
            }
        
            // 注意：不要释放LibVLC，因为它是静态初始化的
            _disposed = true;
        }
    }

}

/// <summary>
/// 播放状态
/// </summary>
public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
    Ended,
    Error
}

/// <summary>
/// 视频信息
/// </summary>
public class VideoInfo
{
    public long Duration { get; set; }
    public long Position { get; set; }
    public int Volume { get; set; }
    public bool IsSeekable { get; set; }
    public bool HasVideo { get; set; }
    public bool HasAudio { get; set; }
    public int VideoTrackCount { get; set; }
    public int AudioTrackCount { get; set; }
}

/// <summary>
/// 播放状态变化事件参数
/// </summary>
public class PlaybackStateChangedEventArgs : EventArgs
{
    public PlaybackState State { get; }
    
    public PlaybackStateChangedEventArgs(PlaybackState state)
    {
        State = state;
    }
}

/// <summary>
/// 时间变化事件参数
/// </summary>
public class TimeChangedEventArgs : EventArgs
{
    public long Time { get; }
    
    public TimeChangedEventArgs(long time)
    {
        Time = time;
    }
}

/// <summary>
/// 位置变化事件参数
/// </summary>
public class PositionChangedEventArgs : EventArgs
{
    public float Position { get; }
    
    public PositionChangedEventArgs(float position)
    {
        Position = position;
    }
}

/// <summary>
/// 长度变化事件参数
/// </summary>
public class LengthChangedEventArgs : EventArgs
{
    public long Length { get; }
    
    public LengthChangedEventArgs(long length)
    {
        Length = length;
    }
}