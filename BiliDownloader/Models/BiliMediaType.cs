namespace BiliDownloader.Models;

/// <summary>
/// B站媒体类型枚举（对应不同 API 端点和解析逻辑）
/// </summary>
public enum BiliMediaType
{
    /// <summary>普通视频 (BV/av)</summary>
    Video = 0,

    /// <summary>番剧 (ep/ss/md)</summary>
    Bangumi = 1,
}
