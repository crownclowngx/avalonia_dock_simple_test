using CommunityToolkit.Mvvm.ComponentModel;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;

namespace BiliDownloader.ViewModels.BiliScheduler;

/// <summary>持久化下载任务的纯 UI 投影；不拥有任务状态，也不直接执行失败恢复动作。</summary>
public partial class DownloadTaskItemViewModel : ObservableObject
{
    private readonly IDownloadFailurePresentationPolicy _failurePolicy;
    private long _lastKnownRateLimit;
    public DownloadTaskRecord Record { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isRateLimitEnabled;

    [ObservableProperty]
    private long _rateLimitKiBPerSecond = BandwidthLimitPolicy.DefaultEditorBytesPerSecond / 1024;

    [ObservableProperty]
    private string _rateLimitValidationMessage = "";

    public DownloadTaskItemViewModel(
        DownloadTaskRecord record,
        IDownloadFailurePresentationPolicy? failurePolicy = null)
    {
        Record = record;
        _failurePolicy = failurePolicy ?? new DownloadFailurePresentationPolicy();
        _lastKnownRateLimit = record.TaskRateLimitBytesPerSecond;
        SyncRateLimitEditor(record.TaskRateLimitBytesPerSecond);
    }

    public string TaskId => Record.TaskId;
    public string ItemTitle => Record.ItemTitle;
    public string Status => Record.Status;
    public string StatusDisplayText => DownloadTaskStatusMapper.ToDisplayText(
        DownloadTaskStatusMapper.FromStorageString(Record.Status));
    public double Progress => Record.Progress;
    public double VideoProgress => Record.VideoProgress;
    public double AudioProgress => Record.AudioProgress;
    public double MergeProgress => Record.MergeProgress;
    public string SpeedText => Record.SpeedText;
    public string EstimatedRemainingText => Record.EstimatedRemainingText;
    public long TotalDownloadedBytes => Record.TotalDownloadedBytes;
    public long TotalExpectedBytes => Record.TotalExpectedBytes;
    public string QualityDisplayText => Record.QualityDisplayText;
    public string OutputSpecificationText => Record.OutputSpecificationDisplayText;
    public string FullOutputPath => !string.IsNullOrWhiteSpace(Record.OutputFilePath)
        ? Record.OutputFilePath
        : Record.FullOutputPath;
    public string? ErrorMessage => string.IsNullOrWhiteSpace(Record.ErrorType)
        ? Record.ErrorMessage
        : FailurePresentation.UserMessage;
    public DownloadFailurePresentation FailurePresentation => _failurePolicy.Resolve(Record.ErrorType);
    public DownloadFailureAction PrimaryFailureAction => FailurePresentation.PrimaryAction;
    public DownloadFailureAction? SecondaryFailureAction => FailurePresentation.SecondaryAction;
    public DownloadFailureActionRequest PrimaryFailureActionRequest => new(Record, PrimaryFailureAction.Kind);
    public DownloadFailureActionRequest? SecondaryFailureActionRequest => SecondaryFailureAction is null
        ? null
        : new(Record, SecondaryFailureAction.Kind);
    public bool HasFailureAction => !string.IsNullOrWhiteSpace(Record.ErrorType);
    public bool HasSecondaryFailureAction => SecondaryFailureAction is not null;
    public bool IsRateLimitEditable =>
        DownloadTaskStatusMapper.FromStorageString(Record.Status) != DownloadTaskStatus.Completed;
    public string TaskRateLimitDisplayText => Record.TaskRateLimitBytesPerSecond == 0
        ? "任务限速：不限速"
        : $"任务限速：{Record.TaskRateLimitBytesPerSecond / 1024} KiB/s";
    public string SourceDocumentDisplay => !string.IsNullOrWhiteSpace(Record.SourceDocumentTitle)
        ? Record.SourceDocumentTitle
        : string.IsNullOrWhiteSpace(Record.DocumentId)
            ? "未知工作台"
            : $"工作台 {Record.DocumentId[..Math.Min(8, Record.DocumentId.Length)]}";

    public void RefreshFrom(DownloadTaskRecord source)
    {
        var rateLimitChanged = source.TaskRateLimitBytesPerSecond != _lastKnownRateLimit;
        Record.Progress = source.Progress;
        Record.VideoProgress = source.VideoProgress;
        Record.AudioProgress = source.AudioProgress;
        Record.MergeProgress = source.MergeProgress;
        Record.SpeedText = source.SpeedText;
        Record.BytesPerSecond = source.BytesPerSecond;
        Record.VideoBytesDownloaded = source.VideoBytesDownloaded;
        Record.AudioBytesDownloaded = source.AudioBytesDownloaded;
        Record.Status = source.Status;
        Record.ErrorMessage = source.ErrorMessage;
        Record.ErrorType = source.ErrorType;
        Record.IsRetryable = source.IsRetryable;
        Record.OutputFilePath = source.OutputFilePath;
        Record.ExpectedVideoBytes = source.ExpectedVideoBytes;
        Record.ExpectedAudioBytes = source.ExpectedAudioBytes;
        Record.ExtrasResultSummary = source.ExtrasResultSummary;
        Record.SubtitleOptions = source.SubtitleOptions;
        Record.DanmakuOptions = source.DanmakuOptions;
        Record.TaskRateLimitBytesPerSecond = source.TaskRateLimitBytesPerSecond;
        if (rateLimitChanged)
        {
            _lastKnownRateLimit = source.TaskRateLimitBytesPerSecond;
            SyncRateLimitEditor(source.TaskRateLimitBytesPerSecond);
        }
        OnPropertyChanged(string.Empty);
    }

    public long GetRequestedRateLimitBytesPerSecond()
        => IsRateLimitEnabled
            ? BandwidthLimitPolicy.FromKibibytesPerSecond(RateLimitKiBPerSecond)
            : 0;

    public void MarkRateLimitApplied(long bytesPerSecond)
    {
        _lastKnownRateLimit = bytesPerSecond;
        Record.TaskRateLimitBytesPerSecond = bytesPerSecond;
        RateLimitValidationMessage = "";
        SyncRateLimitEditor(bytesPerSecond);
        OnPropertyChanged(nameof(TaskRateLimitDisplayText));
    }

    private void SyncRateLimitEditor(long bytesPerSecond)
    {
        IsRateLimitEnabled = bytesPerSecond > 0;
        if (bytesPerSecond > 0)
            RateLimitKiBPerSecond = BandwidthLimitPolicy.ToKibibytesPerSecond(bytesPerSecond);
    }
}

/// <summary>任务卡片命令参数，把任务事实与结构化行动一起传给应用服务。</summary>
public sealed record DownloadFailureActionRequest(
    DownloadTaskRecord Task,
    DownloadFailureActionKind Action);

public enum TaskDateRange
{
    All,
    Today,
    Last7Days,
    Last30Days,
}

public sealed record TaskDocumentFilterOption(string Id, string Label);
public sealed record TaskChoiceOption(string Value, string Label);
