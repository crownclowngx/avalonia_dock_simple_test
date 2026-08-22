using CommunityToolkit.Mvvm.ComponentModel;
using BiliDownloader.Models.ContentSources;
using System.Text.Json.Serialization;

namespace BiliDownloader.Models;

/// <summary>
/// 视频项模型（Document 解析结果中的一项，也对应调度器中的一条下载任务）
/// </summary>
public partial class BiliVideoItem : ObservableObject
{
    /// <summary>
    /// 列表中的递增序号（从 1 开始，解析时赋值）
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 唯一标识，对应 DownloadTaskRecord.TaskId
    /// </summary>
    public string ItemId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 原始标题（解析时赋值，重命名后不变）
    /// </summary>
    public string OriginalTitle { get; set; } = string.Empty;

    /// <summary>
    /// 当前标题（可被用户重命名）
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// 是否已被重命名
    /// </summary>
    public bool IsRenamed => Title != OriginalTitle;

    /// <summary>
    /// Title 变更时同步通知 IsRenamed
    /// </summary>
    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(IsRenamed));
    }

    public long Aid { get; set; }
    public string Bvid { get; set; } = string.Empty;
    public long Cid { get; set; }

    /// <summary>
    /// 解析后的稳定媒体身份，不参与当前 Document schema 3 和任务数据库持久化。
    /// 设计意图：来源身份与媒体身份分离，后续可安全执行跨来源聚合。
    /// </summary>
    [JsonIgnore]
    public MediaUnitKey? MediaUnitKey { get; set; }

    /// <summary>
    /// 时长（秒）
    /// </summary>
    public int Duration { get; set; }

    /// <summary>媒体类型（普通视频/番剧）</summary>
    public BiliMediaType MediaType { get; set; } = BiliMediaType.Video;

    /// <summary>番剧 ep_id（仅 Bangumi 时有值）</summary>
    public long EpId { get; set; }

    /// <summary>番剧 season_id（仅 Bangumi 时有值）</summary>
    public long SeasonId { get; set; }

    /// <summary>封面图 URL</summary>
    public string CoverUrl { get; set; } = string.Empty;

    /// <summary>
    /// 用户是否勾选下载
    /// </summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>
    /// 总下载进度 0~100（由调度器回传）
    /// </summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// 视频下载进度 0~100
    /// </summary>
    [ObservableProperty]
    private double _videoProgress;

    /// <summary>
    /// 音频下载进度 0~100
    /// </summary>
    [ObservableProperty]
    private double _audioProgress;

    /// <summary>
    /// 合成进度 0~100
    /// </summary>
    [ObservableProperty]
    private double _mergeProgress;

    /// <summary>
    /// 下载速度文本，如 "2.5 MB/s"
    /// </summary>
    [ObservableProperty]
    private string _speedText = "";

    /// <summary>
    /// 当前阶段文本：等待中/获取地址/下载视频/下载音频/合并中/完成/失败
    /// </summary>
    [ObservableProperty]
    private string _stageText = "等待中";

    /// <summary>
    /// 下载状态文本（兼容旧逻辑）：等待中/下载视频/下载音频/合并中/完成/失败
    /// </summary>
    [ObservableProperty]
    private string _status = "等待中";
}
