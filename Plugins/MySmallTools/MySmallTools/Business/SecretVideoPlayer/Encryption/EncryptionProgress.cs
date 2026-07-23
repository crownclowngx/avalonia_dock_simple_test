namespace MySmallTools.Business.SecretVideoPlayer.Encryption;

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
