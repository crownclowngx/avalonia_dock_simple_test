using CommunityToolkit.Mvvm.ComponentModel;
using BiliDownloader.Models;

namespace BiliDownloader.ViewModels.BiliScheduler;

/// <summary>UI-only projection for a persisted download task.</summary>
public partial class DownloadTaskItemViewModel : ObservableObject
{
    public DownloadTaskRecord Record { get; }

    [ObservableProperty]
    private bool _isSelected;

    public DownloadTaskItemViewModel(DownloadTaskRecord record) => Record = record;

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
    public string? ErrorMessage => Record.ErrorMessage;
    public string ErrorActionHint => Record.ErrorType switch
    {
        "network" => "检查网络后重试",
        "cdn" => "切换节点后重试",
        "auth" => "重新登录后恢复",
        "ffmpeg" => "检查 ffmpeg 设置",
        "disk" => "检查磁盘空间和目录权限",
        _ => string.IsNullOrWhiteSpace(Record.ErrorMessage) ? "" : "查看详细日志",
    };
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

public enum TaskDateRange
{
    All,
    Today,
    Last7Days,
    Last30Days,
}

public sealed record TaskDocumentFilterOption(string Id, string Label);
public sealed record TaskChoiceOption(string Value, string Label);
