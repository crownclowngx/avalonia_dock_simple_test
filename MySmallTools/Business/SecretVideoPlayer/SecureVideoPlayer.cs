using LibVLCSharp.Shared;
using System.Security.Cryptography;
using System.Linq;

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
    private Media? _currentMedia;
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
                ErrorOccurred?.Invoke(this, "初始化LibVLC Core...");
                Core.Initialize();
                _isLibVlcInitialized = true;
                ErrorOccurred?.Invoke(this, "LibVLC Core初始化完成");
            }
            else
            {
                ErrorOccurred?.Invoke(this, "LibVLC Core已初始化，跳过");
            }
            
            ErrorOccurred?.Invoke(this, "创建LibVLC实例...");
            _libVLC = new LibVLC();
            ErrorOccurred?.Invoke(this, $"LibVLC版本: {_libVLC.Version}");
            
            ErrorOccurred?.Invoke(this, "创建MediaPlayer实例...");
            _player = new MediaPlayer(_libVLC);
            ErrorOccurred?.Invoke(this, "MediaPlayer创建完成");
            
            ErrorOccurred?.Invoke(this, "初始化加密器...");
            _encryptor = new SmartVideoEncryptor();
            
            ErrorOccurred?.Invoke(this, "订阅播放器事件...");
            // 订阅播放器事件
            SubscribeToPlayerEvents();
            
            ErrorOccurred?.Invoke(this, "SecureVideoPlayer初始化完成");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"SecureVideoPlayer初始化失败: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"异常堆栈: {ex.StackTrace}");
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
            ErrorOccurred?.Invoke(this, "事件: 播放开始");
            PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Playing));
        };
        
        _player.Paused += (s, e) => 
        {
            ErrorOccurred?.Invoke(this, "事件: 播放暂停");
            PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Paused));
        };
        
        _player.Stopped += (s, e) => 
        {
            ErrorOccurred?.Invoke(this, "事件: 播放停止");
            PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(PlaybackState.Stopped));
        };
        
        _player.EndReached += (s, e) => 
        {
            ErrorOccurred?.Invoke(this, "事件: 播放结束");
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
        
        _player.EncounteredError += (s, e) => 
        {
            ErrorOccurred?.Invoke(this, "事件: 播放过程中发生错误");
        };
        
        // 添加更多调试事件
        _player.Opening += (s, e) => 
        {
            ErrorOccurred?.Invoke(this, "事件: 媒体正在打开");
        };
        
        _player.Buffering += (s, e) => 
        {
            ErrorOccurred?.Invoke(this, $"事件: 缓冲中 - {e.Cache}%");
        };
        
        _player.MediaChanged += (s, e) => 
        {
            ErrorOccurred?.Invoke(this, "事件: 媒体已更改");
        };
        
        _player.NothingSpecial += (s, e) => 
        {
            ErrorOccurred?.Invoke(this, "事件: 无特殊状态");
        };
        
        _player.ESAdded += (s, e) => 
        {
            ErrorOccurred?.Invoke(this, $"事件: 添加了基本流 - ID: {e.Id}, 类型: {e.Type}");
        };
        
        _player.ESDeleted += (s, e) => 
        {
            ErrorOccurred?.Invoke(this, $"事件: 删除了基本流 - ID: {e.Id}, 类型: {e.Type}");
        };
    }
    
    /// <summary>
    /// 加载加密视频文件
    /// </summary>
    public async Task<bool> LoadEncryptedVideoAsync(string filePath, string password)
    {
        try
        {
            ErrorOccurred?.Invoke(this, $"开始加载加密视频: {filePath}");
            
            if (_disposed) throw new ObjectDisposedException(nameof(SecureVideoPlayer));
            
            // 检查文件是否存在
            if (!File.Exists(filePath))
            {
                ErrorOccurred?.Invoke(this, $"文件不存在: {filePath}");
                return false;
            }
            
            ErrorOccurred?.Invoke(this, "清理之前的资源...");
            // 清理之前的资源
            CleanupCurrentMedia();
            
            ErrorOccurred?.Invoke(this, "验证文件是否为加密视频...");
            // 验证文件是否为加密视频并获取视频信息
            if (!_encryptor.IsEncryptedVideo(filePath))
            {
                ErrorOccurred?.Invoke(this, "文件不是有效的加密视频文件");
                return false;
            }
            
            ErrorOccurred?.Invoke(this, "读取加密视频信息...");
            // 获取加密视频信息
            var videoInfo = _encryptor.GetEncryptedVideoInfo(filePath);
            if (videoInfo == null)
            {
                ErrorOccurred?.Invoke(this, "无法读取加密视频信息");
                return false;
            }
            
            // 验证密码
            ErrorOccurred?.Invoke(this, "验证密码...");
            var key = GenerateDecryptionKey(password, videoInfo);
            using var sha256 = SHA256.Create();
            var keyHash = sha256.ComputeHash(key);
            
            if (!keyHash.SequenceEqual(videoInfo.KeyHash))
            {
                ErrorOccurred?.Invoke(this, "密码错误");
                return false;
            }
            
            // 设置缓存的元数据（从加密文件头中获取）
            if (videoInfo.HasMetadata)
            {
                _cachedMetadata = videoInfo.Metadata;
                ErrorOccurred?.Invoke(this, "已加载视频元数据");
            }
            
            ErrorOccurred?.Invoke(this, "开始完整解密视频到内存...");
            // 回到完整内存解密方案进行测试
            var decryptor = new FullVideoDecryptor(filePath, password);
            var success = await decryptor.DecryptVideoAsync();
            
            if (!success || decryptor.DecryptedStream == null)
            {
                ErrorOccurred?.Invoke(this, "视频解密失败");
                return false;
            }
            
            var decryptedStream = decryptor.DecryptedStream;
            
            ErrorOccurred?.Invoke(this, "估算视频比特率...");
            // 智能估算比特率以优化缓冲块大小
            var estimatedBitrate = VideoBitrateEstimator.SmartEstimate(decryptedStream, filePath);
            ErrorOccurred?.Invoke(this, $"估算比特率: {estimatedBitrate / 1000}Kbps");
            
            ErrorOccurred?.Invoke(this, "创建优化的分块缓冲媒体输入...");
            // 使用优化的分块缓冲策略：专门针对LibVLC在可寻址模式下的随机访问优化
            var chunkedInput = new OptimizedChunkedBufferMediaInput(decryptedStream, estimatedBitrate);
            
            ErrorOccurred?.Invoke(this, "创建LibVLC媒体对象...");
            _currentMedia = new Media(_libVLC, chunkedInput);
            ErrorOccurred?.Invoke(this, $"媒体对象创建成功，状态: {_currentMedia.State}");
            
            // 添加媒体状态变化监听
            _currentMedia.StateChanged += (sender, e) =>
            {
                ErrorOccurred?.Invoke(this, $"媒体状态变化: {e.State}");
            };
            
            ErrorOccurred?.Invoke(this, "设置媒体到播放器...");
            // 设置媒体到播放器
            _player.Media = _currentMedia;
            ErrorOccurred?.Invoke(this, "媒体设置完成");
            
            // 尝试解析媒体
            ErrorOccurred?.Invoke(this, "开始解析媒体...");
            _currentMedia.Parse(MediaParseOptions.ParseLocal);
            
            // 等待媒体解析完成
            ErrorOccurred?.Invoke(this, "等待媒体解析...");
            var parseTimeout = TimeSpan.FromSeconds(10);
            var parseStart = DateTime.Now;
            
            while (!_currentMedia.IsParsed && DateTime.Now - parseStart < parseTimeout)
            {
                await Task.Delay(100);
                ErrorOccurred?.Invoke(this, $"解析中... 状态: {_currentMedia.State}, 已解析: {_currentMedia.IsParsed}");
            }
            
            ErrorOccurred?.Invoke(this, $"媒体解析完成: {_currentMedia.IsParsed}");
            ErrorOccurred?.Invoke(this, $"解析状态: {_currentMedia.ParsedStatus}");
            ErrorOccurred?.Invoke(this, $"媒体状态: {_currentMedia.State}");
            ErrorOccurred?.Invoke(this, $"播放器状态: {_player.State}");
            ErrorOccurred?.Invoke(this, $"媒体时长: {_currentMedia.Duration}ms");
            
            _currentPassword = password;
            
            ErrorOccurred?.Invoke(this, "视频加载完成");
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            ErrorOccurred?.Invoke(this, $"密码错误，无法解密视频文件: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"加载视频文件失败: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"异常堆栈: {ex.StackTrace}");
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
    public async Task<bool> Play()
    {
        if (_disposed)
        {
            ErrorOccurred?.Invoke(this, "播放器已被释放，无法播放");
            return false;
        }
        
        if (_player == null)
        {
            ErrorOccurred?.Invoke(this, "MediaPlayer未初始化");
            return false;
        }
        
        if (_player.Media == null)
        {
            ErrorOccurred?.Invoke(this, "未加载媒体文件，请先调用LoadVideoAsync");
            return false;
        }
        
        try
        {
            ErrorOccurred?.Invoke(this, $"当前播放器状态: {_player.State}");
            
            // 检查媒体是否已解析
            if (_currentMedia != null && !_currentMedia.IsParsed)
            {
                ErrorOccurred?.Invoke(this, "媒体尚未解析，尝试重新解析...");
                _currentMedia.Parse(MediaParseOptions.ParseLocal);
                
                // 等待解析完成
                var parseStart = DateTime.Now;
                while (!_currentMedia.IsParsed && DateTime.Now - parseStart < TimeSpan.FromSeconds(5))
                {
                    await Task.Delay(100);
                }
                
                ErrorOccurred?.Invoke(this, $"重新解析结果: {_currentMedia.IsParsed}");
            }
            
            ErrorOccurred?.Invoke(this, "开始播放...");
            var result = _player.Play();
            ErrorOccurred?.Invoke(this, $"播放命令返回结果: {result}");
            
            // 监控播放状态
            Task.Run(async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(1000);
                    var newState = _player.State;
                    ErrorOccurred?.Invoke(this, $"播放{i+1}秒后状态: {newState}");
                    
                    if (newState == VLCState.Playing)
                    {
                        ErrorOccurred?.Invoke(this, "播放成功开始！");
                        break;
                    }
                    else if (newState == VLCState.Error)
                    {
                        ErrorOccurred?.Invoke(this, "播放器进入错误状态");
                        break;
                    }
                    else if (newState == VLCState.Ended)
                    {
                        ErrorOccurred?.Invoke(this, "播放已结束");
                        break;
                    }
                }
            });
            
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
    /// 获取当前播放状态
    /// </summary>
    public PlaybackState GetPlaybackState()
    {
        if (_disposed || _player == null) return PlaybackState.Stopped;
        
        return _player.State switch
        {
            VLCState.Playing => PlaybackState.Playing,
            VLCState.Paused => PlaybackState.Paused,
            VLCState.Stopped => PlaybackState.Stopped,
            VLCState.Ended => PlaybackState.Ended,
            VLCState.Error => PlaybackState.Error,
            _ => PlaybackState.Stopped
        };
    }
    public VideoInfo _videoInfo { get; private set; } 
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
                videoInfo.IsSeekable = true; // 加密视频通常是可寻址的
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
                    videoInfo.IsSeekable = true;
                    videoInfo.HasVideo = _player.VideoTrackCount > 0;
                    videoInfo.HasAudio = _player.AudioTrackCount > 0;
                    videoInfo.VideoTrackCount = _player.VideoTrackCount;
                    videoInfo.AudioTrackCount = _player.AudioTrackCount;
                }
            }
            _videoInfo = videoInfo;
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
    /// 测试原始文件播放性能（用于对比）
    /// </summary>
    public async Task<bool> LoadUnencryptedVideoForTestAsync(string filePath)
    {
        try
        {
            ErrorOccurred?.Invoke(this, "=== 开始测试原始文件播放性能 ===");
            
            if (!File.Exists(filePath))
            {
                ErrorOccurred?.Invoke(this, "测试文件不存在");
                return false;
            }
            
            ErrorOccurred?.Invoke(this, "清理当前媒体...");
            CleanupCurrentMedia();
            
            ErrorOccurred?.Invoke(this, "创建原始文件媒体对象...");
            _currentMedia = new Media(_libVLC, filePath);
            
            ErrorOccurred?.Invoke(this, "设置媒体到播放器...");
            _player.Media = _currentMedia;
            
            ErrorOccurred?.Invoke(this, "开始解析媒体...");
            _currentMedia.Parse(MediaParseOptions.ParseLocal);
            
            // 等待媒体解析完成
            var parseTimeout = TimeSpan.FromSeconds(10);
            var parseStart = DateTime.Now;
            
            while (!_currentMedia.IsParsed && DateTime.Now - parseStart < parseTimeout)
            {
                await Task.Delay(100);
                ErrorOccurred?.Invoke(this, $"解析中... 状态: {_currentMedia.State}");
            }
            
            if (_currentMedia.IsParsed)
            {
                ErrorOccurred?.Invoke(this, $"原始文件媒体解析成功，时长: {_currentMedia.Duration}ms");
                ErrorOccurred?.Invoke(this, "=== 原始文件播放测试准备完成 ===");
                return true;
            }
            else
            {
                ErrorOccurred?.Invoke(this, "原始文件媒体解析超时");
                return false;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"测试原始文件播放失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 诊断播放器状态 - 用于排查播放问题
    /// </summary>
    public void DiagnosePlayerStatus()
    {
        try
        {
            ErrorOccurred?.Invoke(this, "=== 播放器状态诊断开始 ===");
            
            // 检查对象状态
            ErrorOccurred?.Invoke(this, $"播放器是否已释放: {_disposed}");
            ErrorOccurred?.Invoke(this, $"LibVLC是否为null: {_libVLC == null}");
            ErrorOccurred?.Invoke(this, $"MediaPlayer是否为null: {_player == null}");
            
            if (_libVLC != null)
            {
                ErrorOccurred?.Invoke(this, $"LibVLC版本: {_libVLC.Version}");
                ErrorOccurred?.Invoke(this, $"LibVLC变更集: {_libVLC.Changeset}");
            }
            
            if (_player != null)
            {
                ErrorOccurred?.Invoke(this, $"MediaPlayer状态: {_player.State}");
                ErrorOccurred?.Invoke(this, $"MediaPlayer是否可播放: {_player.IsPlaying}");
                ErrorOccurred?.Invoke(this, $"MediaPlayer是否可寻址: {_player.IsSeekable}");
                ErrorOccurred?.Invoke(this, $"MediaPlayer音量: {_player.Volume}");
                ErrorOccurred?.Invoke(this, $"MediaPlayer时长: {_player.Length}ms");
                ErrorOccurred?.Invoke(this, $"MediaPlayer当前时间: {_player.Time}ms");
                ErrorOccurred?.Invoke(this, $"MediaPlayer位置: {_player.Position}");
                ErrorOccurred?.Invoke(this, $"MediaPlayer视频轨道数: {_player.VideoTrackCount}");
                ErrorOccurred?.Invoke(this, $"MediaPlayer音频轨道数: {_player.AudioTrackCount}");
            }
            
            // 检查媒体状态
            ErrorOccurred?.Invoke(this, $"当前媒体是否为null: {_currentMedia == null}");
            if (_currentMedia != null)
            {
                ErrorOccurred?.Invoke(this, $"媒体状态: {_currentMedia.State}");
                ErrorOccurred?.Invoke(this, $"媒体持续时间: {_currentMedia.Duration}ms");
                ErrorOccurred?.Invoke(this, $"媒体是否已解析: {_currentMedia.IsParsed}");
                ErrorOccurred?.Invoke(this, $"媒体子项数量: {_currentMedia.SubItems.Count}");
            }
            
            // 检查流式解密状态
            ErrorOccurred?.Invoke(this, $"使用流式解密方案");
            
            // 检查元数据
            ErrorOccurred?.Invoke(this, $"缓存元数据是否为null: {_cachedMetadata == null}");
            if (_cachedMetadata != null)
            {
                ErrorOccurred?.Invoke(this, $"元数据视频轨道: {_cachedMetadata.VideoTrackCount}");
                ErrorOccurred?.Invoke(this, $"元数据音频轨道: {_cachedMetadata.AudioTrackCount}");
                ErrorOccurred?.Invoke(this, $"元数据时长: {_cachedMetadata.Duration}ms");
            }
            
            ErrorOccurred?.Invoke(this, "=== 播放器状态诊断结束 ===");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"诊断过程中发生错误: {ex.Message}");
        }
    }
    

    
    /// <summary>
    /// 清理当前媒体资源
    /// </summary>
    private void CleanupCurrentMedia()
    {
        _currentMedia?.Dispose();
        _currentMedia = null;
        

        
        _currentPassword = null;
        _cachedMetadata = null;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            CleanupCurrentMedia();
            
            _player?.Dispose();
            _libVLC?.Dispose();
            
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