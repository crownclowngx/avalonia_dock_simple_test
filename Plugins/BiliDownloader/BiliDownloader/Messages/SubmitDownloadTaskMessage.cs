using BiliDownloader.Models;
using BiliDownloader.Services.Download.Extras;

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

    /// <summary>媒体类型</summary>
    public BiliMediaType MediaType { get; set; } = BiliMediaType.Video;

    /// <summary>番剧 ep_id</summary>
    public long EpId { get; set; }

    /// <summary>番剧 season_id</summary>
    public long SeasonId { get; set; }

    /// <summary>封面图 URL</summary>
    public string CoverUrl { get; set; } = string.Empty;
}

/// <summary>
/// Document -> Tool：提交下载任务消息
/// </summary>
public class SubmitDownloadTaskMessage
{
    public DownloadSubmission Submission { get; }
    /// <summary>
    /// 发起方 Document 实例 ID（用于定向回传进度）
    /// </summary>
    public string SourceDocumentId { get; }
    public string SourceDocumentTitle { get; }

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
    /// 用户选择的音频流 ID（0 表示使用最高码率）
    /// </summary>
    public int AudioQualityId { get; }

    /// <summary>
    /// 输出目录
    /// </summary>
    public string OutputDirectory { get; }

    /// <summary>
    /// 是否使用分组文件夹（以视频组名称命名子文件夹）
    /// </summary>
    public bool UseGroupFolder { get; }

    /// <summary>
    /// 启用的附加资源类型（位枚举）
    /// </summary>
    public ExtrasType ExtrasConfig { get; }

    public SubmitDownloadTaskMessage(
        string sourceDocumentId,
        string seriesTitle,
        List<DownloadItemInfo> items,
        int qualityId,
        int audioQualityId,
        string outputDirectory,
        bool useGroupFolder = false,
        ExtrasType extrasConfig = ExtrasType.None,
        string sourceDocumentTitle = "")
        : this(new DownloadSubmission(
            sourceDocumentId,
            sourceDocumentTitle,
            seriesTitle,
            new DownloadProfileSnapshot(
                qualityId,
                audioQualityId,
                outputDirectory,
                useGroupFolder,
                AddIndexToTitle: false,
                extrasConfig.HasFlag(ExtrasType.Danmaku),
                extrasConfig.HasFlag(ExtrasType.Subtitle),
                extrasConfig.HasFlag(ExtrasType.Cover),
                global::BiliDownloader.Services.Naming.NamingTemplateEngine.DefaultTemplate),
            items.Select(item => new DownloadSubmissionItem(
                item.ItemId, item.Title, item.Aid, item.Bvid, item.Cid, item.Duration,
                item.MediaType, item.EpId, item.SeasonId, item.CoverUrl)).ToArray()))
    {
    }

    public SubmitDownloadTaskMessage(DownloadSubmission submission)
    {
        Submission = submission;
        SourceDocumentId = submission.DocumentId;
        SourceDocumentTitle = submission.DocumentTitle;
        SeriesTitle = submission.SeriesTitle;
        Items = submission.Items.Select(item => new DownloadItemInfo
        {
            ItemId = item.ItemId,
            Title = item.Title,
            Aid = item.Aid,
            Bvid = item.Bvid,
            Cid = item.Cid,
            Duration = item.Duration,
            MediaType = item.MediaType,
            EpId = item.EpId,
            SeasonId = item.SeasonId,
            CoverUrl = item.CoverUrl,
        }).ToList();
        QualityId = submission.Profile.VideoQualityId;
        AudioQualityId = submission.Profile.AudioQualityId;
        OutputDirectory = submission.Profile.OutputDirectory;
        UseGroupFolder = submission.Profile.UseGroupFolder;
        ExtrasConfig = (submission.Profile.DownloadDanmaku ? ExtrasType.Danmaku : ExtrasType.None)
            | (submission.Profile.DownloadSubtitle ? ExtrasType.Subtitle : ExtrasType.None)
            | (submission.Profile.DownloadCover ? ExtrasType.Cover : ExtrasType.None);
    }

    public DownloadSubmission ToSubmission() => new(
        SourceDocumentId,
        SourceDocumentTitle,
        SeriesTitle,
        Submission.Profile with
        {
            VideoQualityId = QualityId,
            AudioQualityId = AudioQualityId,
            OutputDirectory = OutputDirectory,
            UseGroupFolder = UseGroupFolder,
            DownloadDanmaku = ExtrasConfig.HasFlag(ExtrasType.Danmaku),
            DownloadSubtitle = ExtrasConfig.HasFlag(ExtrasType.Subtitle),
            DownloadCover = ExtrasConfig.HasFlag(ExtrasType.Cover),
        },
        Items.Select(item => new DownloadSubmissionItem(
            item.ItemId, item.Title, item.Aid, item.Bvid, item.Cid, item.Duration,
            item.MediaType, item.EpId, item.SeasonId, item.CoverUrl)).ToArray());
}
