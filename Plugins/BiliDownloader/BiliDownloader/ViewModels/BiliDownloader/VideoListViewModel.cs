using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagementCommon.Message;
using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Download.Extras;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 提交下载所需的上下文信息，由主 VM 提供。
/// <para>
/// 设计思考（G5 扩展）：新增 NamingTemplate/UpName/PublishDate 字段，
/// 使命名模板能在 Document 侧渲染为最终文件名。
/// Coordinator 接收的 Title 已是成品，无需感知模板逻辑。
/// </para>
/// </summary>
public class SubmitContext
{
    public string DocumentId { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public int QualityId { get; set; }
    public int AudioQualityId { get; set; }
    public string OutputDirectory { get; set; } = "";
    public bool UseGroupFolder { get; set; }
    public bool AddIndexToTitle { get; set; }
    public string SeriesTitle { get; set; } = "下载";

    /// <summary>附加资源选项</summary>
    public bool DownloadDanmaku { get; set; }
    public bool DownloadSubtitle { get; set; }
    public bool DownloadCover { get; set; }

    /// <summary>封面图 URL</summary>
    public string CoverUrl { get; set; } = "";

    /// <summary>G5: 命名模板（如 "{index}.{title}"）</summary>
    public string NamingTemplate { get; set; } = "{index}.{title}";

    /// <summary>G5: UP 主名称（供 {up} 变量使用）</summary>
    public string UpName { get; set; } = "";

    /// <summary>G5: 发布时间（供 {date} 变量使用）</summary>
    public DateTime? PublishDate { get; set; }
    public bool IsNamingValid { get; set; } = true;
    public string NamingValidationError { get; set; } = "";
    public FileConflictPolicy ConflictPolicy { get; set; } = FileConflictPolicy.AutoNumber;
}

/// <summary>
/// 视频列表子 ViewModel：负责视频列表展示、全选/全不选、提交下载、总进度
/// </summary>
public partial class VideoListViewModel : ObservableObject
{
    private readonly Func<SubmitContext> _getSubmitContext;
    private readonly IMessengerService? _messengerService;
    private readonly Action<string> _onStatusMessage;
    private readonly IFfmpegService _ffmpegService;
    private readonly Action? _onConfigurationBlocked;
    private readonly IDownloadSubmissionService? _submissionService;
    private readonly IUserPromptService? _promptService;
    private bool _isBulkSelectionUpdate;

    public ObservableCollection<BiliVideoItem> VideoItems { get; } = new();
    public event Action? SelectionOrTitleChanged;

    [ObservableProperty]
    private double _totalProgress;

    [ObservableProperty]
    private int _selectedCount;

    public int ItemCount => VideoItems.Count;
    public bool HasSelection => SelectedCount > 0;
    public string SelectionSummaryText => $"已选 {SelectedCount} / {ItemCount}";
    public string SubmitButtonText => $"下载所选 {SelectedCount} 项";

    [ObservableProperty]
    private bool _isPreflighting;

    [ObservableProperty]
    private string _preflightSummary = "";

    public RenamePanelViewModel RenamePanel { get; }

    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand DeselectAllCommand { get; }
    public IAsyncRelayCommand SubmitDownloadCommand { get; }
    public IRelayCommand OpenOutputDirCommand { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="getSubmitContext">获取提交上下文的函数（从主 VM 收集参数）</param>
    /// <param name="messengerService">消息总线服务（用于发送提交消息）</param>
    /// <param name="onStatusMessage">状态消息回调（传回主 VM 显示日志）</param>
    public VideoListViewModel(
        Func<SubmitContext> getSubmitContext,
        IMessengerService? messengerService,
        Action<string> onStatusMessage,
        IFfmpegService ffmpegService,
        Action? onConfigurationBlocked = null,
        IDownloadSubmissionService? submissionService = null,
        IUserPromptService? promptService = null)
    {
        _getSubmitContext = getSubmitContext;
        _messengerService = messengerService;
        _onStatusMessage = onStatusMessage;
        _ffmpegService = ffmpegService;
        _onConfigurationBlocked = onConfigurationBlocked;
        _submissionService = submissionService;
        _promptService = promptService;

        SelectAllCommand = new RelayCommand(() => SetAllSelected(true));
        DeselectAllCommand = new RelayCommand(() => SetAllSelected(false));
        SubmitDownloadCommand = new AsyncRelayCommand(SubmitDownloadAsync);
        OpenOutputDirCommand = new RelayCommand(OpenOutputDir);

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
        foreach (var oldItem in VideoItems)
            oldItem.PropertyChanged -= OnVideoItemPropertyChanged;

        VideoItems.Clear();
        foreach (var item in items)
        {
            // 解析结果始终从“未选择”开始，避免单视频或合集被意外提交。
            item.IsSelected = false;
            VideoItems.Add(item);
            item.PropertyChanged += OnVideoItemPropertyChanged;
        }

        RenamePanel.InitTitles(items);
        TotalProgress = 0;
        NotifyCollectionSummaryChanged();
    }

    /// <summary>
    /// 恢复任务时添加单个视频项
    /// </summary>
    public void AddRecoveredItem(BiliVideoItem item)
    {
        var existing = VideoItems.FirstOrDefault(v => v.ItemId == item.ItemId);
        if (existing == null)
        {
            item.IsSelected = false;
            VideoItems.Add(item);
            item.PropertyChanged += OnVideoItemPropertyChanged;
            NotifyCollectionSummaryChanged();
        }
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
        {
            item.PropertyChanged -= OnVideoItemPropertyChanged;
            VideoItems.Remove(item);
            NotifyCollectionSummaryChanged();
        }
    }

    /// <summary>
    /// 当前视频数量
    /// </summary>
    public int Count => VideoItems.Count;

    #endregion

    #region 任务提交

    private async Task SubmitDownloadAsync()
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

        if (!ctx.IsNamingValid)
        {
            _onConfigurationBlocked?.Invoke();
            _onStatusMessage(string.IsNullOrWhiteSpace(ctx.NamingValidationError)
                ? "请先修正命名模板"
                : ctx.NamingValidationError);
            return;
        }

        if (ctx.QualityId == 0)
        {
            _onConfigurationBlocked?.Invoke();
            _onStatusMessage("请选择清晰度");
            return;
        }

        // 旧构造路径没有 G6 预检服务时保留本地兜底；生产路径由结构化预检统一报告。
        if (_submissionService is null && !_ffmpegService.IsReady)
        {
            _onStatusMessage("ffmpeg 未就绪，请在调度器工具中等待下载完成或手动配置路径");
            return;
        }

        // 构造消息
        // G5: 命名模板在 Document 侧（提交前）解析为最终标题。
        // 设计思考：Coordinator 接收的 DownloadItemInfo.Title 已是成品文件名，
        // 这保持了 Coordinator 的稳定性——G2/G3/G4 共 1200+ 行测试覆盖 Coordinator，
        // 不应因命名逻辑变更而回归。
        // 手动重命名优先级高于模板（IsRenamed 的项直接使用用户编辑的标题）。
        var downloadItems = selectedItems.Select(v => new DownloadItemInfo
        {
            ItemId = v.ItemId,
            Title = v.IsRenamed
                ? v.Title
                : Services.Naming.NamingTemplateEngine.Render(
                    ctx.NamingTemplate,
                    new Services.Naming.NamingContext
                    {
                        Title = v.Title,
                        Index = v.Index,
                        Bvid = v.Bvid,
                        UpName = ctx.UpName,
                        PublishDate = ctx.PublishDate,
                        SeriesTitle = ctx.SeriesTitle
                    }),
            Aid = v.Aid,
            Bvid = v.Bvid,
            Cid = v.Cid,
            Duration = v.Duration,
            MediaType = v.MediaType,
            EpId = v.EpId,
            SeasonId = v.SeasonId,
            CoverUrl = ctx.CoverUrl,
        }).ToList();

        var duplicateNames = downloadItems
            .GroupBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (_submissionService is null && duplicateNames.Count > 0)
        {
            _onStatusMessage($"命名冲突：{string.Join("、", duplicateNames.Take(3))}。请加入 {{index}} 或 {{bv}} 变量。");
            return;
        }

        var submission = new DownloadSubmission(
            ctx.DocumentId,
            ctx.DocumentTitle,
            ctx.SeriesTitle,
            new DownloadProfileSnapshot(
                ctx.QualityId,
                ctx.AudioQualityId,
                ctx.OutputDirectory,
                ctx.UseGroupFolder,
                ctx.AddIndexToTitle,
                ctx.DownloadDanmaku,
                ctx.DownloadSubtitle,
                ctx.DownloadCover,
                ctx.NamingTemplate,
                ConflictPolicy: ctx.ConflictPolicy),
            downloadItems.Select(item => new DownloadSubmissionItem(
                item.ItemId, item.Title, item.Aid, item.Bvid, item.Cid, item.Duration,
                item.MediaType, item.EpId, item.SeasonId, item.CoverUrl)).ToArray());
        var submittedIds = new HashSet<string>(downloadItems.Select(item => item.ItemId));
        try
        {
            if (_submissionService is null)
            {
                // 兼容旧测试和旧宿主构造路径；生产 DI 始终注入 G6 可等待提交服务。
                _messengerService?.Send(new SubmitDownloadTaskMessage(submission));
                _onStatusMessage($"已提交 {selectedItems.Count} 个下载任务到调度器");
            }
            else
            {
                IsPreflighting = true;
                SubmissionCommitResult? result = null;
                SubmissionPreflightReport? report = null;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    report = await _submissionService.PreflightAsync(submission);
                    PreflightSummary = $"可提交 {report.ReadyCount}，跳过 {report.SkipCount}，警告 {report.WarningCount}，阻止 {report.BlockedCount}";
                    if (report.IsBlocked)
                    {
                        _onConfigurationBlocked?.Invoke();
                        _onStatusMessage(PreflightSummary + "。请先处理阻止项。");
                        return;
                    }
                    var confirmed = !report.RequiresConfirmation
                        || (_promptService is not null && await _promptService.ConfirmSubmissionAsync(report));
                    if (!confirmed)
                    {
                        _onStatusMessage("已取消提交，未创建任何任务。");
                        return;
                    }
                    result = await _submissionService.CommitAsync(new PreparedSubmission(report, confirmed));
                    if (result.Status != SubmissionCommitStatus.Stale) break;
                }
                if (result is null || result.Status != SubmissionCommitStatus.Committed)
                {
                    _onStatusMessage(result?.Message ?? "输出目录持续变化，提交已取消。");
                    return;
                }
                submittedIds = report!.Items.Where(item => item.ShouldSubmit)
                    .Select(item => item.Item.ItemId).ToHashSet();
                _onStatusMessage(result.Message);
            }

            // 标记为已提交
            foreach (var item in selectedItems.Where(item => submittedIds.Contains(item.ItemId)))
            {
                item.Status = "排队中";
                item.IsSelected = false;
            }
        }
        catch (Exception ex)
        {
            _onStatusMessage($"提交任务失败: {ex.Message}");
        }
        finally
        {
            IsPreflighting = false;
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

    private void SetAllSelected(bool isSelected)
    {
        _isBulkSelectionUpdate = true;
        try
        {
            foreach (var item in VideoItems)
                item.IsSelected = isSelected;
        }
        finally
        {
            _isBulkSelectionUpdate = false;
        }

        UpdateSelectionSummary();
        SelectionOrTitleChanged?.Invoke();
    }

    private void OnVideoItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(BiliVideoItem.IsSelected))
        {
            if (!_isBulkSelectionUpdate)
            {
                UpdateSelectionSummary();
                SelectionOrTitleChanged?.Invoke();
            }
            return;
        }

        if (args.PropertyName == nameof(BiliVideoItem.Title))
            SelectionOrTitleChanged?.Invoke();
    }

    private void NotifyCollectionSummaryChanged()
    {
        OnPropertyChanged(nameof(ItemCount));
        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        SelectedCount = VideoItems.Count(item => item.IsSelected);
        OnPropertyChanged(nameof(SelectionSummaryText));
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(SubmitButtonText));
    }

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
        "interrupted" => "已中断",
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
        "interrupted" => "已中断",
        _ => status,
    };

    #endregion

    #region 打开输出目录

    private void OpenOutputDir()
    {
        try
        {
            var ctx = _getSubmitContext();
            var dir = ctx.OutputDirectory;
            if (Directory.Exists(dir))
            {
                Process.Start("explorer.exe", dir);
            }
            else
            {
                _onStatusMessage($"目录不存在: {dir}");
            }
        }
        catch (Exception ex)
        {
            _onStatusMessage($"打开目录失败: {ex.Message}");
        }
    }

    #endregion
}
