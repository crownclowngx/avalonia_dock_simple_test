namespace MySmallTools.Business.SecretVideoPlayer.Playback;

/// <summary>播放器对界面公开的简化状态。</summary>
public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
    Ended,
    Error
}

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

public sealed class PlaybackStateChangedEventArgs(PlaybackState state) : EventArgs
{
    public PlaybackState State { get; } = state;
}

public sealed class TimeChangedEventArgs(long time) : EventArgs
{
    public long Time { get; } = time;
}

public sealed class PositionChangedEventArgs(float position) : EventArgs
{
    public float Position { get; } = position;
}

public sealed class LengthChangedEventArgs(long length) : EventArgs
{
    public long Length { get; } = length;
}

public sealed class SeekableChangedEventArgs(bool isSeekable) : EventArgs
{
    public bool IsSeekable { get; } = isSeekable;
}
