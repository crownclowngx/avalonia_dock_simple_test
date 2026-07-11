using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliDownloader.Models;

/// <summary>
/// 视频项模型（Document 解析结果中的一项，也对应调度器中的一条下载任务）
/// </summary>
public partial class BiliVideoItem : ObservableObject
{
    /// <summary>
    /// 唯一标识，对应 DownloadTaskRecord.TaskId
    /// </summary>
    public string ItemId { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;
    public long Aid { get; set; }
    public string Bvid { get; set; } = string.Empty;
    public long Cid { get; set; }

    /// <summary>
    /// 时长（秒）
    /// </summary>
    public int Duration { get; set; }

    /// <summary>
    /// 用户是否勾选下载
    /// </summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>
    /// 下载进度 0~100（由调度器回传）
    /// </summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// 下载状态文本：等待中/下载视频/下载音频/合并中/完成/失败
    /// </summary>
    [ObservableProperty]
    private string _status = "等待中";
}
