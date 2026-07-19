namespace BiliDownloader.Models;

/// <summary>
/// 字幕列表项（从 B站 player API 获取）
/// </summary>
public class SubtitleListItem
{
    /// <summary>语言代码（如 "zh-CN", "en"）</summary>
    public string Lan { get; set; } = "";

    /// <summary>语言显示名（如 "中文（自动生成）"）</summary>
    public string LanDoc { get; set; } = "";

    /// <summary>字幕下载 URL（已补充 https: 协议头）</summary>
    public string SubtitleUrl { get; set; } = "";
}
