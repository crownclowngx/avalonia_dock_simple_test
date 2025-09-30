using LibVLCSharp.Shared;
using System.Security.Cryptography;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 安全视频播放器 - 核心控制器，集成LibVLC和加密解密功能
/// </summary>
public class SecureVideoPlayer : IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    private readonly SmartVideoEncryptor _encryptor;
    private readonly MultiLevelVideoBuffer _buffer;
    private static bool _isLibVlcInitialized = false;
    
    private BufferedAesCtrStream? _decryptStream;
    private FileStream? _encryptedFileStream;
    private Media? _currentMedia;
    private string? _currentPassword;
    private bool _disposed;
    
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
        // 确保LibVLC只初始化一次
        if (!_isLibVlcInitialized)
        {
            Core.Initialize();
            _isLibVlcInitialized = true;
        }
        _libVLC = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVLC);
        
        _encryptor = new SmartVideoEncryptor();
        _buffer = new MultiLevelVideoBuffer(1024 * 1024, 15); // 1MB块，15个缓存
        
        // 订阅播放器事件
        SubscribeToPlayerEvents();
        
        // 启动统计更新定时器
        var statisticsTimer = new Timer(UpdateStatistics, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }
    
    /// <summary>
    /// 订阅播放器事件
    /// </summary>
    private void SubscribeToPlayerEvents()
    {
        _mediaPlayer.Playing += (s, e) => PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Playing));
        _mediaPlayer.Paused += (s, e) => PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Paused));
        _mediaPlayer.Stopped += (s, e) => PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Stopped));
        _mediaPlayer.EndReached += (s, e) => PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Ended));
        
        _mediaPlayer.TimeChanged += (s, e) => TimeChanged?.Invoke(this, new TimeChangedEventArgs(e.Time));
        _mediaPlayer.PositionChanged += (s, e) => PositionChanged?.Invoke(this, new PositionChangedEventArgs(e.Position));
        _mediaPlayer.LengthChanged += (s, e) => LengthChanged?.Invoke(this, new LengthChangedEventArgs(e.Length));
        
        _mediaPlayer.EncounteredError += (s, e) => ErrorOccurred?.Invoke(this, "播放过程中发生错误");
    }
    
    /// <summary>
    /// 加载加密视频文件
    /// </summary>
    public async Task<bool> LoadEncryptedVideoAsync(string filePath, string password)
    {
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SecureVideoPlayer));
            
            // 清理之前的资源
            CleanupCurrentMedia();
            
            // 验证文件是否为加密视频
            if (!_encryptor.IsEncryptedVideo(filePath))
            {
                ErrorOccurred?.Invoke(this, "文件不是有效的加密视频文件");
                return false;
            }
            
            // 打开加密文件
            _encryptedFileStream = File.OpenRead(filePath);
            
            // 获取加密信息
            var videoInfo = _encryptor.GetEncryptedVideoInfo(_encryptedFileStream);
            
            // 生成解密密钥
            var key = GenerateDecryptionKey(password, videoInfo);
            
            // 创建解密流
            _decryptStream = new BufferedAesCtrStream(_encryptedFileStream, key, videoInfo, _buffer);
            
            // 创建LibVLC媒体
            _currentMedia = new Media(_libVLC, new StreamMediaInput(_decryptStream));
            
            // 设置媒体到播放器
            _mediaPlayer.Media = _currentMedia;
            
            _currentPassword = password;
            
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            ErrorOccurred?.Invoke(this, "密码错误，无法解密视频文件");
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"加载视频文件失败: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 生成解密密钥
    /// </summary>
    private byte[] GenerateDecryptionKey(string password, EncryptedVideoInfo videoInfo)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, 
            System.Text.Encoding.UTF8.GetBytes("SecretVideoSalt2024"), 10000, HashAlgorithmName.SHA256);
        
        return pbkdf2.GetBytes(32); // AES-256
    }
    
    /// <summary>
    /// 播放视频
    /// </summary>
    public bool Play()
    {
        if (_disposed || _mediaPlayer.Media == null) return false;
        
        try
        {
            return _mediaPlayer.Play();
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
        if (_disposed) return;
        
        try
        {
            _mediaPlayer.Pause();
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
        if (_disposed) return;
        
        try
        {
            _mediaPlayer.Stop();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"停止失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 设置播放位置（0.0 - 1.0）
    /// </summary>
    public async Task<bool> SetPositionAsync(float position)
    {
        if (_disposed || _mediaPlayer.Media == null) return false;
        
        try
        {
            // 预加载目标位置附近的数据
            if (_decryptStream != null)
            {
                var targetPosition = (long)(position * _decryptStream.Length);
                var preloadStart = Math.Max(0, targetPosition - 1024 * 1024); // 前1MB
                var preloadEnd = Math.Min(_decryptStream.Length, targetPosition + 2 * 1024 * 1024); // 后2MB
                
                await _decryptStream.PreloadRangeAsync(preloadStart, preloadEnd);
            }
            
            _mediaPlayer.Position = position;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"设置播放位置失败: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 设置播放时间（毫秒）
    /// </summary>
    public async Task<bool> SetTimeAsync(long timeMs)
    {
        if (_disposed || _mediaPlayer.Media == null) return false;
        
        try
        {
            // 计算相对位置
            var length = _mediaPlayer.Length;
            if (length > 0)
            {
                var position = (float)timeMs / length;
                return await SetPositionAsync(position);
            }
            
            _mediaPlayer.Time = timeMs;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"设置播放时间失败: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 设置音量（0-100）
    /// </summary>
    public bool SetVolume(int volume)
    {
        if (_disposed) return false;
        
        try
        {
            volume = Math.Clamp(volume, 0, 100);
            _mediaPlayer.Volume = volume;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"设置音量失败: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 获取当前播放状态
    /// </summary>
    public PlaybackState GetPlaybackState()
    {
        if (_disposed) return PlaybackState.Stopped;
        
        return _mediaPlayer.State switch
        {
            VLCState.Playing => PlaybackState.Playing,
            VLCState.Paused => PlaybackState.Paused,
            VLCState.Stopped => PlaybackState.Stopped,
            VLCState.Ended => PlaybackState.Ended,
            VLCState.Error => PlaybackState.Error,
            _ => PlaybackState.Stopped
        };
    }
    
    /// <summary>
    /// 获取视频信息
    /// </summary>
    public VideoInfo? GetVideoInfo()
    {
        if (_disposed || _mediaPlayer.Media == null) return null;
        
        try
        {
            return new VideoInfo
            {
                Duration = _mediaPlayer.Length,
                Position = _mediaPlayer.Time,
                Volume = _mediaPlayer.Volume,
                IsSeekable = _mediaPlayer.IsSeekable,
                HasVideo = _mediaPlayer.VideoTrackCount > 0,
                HasAudio = _mediaPlayer.AudioTrackCount > 0,
                VideoTrackCount = _mediaPlayer.VideoTrackCount,
                AudioTrackCount = _mediaPlayer.AudioTrackCount
            };
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// 获取MediaPlayer实例（用于UI绑定）
    /// </summary>
    public MediaPlayer GetMediaPlayer()
    {
        return _mediaPlayer;
    }
    
    /// <summary>
    /// 更新缓冲统计信息
    /// </summary>
    private void UpdateStatistics(object? state)
    {
        if (_disposed || _buffer == null) return;
        
        try
        {
            var statistics = _buffer.GetStatistics();
            BufferStatisticsUpdated?.Invoke(this, statistics);
        }
        catch
        {
            // 忽略统计更新错误
        }
    }
    
    /// <summary>
    /// 清理当前媒体资源
    /// </summary>
    private void CleanupCurrentMedia()
    {
        _currentMedia?.Dispose();
        _currentMedia = null;
        
        _decryptStream?.Dispose();
        _decryptStream = null;
        
        _encryptedFileStream?.Dispose();
        _encryptedFileStream = null;
        
        _buffer?.Clear();
        _currentPassword = null;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            CleanupCurrentMedia();
            
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
            _buffer?.Dispose();
            
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