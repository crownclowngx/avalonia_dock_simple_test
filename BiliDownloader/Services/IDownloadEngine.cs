using BiliDownloader.Models;

namespace BiliDownloader.Services;

/// <summary>
/// 下载引擎接口：抽象下载和合并操作
/// </summary>
public interface IDownloadEngine
{
    /// <summary>
    /// 下载单个视频项的完整流程
    /// </summary>
    Task<string> DownloadItemAsync(
        DownloadTaskRecord task,
        BiliApiService apiService,
        Action<DownloadProgressInfo> onProgress,
        Action<long, long>? onBytesUpdate,
        CancellationToken ct);
}
