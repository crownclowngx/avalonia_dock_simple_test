namespace BiliDownloader.Models;

/// <summary>
/// A complete, immutable snapshot of the mutable runtime facts for one task.
/// Progress persistence always writes this value as one database operation.
/// </summary>
public sealed record TaskRuntimeSnapshot(
    string TaskId,
    double Progress,
    string Status,
    double VideoProgress,
    double AudioProgress,
    double MergeProgress,
    string SpeedText,
    long BytesPerSecond,
    long VideoBytes,
    long AudioBytes,
    DateTime UpdatedAt)
{
    public static TaskRuntimeSnapshot From(DownloadTaskRecord task) => new(
        task.TaskId,
        task.Progress,
        task.Status,
        task.VideoProgress,
        task.AudioProgress,
        task.MergeProgress,
        task.SpeedText,
        task.BytesPerSecond,
        task.VideoBytesDownloaded,
        task.AudioBytesDownloaded,
        DateTime.Now);
}
