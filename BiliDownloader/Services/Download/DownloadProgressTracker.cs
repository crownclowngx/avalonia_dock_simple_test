using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using MyAvaloniaManagementCommon.Message;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 下载进度追踪器：负责节流写入 SQLite 和消息总线广播
/// </summary>
public class DownloadProgressTracker : IDownloadProgressTracker
{
    private static readonly IPluginLogger Log = PluginLog.For<DownloadProgressTracker>();

    private readonly IDownloadTaskRepository _repository;
    private readonly IMessengerService _messengerService;
    private static readonly TimeSpan DbWriteInterval = TimeSpan.FromMilliseconds(500);

    private DateTime _lastProgressDbWrite = DateTime.MinValue;
    private DateTime _lastBytesDbWrite = DateTime.MinValue;

    public DownloadProgressTracker(IDownloadTaskRepository repository, IMessengerService messengerService)
    {
        _repository = repository;
        _messengerService = messengerService;
    }

    /// <inheritdoc />
    public void OnProgressChanged(DownloadTaskRecord task, DownloadProgressInfo info)
    {
        task.Progress = info.OverallProgress;
        task.VideoProgress = info.VideoProgress;
        task.AudioProgress = info.AudioProgress;
        task.MergeProgress = info.MergeProgress;
        task.SpeedText = info.SpeedText;
        task.Status = info.Stage switch
        {
            "video" => DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.DownloadingVideo),
            "audio" => DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.DownloadingAudio),
            "merging" => DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Merging),
            "done" => DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Completed),
            _ => task.Status,
        };

        // 节流写 SQLite
        var now = DateTime.UtcNow;
        var isCriticalState = info.Stage is "done" or "merging";
        if (isCriticalState || (now - _lastProgressDbWrite) >= DbWriteInterval)
        {
            _lastProgressDbWrite = now;
            _ = _repository.UpdateStageProgressAsync(
                task.TaskId, task.Progress, task.Status,
                task.VideoProgress, task.AudioProgress,
                task.MergeProgress, task.SpeedText);
        }

        // UI 通知不受节流影响
        BroadcastProgress(task);
    }

    /// <inheritdoc />
    public void OnBytesChanged(DownloadTaskRecord task, long videoBytes, long audioBytes)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastBytesDbWrite) >= DbWriteInterval)
        {
            _lastBytesDbWrite = now;
            _ = _repository.UpdateBytesAsync(task.TaskId, videoBytes, audioBytes);
        }
    }

    /// <inheritdoc />
    public void BroadcastStatusChanged(DownloadTaskRecord task)
    {
        try
        {
            _messengerService.Send(new DownloadTaskStatusChangedMessage(
                targetDocumentId: task.DocumentId,
                taskId: task.TaskId,
                newStatus: task.Status,
                progress: task.Progress,
                videoProgress: task.VideoProgress,
                audioProgress: task.AudioProgress,
                mergeProgress: task.MergeProgress,
                speedText: task.SpeedText));
        }
        catch { /* 忽略广播失败 */ }
    }

    /// <inheritdoc />
    public void BroadcastProgress(DownloadTaskRecord task)
    {
        try
        {
            _messengerService.Send(new DownloadTaskProgressMessage(
                targetDocumentId: task.DocumentId,
                taskId: task.TaskId,
                itemTitle: task.ItemTitle,
                progress: task.Progress,
                status: task.Status,
                errorMessage: task.ErrorMessage,
                videoProgress: task.VideoProgress,
                audioProgress: task.AudioProgress,
                mergeProgress: task.MergeProgress,
                speedText: task.SpeedText));
        }
        catch { /* 忽略广播失败 */ }
    }
}
