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
    /// UP 主名称（由 API 解析时填充）。
    /// <para>
    /// 设计思考：放在 Collection 级别而非 Item 级别，因为同一解析批次的所有视频
    /// 共享同一 UP 主（单视频/多P/合集均如此），避免每个 Item 冗余存储。
    /// 番剧场景下此字段为空字符串（番剧无 UP 主概念）。
    /// </para>
    /// </summary>
    public string UpName { get; set; } = string.Empty;

    /// <summary>
    /// 发布时间（由 API 的 pubdate 字段转换，Unix 时间戳 → DateTime）。
    /// 番剧或无数据时为 null。命名模板中 {date} 变量消费此字段。
    /// </summary>
    public DateTime? PublishDate { get; set; }

    /// <summary>
    /// 视频列表（单视频 Count=1）
    /// </summary>
    public List<BiliVideoItem> Items { get; set; } = new();
}
