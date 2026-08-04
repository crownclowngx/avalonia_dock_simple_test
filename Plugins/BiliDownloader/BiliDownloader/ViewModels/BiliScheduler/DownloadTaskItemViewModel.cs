using CommunityToolkit.Mvvm.ComponentModel;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;

namespace BiliDownloader.ViewModels.BiliScheduler;

/// <summary>持久化下载任务的纯 UI 投影；不拥有任务状态，也不直接执行失败恢复动作。</summary>
public partial class DownloadTaskItemViewModel : ObservableObject
{
    private readonly IDownloadFailurePresentationPolicy _failurePolicy;
    public DownloadTaskRecord Record { get; }

    [ObservableProperty]
    private bool _isSelected;

    public DownloadTaskItemViewModel(
        DownloadTaskRecord record,
        IDownloadFailurePresentationPolicy? failurePolicy = null)
    {
        Record = record;
        _failurePolicy = failurePolicy ?? new DownloadFailurePresentationPolicy();
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
    public string SourceDocumentDisplay => !string.IsNullOrWhiteSpace(Record.SourceDocumentTitle)
        ? Record.SourceDocumentTitle
        : string.IsNullOrWhiteSpace(Record.DocumentId)
            ? "未知工作台"
            : $"工作台 {Record.DocumentId[..Math.Min(8, Record.DocumentId.Length)]}";

    public void RefreshFrom(DownloadTaskRecord source)
    {
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
        OnPropertyChanged(string.Empty);
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
