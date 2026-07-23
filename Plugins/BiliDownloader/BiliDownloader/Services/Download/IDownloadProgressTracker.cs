using BiliDownloader.Models;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 下载进度追踪器接口：负责节流持久化和消息广播
/// </summary>
public interface IDownloadProgressTracker
{
    /// <summary>处理进度变更（节流写 SQLite + 广播）</summary>
    void OnProgressChanged(DownloadTaskRecord task, DownloadProgressInfo info);

    /// <summary>处理字节数变更（节流写 SQLite）</summary>
    void OnBytesChanged(DownloadTaskRecord task, long videoBytes, long audioBytes);

    /// <summary>立即广播任务状态变更</summary>
    void BroadcastStatusChanged(DownloadTaskRecord task);

    /// <summary>立即广播任务进度</summary>
    void BroadcastProgress(DownloadTaskRecord task);
}
