namespace BiliDownloader.Models;

/// <summary>
/// 视频集合模型（统一单视频和列表/番剧多集）
/// </summary>
public class BiliVideoCollection
{
    /// <summary>
    /// 系列/番剧总标题
    /// </summary>
    public string SeriesTitle { get; set; } = string.Empty;

    /// <summary>
    /// 封面图 URL
    /// </summary>
    public string Cover { get; set; } = string.Empty;

    /// <summary>
    /// 视频列表（单视频 Count=1）
    /// </summary>
    public List<BiliVideoItem> Items { get; set; } = new();
}
