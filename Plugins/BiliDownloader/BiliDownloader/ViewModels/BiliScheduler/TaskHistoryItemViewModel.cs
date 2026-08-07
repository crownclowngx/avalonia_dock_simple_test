using BiliDownloader.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliDownloader.ViewModels.BiliScheduler;

/// <summary>历史条目的纯展示投影；选择与文件状态均为会话态，不修改 SQLite 任务事实。</summary>
public partial class TaskHistoryItemViewModel : ObservableObject
{
    public TaskHistoryEntry Entry { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilePresenceText))]
    [NotifyPropertyChangedFor(nameof(CanReveal))]
    [NotifyPropertyChangedFor(nameof(CanRedownload))]
    private FilePresenceStatus _filePresenceStatus;

    public TaskHistoryItemViewModel(
        TaskHistoryEntry entry,
        bool isSelected,
        FilePresenceStatus filePresenceStatus)
    {
        Entry = entry;
        _isSelected = isSelected;
        _filePresenceStatus = filePresenceStatus;
    }

    public string TaskId => Entry.TaskId;
    public string ItemTitle => Entry.ItemTitle;
    public string StatusDisplayText => DownloadTaskStatusMapper.ToDisplayText(
        DownloadTaskStatusMapper.FromStorageString(Entry.Status));
    public string SourceDocumentDisplay => string.IsNullOrWhiteSpace(Entry.SourceDocumentTitle)
        ? string.IsNullOrWhiteSpace(Entry.DocumentId) ? "未知工作台" : $"工作台 {Entry.DocumentId[..Math.Min(8, Entry.DocumentId.Length)]}"
        : Entry.SourceDocumentTitle;
    public string CreatedAtText => Entry.CreatedAt.ToString("yyyy-MM-dd HH:mm");
    public string QualityText => $"Q{Entry.VideoQualityId} / A{Entry.AudioQualityId}";
    public string OutputSpecificationText
    {
        get
        {
            var codec = Entry.OutputMediaMode == OutputMediaMode.AudioOnly
                ? "不适用"
                : Entry.SelectedVideoCodec is null
                    ? "未知编码"
                    : $"{Entry.SelectedVideoCodec.Value.ToDisplayText()} ({Entry.SelectedVideoCodec.Value}) → {OutputOptionDisplay.ActualCodecToDisplayText(Entry.ActualVideoCodec)}";
            return $"{codec} · {Entry.OutputContainer?.ToDisplayText() ?? "未知容器"} · {Entry.OutputMediaMode?.ToDisplayText() ?? "未知模式"}";
        }
    }
    public string OutputFilePath => Entry.OutputFilePath;
    public string ErrorSummary => string.IsNullOrWhiteSpace(Entry.ErrorMessage)
        ? string.Empty
        : Entry.ErrorMessage.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
    public string FilePresenceText => FilePresenceStatus switch
    {
        FilePresenceStatus.Exists => "文件存在",
        FilePresenceStatus.Missing => "文件缺失",
        FilePresenceStatus.Inaccessible => "无法访问",
        _ => "未检查",
    };
    public bool CanReveal => FilePresenceStatus == FilePresenceStatus.Exists;
    public bool CanRetryOriginal => Entry.Status == "failed" && Entry.IsRetryable;
    public bool CanRedownload => Entry.Status switch
    {
        "done" => FilePresenceStatus == FilePresenceStatus.Missing,
        "failed" => !Entry.IsRetryable,
        "canceled" => true,
        _ => false,
    };
}
