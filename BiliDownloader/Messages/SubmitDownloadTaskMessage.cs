namespace BiliDownloader.Messages;

/// <summary>
/// 下载项基础信息（Document 解析后传递给调度器）
/// </summary>
public class DownloadItemInfo
{
    public string ItemId { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public long Aid { get; set; }
    public string Bvid { get; set; } = string.Empty;
    public long Cid { get; set; }
    public int Duration { get; set; }
}

/// <summary>
/// Document -> Tool：提交下载任务消息
/// </summary>
public class SubmitDownloadTaskMessage
{
    /// <summary>
    /// 发起方 Document 实例 ID（用于定向回传进度）
    /// </summary>
    public string SourceDocumentId { get; }

    /// <summary>
    /// 系列标题（用于文件夹命名）
    /// </summary>
    public string SeriesTitle { get; }

    /// <summary>
    /// 要下载的视频列表
    /// </summary>
    public List<DownloadItemInfo> Items { get; }

    /// <summary>
    /// 用户选择的清晰度 QualityId
    /// </summary>
    public int QualityId { get; }

    /// <summary>
    /// 输出目录
    /// </summary>
    public string OutputDirectory { get; }

    /// <summary>
    /// 当前 Cookie（下载时需要）
    /// </summary>
    public string Cookie { get; }

    public SubmitDownloadTaskMessage(
        string sourceDocumentId,
        string seriesTitle,
        List<DownloadItemInfo> items,
        int qualityId,
        string outputDirectory,
        string cookie)
    {
        SourceDocumentId = sourceDocumentId;
        SeriesTitle = seriesTitle;
        Items = items;
        QualityId = qualityId;
        OutputDirectory = outputDirectory;
        Cookie = cookie;
    }
}
