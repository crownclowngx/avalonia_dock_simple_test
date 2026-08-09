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

    /// <summary>平台字幕轨 ID；只用于同语言确定性去重，不作为跨视频语言选择键。</summary>
    public string TrackId { get; set; } = "";

    /// <summary>由 API 结构化字段映射出的来源类型；不得仅解析 LanDoc 文案。</summary>
    public SubtitleSourceType SourceType { get; set; }

    /// <summary>转为不携带下载地址的安全描述；需要执行下载时显式传 true。</summary>
    public SubtitleTrackDescriptor ToDescriptor(bool includeDownloadUrl = false) => new(
        Lan.Trim(),
        string.IsNullOrWhiteSpace(LanDoc) ? Lan.Trim() : LanDoc.Trim(),
        SourceType,
        TrackId,
        includeDownloadUrl ? SubtitleUrl : string.Empty);
}
