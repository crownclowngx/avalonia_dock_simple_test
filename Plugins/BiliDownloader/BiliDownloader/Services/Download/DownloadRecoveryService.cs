using BiliDownloader.Models;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.Services.Download;

public interface IDownloadRecoveryService
{
    Task ReconcileAsync(DownloadTaskRecord task);
}

/// <summary>Reconciles persisted byte counters with resumable files on disk.</summary>
public sealed class DownloadRecoveryService : IDownloadRecoveryService
{
    private readonly IDownloadTaskRepository _repository;

    public DownloadRecoveryService(IDownloadTaskRepository repository) => _repository = repository;

    public async Task ReconcileAsync(DownloadTaskRecord task)
    {
        var videoBytes = GetDownloadedLength(task.TempDirectory, "video.tmp", task.ExpectedVideoBytes);
        var audioBytes = GetDownloadedLength(task.TempDirectory, "audio.tmp", task.ExpectedAudioBytes);
        if (videoBytes == task.VideoBytesDownloaded && audioBytes == task.AudioBytesDownloaded) return;

        task.VideoBytesDownloaded = videoBytes;
        task.AudioBytesDownloaded = audioBytes;
        await _repository.UpdateBytesAsync(task.TaskId, videoBytes, audioBytes);
    }

    private static long GetDownloadedLength(string? directory, string baseName, long expectedBytes)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return 0;
        var completedPath = Path.Combine(directory, baseName);
        long length;
        if (File.Exists(completedPath))
        {
            length = new FileInfo(completedPath).Length;
        }
        else
        {
            length = Directory.EnumerateFiles(directory, $"{baseName}.chunk*")
                .Sum(path => new FileInfo(path).Length);
        }

        return expectedBytes > 0 ? Math.Min(length, expectedBytes) : length;
    }
}
