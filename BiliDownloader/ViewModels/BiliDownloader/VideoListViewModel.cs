using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagementCommon.Message;
using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 提交下载所需的上下文信息，由主 VM 提供
/// </summary>
public class SubmitContext
{
    public string DocumentId { get; set; } = "";
    public string Cookie { get; set; } = "";
    public int QualityId { get; set; }
    public int AudioQualityId { get; set; }
    public string OutputDirectory { get; set; } = "";
    public bool UseGroupFolder { get; set; }
    public bool AddIndexToTitle { get; set; }
    public string SeriesTitle { get; set; } = "下载";
}

/// <summary>
/// 视频列表子 ViewModel：负责视频列表展示、全选/全不选、提交下载、总进度
/// </summary>
public partial class VideoListViewModel : ObservableObject
{
    private readonly Func<SubmitContext> _getSubmitContext;
    private readonly IMessengerService? _messengerService;
    private readonly Action<string> _onStatusMessage;

    public ObservableCollection<BiliVideoItem> VideoItems { get; } = new();

    [ObservableProperty]
    private double _totalProgress;

    public RenamePanelViewModel RenamePanel { get; }

    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand DeselectAllCommand { get; }
    public IRelayCommand SubmitDownloadCommand { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="getSubmitContext">获取提交上下文的函数（从主 VM 收集参数）</param>
    /// <param name="messengerService">消息总线服务（用于发送提交消息）</param>
    /// <param name="onStatusMessage">状态消息回调（传回主 VM 显示日志）</param>
    public VideoListViewModel(
        Func<SubmitContext> getSubmitContext,
        IMessengerService? messengerService,
        Action<string> onStatusMessage)
    {
        _getSubmitContext = getSubmitContext;
        _messengerService = messengerService;
        _onStatusMessage = onStatusMessage;

        SelectAllCommand = new RelayCommand(() => { foreach (var v in VideoItems) v.IsSelected = true; });
        DeselectAllCommand = new RelayCommand(() => { foreach (var v in VideoItems) v.IsSelected = false; });
        SubmitDownloadCommand = new RelayCommand(SubmitDownload);

        RenamePanel = new RenamePanelViewModel(
            onRenameApplied: ApplyRenameToVideoItems,
            getVideoCount: () => VideoItems.Count);
    }

    #region 公开方法（供主 VM 调用）

    /// <summary>
    /// 解析成功后设置视频列表
    /// </summary>
    public void SetItems(List<BiliVideoItem> items)
    {
        VideoItems.Clear();
        foreach (var item in items)
            VideoItems.Add(item);

        RenamePanel.InitTitles(items);
        TotalProgress = 0;
    }

    /// <summary>
    /// 恢复任务时添加单个视频项
    /// </summary>
    public void AddRecoveredItem(BiliVideoItem item)
    {
        var existing = VideoItems.FirstOrDefault(v => v.ItemId == item.ItemId);
        if (existing == null)
            VideoItems.Add(item);
        else
        {
            existing.Status = item.Status;
            existing.StageText = item.StageText;
            existing.Progress = item.Progress;
            existing.VideoProgress = item.VideoProgress;
            existing.AudioProgress = item.AudioProgress;
            existing.MergeProgress = item.MergeProgress;
            existing.SpeedText = item.SpeedText;
        }
    }

    /// <summary>
    /// 更新单个视频的下载进度
    /// </summary>
    public void UpdateItemProgress(DownloadTaskProgressMessage msg)
    {
        var item = VideoItems.FirstOrDefault(v => v.ItemId == msg.TaskId);
        if (item == null) return;

        item.Status = MapStatusToDisplay(msg.Status);
        item.StageText = MapStageToDisplay(msg.Status);
        item.Progress = msg.Progress;
        item.VideoProgress = msg.VideoProgress;
        item.AudioProgress = msg.AudioProgress;
        item.MergeProgress = msg.MergeProgress;
        item.SpeedText = msg.SpeedText;

        if (msg.Status == "failed" && !string.IsNullOrEmpty(msg.ErrorMessage))
        {
            item.Status = $"失败: {msg.ErrorMessage}";
            item.StageText = "失败";
        }

        UpdateTotalProgress();
    }

    /// <summary>
    /// 更新单个视频的状态（调度器自主变更）
    /// </summary>
    public void UpdateItemStatus(DownloadTaskStatusChangedMessage msg)
    {
        var item = VideoItems.FirstOrDefault(v => v.ItemId == msg.TaskId);
        if (item == null) return;

        item.Status = MapStatusToDisplay(msg.NewStatus);
        item.StageText = MapStageToDisplay(msg.NewStatus);
        item.Progress = msg.Progress;
        item.VideoProgress = msg.VideoProgress;
        item.AudioProgress = msg.AudioProgress;
        item.MergeProgress = msg.MergeProgress;
        item.SpeedText = msg.SpeedText;
    }

    /// <summary>
    /// 移除指定任务
    /// </summary>
    public void RemoveItem(string taskId)
    {
        var item = VideoItems.FirstOrDefault(v => v.ItemId == taskId);
        if (item != null)
            VideoItems.Remove(item);
    }

    /// <summary>
    /// 当前视频数量
    /// </summary>
    public int Count => VideoItems.Count;

    #endregion

    #region 任务提交

    private void SubmitDownload()
    {
        if (VideoItems.Count == 0)
        {
            _onStatusMessage("请先解析视频");
            return;
        }

        var selectedItems = VideoItems.Where(v => v.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            _onStatusMessage("请至少勾选一个视频");
            return;
        }

        var ctx = _getSubmitContext();

        if (ctx.QualityId == 0)
        {
            _onStatusMessage("请选择清晰度");
            return;
        }

        // 检查 ffmpeg 是否就绪
        if (!FfmpegService.IsReady)
        {
            _onStatusMessage("ffmpeg 未就绪，请在调度器工具中等待下载完成或手动配置路径");
            return;
        }

        // 构造消息
        var downloadItems = selectedItems.Select(v => new DownloadItemInfo
        {
            ItemId = v.ItemId,
            Title = ctx.AddIndexToTitle ? $"{v.Index}.{v.Title}" : v.Title,
            Aid = v.Aid,
            Bvid = v.Bvid,
            Cid = v.Cid,
            Duration = v.Duration,
        }).ToList();

        var message = new SubmitDownloadTaskMessage(
            sourceDocumentId: ctx.DocumentId,
            seriesTitle: ctx.SeriesTitle,
            items: downloadItems,
            qualityId: ctx.QualityId,
            audioQualityId: ctx.AudioQualityId,
            outputDirectory: ctx.OutputDirectory,
            cookie: ctx.Cookie,
            useGroupFolder: ctx.UseGroupFolder);

        // 通过消息总线发送给调度器
        try
        {
            _messengerService?.Send(message);
            _onStatusMessage($"已提交 {selectedItems.Count} 个下载任务到调度器");

            // 标记为已提交
            foreach (var item in selectedItems)
            {
                item.Status = "排队中";
                item.IsSelected = false;
            }
        }
        catch (Exception ex)
        {
            _onStatusMessage($"提交任务失败: {ex.Message}");
        }
    }

    #endregion

    #region 批量重命名

    private void ApplyRenameToVideoItems(List<string> newTitles)
    {
        for (int i = 0; i < VideoItems.Count && i < newTitles.Count; i++)
        {
            if (!string.IsNullOrEmpty(newTitles[i]))
                VideoItems[i].Title = newTitles[i];
        }

        _onStatusMessage($"已应用批量重命名（{VideoItems.Count} 个视频）");
    }

    #endregion

    #region 辅助方法

    private void UpdateTotalProgress()
    {
        if (VideoItems.Count > 0)
            TotalProgress = VideoItems.Average(v => v.Progress);
    }

    private static string MapStatusToDisplay(string status) => status switch
    {
        "pending" => "排队中",
        "downloading_video" => "下载视频",
        "downloading_audio" => "下载音频",
        "merging" => "合并中",
        "done" => "完成",
        "failed" => "失败",
        _ => status,
    };

    private static string MapStageToDisplay(string status) => status switch
    {
        "pending" => "排队中",
        "downloading_video" => "下载视频",
        "downloading_audio" => "下载音频",
        "merging" => "合并中",
        "done" => "完成",
        "failed" => "失败",
        _ => status,
    };

    #endregion
}
