namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 视频轨道和容器的只读描述模型。
/// </summary>
/// <remarks>
/// 该模型用于把 LibVLC 的解析结果传递给界面，不参与 SECVID03 的磁盘序列化，
/// 因而新增展示字段不会改变加密文件格式或固定头认证结果。
/// </remarks>
public sealed class VideoMetadata
{
    public long Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public int VideoTrackCount { get; set; }
    public int AudioTrackCount { get; set; }
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string OriginalFormat { get; set; } = string.Empty;
}

/// <summary>
/// 流式加密过程向界面报告的进度快照。
/// </summary>
/// <remarks>
/// 进度以已经完成认证加密并写出的原视频字节计算，而不是以物理容器大小计算，
/// 因此不会把 64 KiB 公开区和每块 16 字节标签计入用户看到的视频处理进度。
/// </remarks>
public sealed class EncryptionProgress
{
    public long ProcessedBytes { get; set; }
    public long TotalBytes { get; set; }
    public double Percentage { get; set; }
    public string Status { get; set; } = string.Empty;
}
