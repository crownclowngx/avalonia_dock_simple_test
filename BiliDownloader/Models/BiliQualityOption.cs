namespace BiliDownloader.Models;

/// <summary>
/// 清晰度选项模型
/// </summary>
public class BiliQualityOption
{
    /// <summary>
    /// B站画质标识 ID（如 127=8K, 120=4K, 116=1080P60, 80=1080P, 64=720P, 32=480P, 16=360P）
    /// </summary>
    public int QualityId { get; set; }

    /// <summary>
    /// 显示名称（如 "1080P 高清"）
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}
