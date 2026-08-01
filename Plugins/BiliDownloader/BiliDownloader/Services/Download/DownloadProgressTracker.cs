using System.Collections.Concurrent;
using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using MyAvaloniaManagementCommon.Message;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 下载进度追踪器：负责节流写入 SQLite 和消息总线广播。
/// <para>
/// G3 重构：使用 ProgressWriteChannel 串行写入队列替代 fire-and-forget，
/// 确保同一任务的进度写入严格有序，消除旧进度覆盖新进度的风险。
/// </para>
/// <para>
/// 设计思考：
/// - 节流在入口（OnProgressChanged/OnBytesChanged），合并在消费端（Channel 消费循环）。
///   入口节流减少入队频率（500ms 间隔），消费端合并减少 DB 写入次数。
/// - 序列号（Version）单调递增，消费循环通过比较序列号确保只写入最新值。
/// - UI 广播（BroadcastProgress）仍然同步立即执行，不受节流和队列影响。
/// - FlushAsync/ShutdownAsync 委托给 Channel，提供阶段边界和关闭时的持久化保证。
/// </para>
/// </summary>
public class DownloadProgressTracker : IDownloadProgressTracker
{
    private static readonly IPluginLogger Log = PluginLog.For<DownloadProgressTracker>();

    private readonly IMessengerService _messengerService;
    private readonly ProgressWriteChannel _writeChannel;

    /// <summary>进度写入节流间隔：同一任务在此间隔内只入队一次</summary>
    private static readonly TimeSpan DbWriteInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>per-task 上次进度入队时间（入口节流）</summary>
    private readonly ConcurrentDictionary<string, DateTime> _lastProgressDbWrite = new(StringComparer.Ordinal);

    /// <summary>per-task 上次字节数入队时间（入口节流）</summary>
    private readonly ConcurrentDictionary<string, DateTime> _lastBytesDbWrite = new(StringComparer.Ordinal);

    /// <summary>per-task 序列号计数器：每次入队递增，用于消费端合并判断</summary>
    private readonly ConcurrentDictionary<string, long> _versionCounters = new(StringComparer.Ordinal);

    public DownloadProgressTracker(IDownloadTaskRepository repository, IMessengerService messengerService)
    {
        _messengerService = messengerService;
        _writeChannel = new ProgressWriteChannel(repository);
    }

    /// <inheritdoc />
    public void OnProgressChanged(DownloadTaskRecord task, DownloadProgressInfo info)
    {
        // 更新内存状态（UI 绑定立即生效）
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

        // 入口节流：关键状态（done/merging）立即入队，其余按 500ms 间隔
        var now = DateTime.UtcNow;
        var isCriticalState = info.Stage is "done" or "merging";
        var lastWrite = _lastProgressDbWrite.GetOrAdd(task.TaskId, DateTime.MinValue);

        if (isCriticalState || (now - lastWrite) >= DbWriteInterval)
        {
            _lastProgressDbWrite[task.TaskId] = now;

            // 递增序列号并入队（替代原来的 fire-and-forget）
            var version = _versionCounters.AddOrUpdate(task.TaskId, 1, (_, v) => v + 1);
            _writeChannel.Enqueue(new ProgressWriteRequest(
                TaskId: task.TaskId,
                Version: version,
                Kind: ProgressWriteKind.StageProgress,
                Progress: task.Progress,
                Status: task.Status,
                VideoProgress: task.VideoProgress,
                AudioProgress: task.AudioProgress,
                MergeProgress: task.MergeProgress,
                SpeedText: task.SpeedText));
        }

        // UI 通知不受节流影响，立即广播
        BroadcastProgress(task);
    }

    /// <inheritdoc />
    public void OnBytesChanged(DownloadTaskRecord task, long videoBytes, long audioBytes)
    {
        // 入口节流：按 500ms 间隔入队
        var now = DateTime.UtcNow;
        var lastWrite = _lastBytesDbWrite.GetOrAdd(task.TaskId, DateTime.MinValue);

        if ((now - lastWrite) >= DbWriteInterval)
        {
            _lastBytesDbWrite[task.TaskId] = now;

            var version = _versionCounters.AddOrUpdate(task.TaskId, 1, (_, v) => v + 1);
            _writeChannel.Enqueue(new ProgressWriteRequest(
                TaskId: task.TaskId,
                Version: version,
                Kind: ProgressWriteKind.Bytes,
                VideoBytes: videoBytes,
                AudioBytes: audioBytes));
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

    /// <inheritdoc />
    public Task FlushAsync(string taskId) => _writeChannel.FlushAsync(taskId);

    /// <inheritdoc />
    public Task ShutdownAsync() => _writeChannel.ShutdownAsync();
}
