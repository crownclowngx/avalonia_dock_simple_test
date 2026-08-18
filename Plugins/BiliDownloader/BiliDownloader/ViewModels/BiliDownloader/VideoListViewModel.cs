using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagementCommon.Events;
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
    public VideoCodecPreference VideoCodecPreference { get; set; } = VideoCodecPreference.AutoCompatibility;
    public OutputContainer OutputContainer { get; set; } = OutputContainer.Mp4;
    public OutputMediaMode OutputMediaMode { get; set; } = OutputMediaMode.AudioVideo;
    public VideoDynamicRangePreference VideoDynamicRangePreference { get; set; } = VideoDynamicRangePreference.Auto;
    public AudioFeaturePreference AudioFeaturePreference { get; set; } = AudioFeaturePreference.Auto;
    public SubtitleOptions SubtitleOptions { get; set; } = SubtitleOptions.None;
    public DanmakuOptions DanmakuOptions { get; set; } = DanmakuOptions.None;
    public long PerTaskRateLimitBytesPerSecond { get; set; }
    public bool IsHighSpecificationSelectionValid { get; set; } = true;
    public IncrementalSubmissionExpectation? IncrementalExpectation { get; set; }
}

/// <summary>
/// 视频列表子 ViewModel：负责视频列表展示、全选/全不选、提交下载、总进度
/// </summary>
public partial class VideoListViewModel : ObservableObject, IDisposable
{
    private readonly Func<SubmitContext> _getSubmitContext;
    private readonly IHostEventBus? _eventBus;
    private readonly Action<string> _onStatusMessage;
    private readonly IFfmpegRuntimeLocator _ffmpegService;
    private readonly Action? _onConfigurationBlocked;
    private readonly IDownloadSubmissionService? _submissionService;
    private readonly IUserPromptService? _promptService;
    private readonly Func<string, Task>? _onPreflightAction;
    private readonly Dictionary<string, string> _taskToSubmissionItem = new(StringComparer.Ordinal);
    private bool _isBulkSelectionUpdate;
    // Document 令牌只覆盖提交前仍属于页面的阶段，包括预检、用户确认和等待提交锁。
    // Commit 成功后，任务所有权已经转移到插件级下载协调器；此 ViewModel 关闭时只停止消费
    // 结果和进度，不撤销数据库中的任务，也不取消已经开始的后台下载。
    private readonly CancellationToken _documentToken;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

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

    [ObservableProperty]
    private string _blockingActionCode = "";

    [ObservableProperty]
    private string _blockingActionLabel = "";

    public bool HasBlockingAction => !string.IsNullOrWhiteSpace(BlockingActionCode);

    public RenamePanelViewModel RenamePanel { get; }

    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand DeselectAllCommand { get; }
    public IAsyncRelayCommand SubmitDownloadCommand { get; }
    public IAsyncRelayCommand ExecuteBlockingActionCommand { get; }
    public IRelayCommand OpenOutputDirCommand { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="getSubmitContext">获取提交上下文的函数（从主 VM 收集参数）</param>
    /// <param name="eventBus">宿主事件总线（用于发送提交事件）</param>
    /// <param name="onStatusMessage">状态消息回调（传回主 VM 显示日志）</param>
    public VideoListViewModel(
        Func<SubmitContext> getSubmitContext,
        IHostEventBus? eventBus,
        Action<string> onStatusMessage,
        IFfmpegRuntimeLocator ffmpegService,
        Action? onConfigurationBlocked = null,
        IDownloadSubmissionService? submissionService = null,
        IUserPromptService? promptService = null,
        Func<string, Task>? onPreflightAction = null,
        CancellationToken documentToken = default)
    {
        _getSubmitContext = getSubmitContext;
        _eventBus = eventBus;
        _onStatusMessage = onStatusMessage;
        _ffmpegService = ffmpegService;
        _onConfigurationBlocked = onConfigurationBlocked;
        _submissionService = submissionService;
        _promptService = promptService;
        _onPreflightAction = onPreflightAction;
        _documentToken = documentToken;

        SelectAllCommand = new RelayCommand(() => SetAllSelected(true));
        DeselectAllCommand = new RelayCommand(() => SetAllSelected(false));
        SubmitDownloadCommand = new AsyncRelayCommand(SubmitDownloadAsync);
        ExecuteBlockingActionCommand = new AsyncRelayCommand(ExecuteBlockingActionAsync);
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
        _taskToSubmissionItem.Clear();
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
        var item = FindItemForTask(msg.TaskId);
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
        var item = FindItemForTask(msg.TaskId);
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

    private async Task SubmitDownloadAsync(CancellationToken commandToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            _documentToken,
            _disposeCts.Token);
        var cancellationToken = linked.Token;
        if (IsDisposed) return;
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

        if (ctx.OutputMediaMode != OutputMediaMode.AudioOnly && ctx.QualityId == 0)
        {
            _onConfigurationBlocked?.Invoke();
            _onStatusMessage("请选择清晰度");
            return;
        }

        if (!ctx.IsHighSpecificationSelectionValid)
        {
            _onConfigurationBlocked?.Invoke();
            _onStatusMessage("当前显式高规格并非所有所选媒体都可用；请调整选择或改为自动/标准。");
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
                ConflictPolicy: ctx.ConflictPolicy,
                VideoCodecPreference: ctx.VideoCodecPreference,
                OutputContainer: ctx.OutputContainer,
                OutputMediaMode: ctx.OutputMediaMode,
                VideoDynamicRangePreference: ctx.VideoDynamicRangePreference,
                AudioFeaturePreference: ctx.AudioFeaturePreference,
                SubtitleOptions: ctx.SubtitleOptions,
                DanmakuOptions: ctx.DanmakuOptions,
                PerTaskRateLimitBytesPerSecond: ctx.PerTaskRateLimitBytesPerSecond),
            downloadItems.Select(item => new DownloadSubmissionItem(
                item.ItemId, item.Title, item.Aid, item.Bvid, item.Cid, item.Duration,
                item.MediaType, item.EpId, item.SeasonId, item.CoverUrl)).ToArray(),
            ctx.IncrementalExpectation);
        var submittedIds = new HashSet<string>(downloadItems.Select(item => item.ItemId));
        try
        {
            if (_submissionService is null)
            {
                // 兼容旧测试和旧宿主构造路径；生产 DI 始终注入 G6 可等待提交服务。
                _eventBus?.Publish(new SubmitDownloadTaskMessage(submission));
                _onStatusMessage($"已提交 {selectedItems.Count} 个下载任务到调度器");
            }
            else
            {
                IsPreflighting = true;
                SubmissionCommitResult? result = null;
                SubmissionPreflightReport? report = null;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    report = await _submissionService.PreflightAsync(submission, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    PreflightSummary = $"可提交 {report.ReadyCount}，跳过 {report.SkipCount}，警告 {report.WarningCount}，阻止 {report.BlockedCount}";
                    if (report.IsBlocked)
                    {
                        SetBlockingAction(report);
                        _onConfigurationBlocked?.Invoke();
                        _onStatusMessage(PreflightSummary + "。请先处理阻止项。");
                        return;
                    }
                    var confirmed = !report.RequiresConfirmation
                        || (_promptService is not null && await _promptService.ConfirmSubmissionAsync(
                            report,
                            cancellationToken));
                    if (!confirmed)
                    {
                        _onStatusMessage("已取消提交，未创建任何任务。");
                        return;
                    }
                    result = await _submissionService.CommitAsync(
                        new PreparedSubmission(report, confirmed),
                        cancellationToken);
                    if (IsDisposed) return;
                    if (result.Status is not (SubmissionCommitStatus.Stale or SubmissionCommitStatus.StaleComparison)) break;
                    if (result.Status == SubmissionCommitStatus.StaleComparison)
                    {
                        if (_onPreflightAction is not null) await _onPreflightAction("stale-comparison");
                        _onStatusMessage(result.Message);
                        return;
                    }
                }
                if (result is null || result.Status != SubmissionCommitStatus.Committed)
                {
                    _onStatusMessage(result?.Message ?? "输出目录持续变化，提交已取消。");
                    return;
                }
                BlockingActionCode = "";
                BlockingActionLabel = "";
                OnPropertyChanged(nameof(HasBlockingAction));
                submittedIds = report!.Items.Where(item => item.ShouldSubmit)
                    .Select(item => item.Item.ItemId).ToHashSet();
                foreach (var reference in result.EffectiveCommittedTasks)
                    _taskToSubmissionItem[reference.TaskId] = reference.SubmissionItemId;
                _onStatusMessage(result.Message);
            }

            // 标记为已提交
            foreach (var item in selectedItems.Where(item => submittedIds.Contains(item.ItemId)))
            {
                item.Status = "排队中";
                item.IsSelected = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Document 关闭属于用户主动结束当前编辑上下文，不是下载提交失败。
            // 取消只覆盖预检、确认窗口和等待 Coordinator 锁的阶段；如果 Commit 已经完成
            // SQLite 原子写入，任务所有权已经转交给插件级 Coordinator，不能在这里回滚或
            // 取消后台下载。因此关闭路径保持静默，并由 IsDisposed 门禁阻止迟到结果回写 UI。
        }
        catch (Exception ex)
        {
            _onStatusMessage($"提交任务失败: {ex.Message}");
        }
        finally
        {
            if (!IsDisposed) IsPreflighting = false;
        }
    }

    private BiliVideoItem? FindItemForTask(string taskId)
    {
        var itemId = _taskToSubmissionItem.GetValueOrDefault(taskId, taskId);
        return VideoItems.FirstOrDefault(item => item.ItemId == itemId);
    }

    private async Task ExecuteBlockingActionAsync(CancellationToken cancellationToken)
    {
        if (_onPreflightAction is null || string.IsNullOrWhiteSpace(BlockingActionCode)) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _documentToken,
            _disposeCts.Token);
        linked.Token.ThrowIfCancellationRequested();
        await _onPreflightAction(BlockingActionCode);
        linked.Token.ThrowIfCancellationRequested();
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0 || _documentToken.IsCancellationRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // 命令取消用于唤醒尚处于预检或确认阶段的等待；事件解绑用于切断页面内部状态传播。
        // 二者都不触碰提交服务和插件级 Coordinator，借此保持 Document 与后台任务的所有权边界。
        SubmitDownloadCommand.Cancel();
        ExecuteBlockingActionCommand.Cancel();
        _disposeCts.Cancel();
        foreach (var item in VideoItems) item.PropertyChanged -= OnVideoItemPropertyChanged;
        _disposeCts.Dispose();
    }

    private void SetBlockingAction(SubmissionPreflightReport report)
    {
        var codes = report.GlobalIssues.Concat(report.Items.SelectMany(item => item.Issues))
            .Where(issue => issue.Severity == PreflightIssueSeverity.Blocking)
            .Select(issue => issue.Code)
            .ToHashSet(StringComparer.Ordinal);
        (BlockingActionCode, BlockingActionLabel) = codes.Contains("login")
            ? ("login", "重新登录")
            : codes.Contains("ffmpeg")
                ? ("ffmpeg", "安装/修复 ffmpeg")
                : codes.Contains("disk_insufficient")
                    ? ("disk", "更换输出目录")
                    : codes.Contains("output_empty") || codes.Contains("output_unwritable")
                        ? ("directory", "选择输出目录")
                        : ("", "");
        OnPropertyChanged(nameof(HasBlockingAction));
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
