using LibVLCSharp.Shared;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 基于“认证随机读取流”的 SECVID03 安全视频播放器。
/// </summary>
/// <remarks>
/// 播放链路固定为 SECVID03 → <see cref="SeekableEncryptedVideoStream"/> →
/// <see cref="SeekableStreamMediaInput"/> → LibVLC Media → Avalonia VideoView。
/// 该类不持有完整视频明文，只管理当前 Media、MediaInput 及 LibVLC 对象的生命周期。
/// </remarks>
public sealed class SecureVideoPlayer : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private Media? _currentMedia;
    private SeekableStreamMediaInput? _mediaInput;
    private bool _disposed;

    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;
    public event EventHandler<TimeChangedEventArgs>? TimeChanged;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    public event EventHandler<LengthChangedEventArgs>? LengthChanged;
    public event EventHandler<string>? ErrorOccurred;

    public SecureVideoPlayer()
    {
        // 必须先用插件内的绝对路径初始化 Core，再创建任何 LibVLC/MediaPlayer 实例。
        LibVlcRuntime.EnsureInitialized();
        _libVlc = new LibVLC();
        _player = new MediaPlayer(_libVlc);
        SubscribeToPlayerEvents();
    }

    private void SubscribeToPlayerEvents()
    {
        _player.Playing += (_, _) => PlaybackStateChanged?.Invoke(this, new(PlaybackState.Playing));
        _player.Paused += (_, _) => PlaybackStateChanged?.Invoke(this, new(PlaybackState.Paused));
        _player.Stopped += (_, _) => PlaybackStateChanged?.Invoke(this, new(PlaybackState.Stopped));
        _player.EndReached += (_, _) => PlaybackStateChanged?.Invoke(this, new(PlaybackState.Ended));
        _player.TimeChanged += (_, e) => TimeChanged?.Invoke(this, new(e.Time));
        _player.PositionChanged += (_, e) => PositionChanged?.Invoke(this, new(e.Position));
        _player.LengthChanged += (_, e) => LengthChanged?.Invoke(this, new(e.Length));
        _player.EncounteredError += (_, _) =>
        {
            // 原生事件本身不携带托管解密异常，优先转发 MediaInput 保存的认证/读取错误。
            var detail = _mediaInput?.LastError?.Message;
            ErrorOccurred?.Invoke(this, detail is null ? "播放失败。" : $"播放失败: {detail}");
            PlaybackStateChanged?.Invoke(this, new(PlaybackState.Error));
        };
    }

    /// <summary>
    /// 验证 SECVID03 密码并把随机读取媒体绑定到播放器，保留原有公共方法签名。
    /// </summary>
    /// <remarks>
    /// 此处的“加载”只包含 PBKDF2、固定头认证和 LibVLC 媒体解析，不执行完整视频解密。
    /// 因此首帧等待时间和内存占用不会随视频总大小线性增长。
    /// </remarks>
    public async Task<bool> LoadEncryptedVideoAsync(string filePath, string password)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!File.Exists(filePath))
        {
            ErrorOccurred?.Invoke(this, "文件不存在。");
            return false;
        }

        // 先释放旧 Media/Input，保证重复切换视频时不会留下文件句柄或把回调发送到旧流。
        CleanupCurrentMedia();
        SeekableStreamMediaInput? newInput = null;
        Media? newMedia = null;

        try
        {
            var stream = SeekableEncryptedVideoStream.Open(filePath, password);
            newInput = new SeekableStreamMediaInput(stream);
            newMedia = new Media(_libVlc, newInput);
            _player.Media = newMedia;
            await newMedia.Parse(MediaParseOptions.ParseLocal);

            _mediaInput = newInput;
            _currentMedia = newMedia;
            return true;
        }
        catch (Exception ex)
        {
            // 创建过程可能在流、MediaInput 或 Media 任一阶段失败；按所有权逆序释放已经创建的对象。
            _player.Media = null;
            newMedia?.Dispose();
            newInput?.Dispose();
            ErrorOccurred?.Invoke(this, $"加载失败: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> Play()
    {
        if (_disposed || _player.Media is null)
        {
            return false;
        }

        try
        {
            if (_currentMedia is { IsParsed: false })
            {
                await _currentMedia.Parse(MediaParseOptions.ParseLocal);
            }

            return _player.Play();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"播放失败: {ex.Message}");
            return false;
        }
    }

    public void Pause()
    {
        if (!_disposed) _player.Pause();
    }

    public void Stop()
    {
        if (!_disposed) _player.Stop();
    }

    public void SetPosition(float position)
    {
        if (!_disposed && _player.Media is not null) _player.Position = Math.Clamp(position, 0, 1);
    }

    public void SetTime(long timeMs)
    {
        if (!_disposed && _player.Media is not null) _player.Time = Math.Max(0, timeMs);
    }

    public bool SetVolume(int volume)
    {
        if (_disposed) return false;
        _player.Volume = Math.Clamp(volume, 0, 100);
        return true;
    }

    public VideoInfo? GetVideoInfo()
    {
        if (_disposed || _player.Media is null) return null;
        return new VideoInfo
        {
            Duration = _player.Length,
            Position = _player.Time,
            Volume = _player.Volume,
            IsSeekable = _player.IsSeekable,
            HasVideo = _player.VideoTrackCount > 0,
            HasAudio = _player.AudioTrackCount > 0,
            VideoTrackCount = _player.VideoTrackCount,
            AudioTrackCount = _player.AudioTrackCount
        };
    }

    public VideoMetadata? GetDetailedMetadata() => null;
    public MediaPlayer GetMediaPlayer() => _player;

    public void CleanupCurrentMedia()
    {
        if (_disposed) return;
        // 释放顺序很重要：先停止原生读取并解除 Media，再销毁 Media，最后由 MediaInput 释放解密流。
        // 如果先关闭流，LibVLC 尚未结束的回调可能在关闭后的文件句柄上读取。
        _player.Stop();
        _player.Media = null;
        _currentMedia?.Dispose();
        _currentMedia = null;
        _mediaInput?.Dispose();
        _mediaInput = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CleanupCurrentMedia();
        _disposed = true;
        _player.Dispose();
        // LibVLC 实例由本播放器独占；Core.Initialize 是进程级初始化，但 LibVLC 对象仍必须正常释放。
        _libVlc.Dispose();
    }
}

/// <summary>播放器对界面公开的简化状态。</summary>
public enum PlaybackState { Stopped, Playing, Paused, Ended, Error }

/// <summary>从当前 LibVLC MediaPlayer 快照得到的播放信息。</summary>
public sealed class VideoInfo
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

public sealed class PlaybackStateChangedEventArgs(PlaybackState state) : EventArgs { public PlaybackState State { get; } = state; }
public sealed class TimeChangedEventArgs(long time) : EventArgs { public long Time { get; } = time; }
public sealed class PositionChangedEventArgs(float position) : EventArgs { public float Position { get; } = position; }
public sealed class LengthChangedEventArgs(long length) : EventArgs { public long Length { get; } = length; }
