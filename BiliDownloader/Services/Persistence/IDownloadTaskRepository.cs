using BiliDownloader.Models;

namespace BiliDownloader.Services.Persistence;

/// <summary>
/// 下载任务仓储接口：抽象 SQLite 持久化操作
/// </summary>
public interface IDownloadTaskRepository
{
    /// <summary>初始化数据库（建表 + 迁移）</summary>
    Task InitAsync();

    /// <summary>批量插入任务记录</summary>
    Task InsertBatchAsync(List<DownloadTaskRecord> records);

    /// <summary>查询所有任务</summary>
    Task<List<DownloadTaskRecord>> GetAllAsync();

    /// <summary>按 Document ID 查询任务列表</summary>
    Task<List<DownloadTaskRecord>> GetByDocumentIdAsync(string documentId);

    /// <summary>查询所有未完成的任务（用于重启恢复）</summary>
    Task<List<DownloadTaskRecord>> GetIncompleteAsync();

    /// <summary>更新任务进度和状态</summary>
    Task UpdateProgressAsync(string taskId, double progress, string status, string? errorMessage = null);

    /// <summary>更新任务分段进度和速度</summary>
    Task UpdateStageProgressAsync(
        string taskId, double progress, string status,
        double videoProgress, double audioProgress,
        double mergeProgress, string speedText);

    /// <summary>更新断点续传字节数</summary>
    Task UpdateBytesAsync(string taskId, long videoBytes, long audioBytes);

    /// <summary>更新临时目录路径</summary>
    Task UpdateTempDirectoryAsync(string taskId, string tempDirectory);

    /// <summary>按 task_id 删除单条记录</summary>
    Task DeleteByIdAsync(string taskId);

    /// <summary>按 task_id 列表批量删除记录</summary>
    Task DeleteByIdsAsync(IEnumerable<string> taskIds);

    /// <summary>删除已完成的任务</summary>
    Task DeleteDoneAsync();
}
