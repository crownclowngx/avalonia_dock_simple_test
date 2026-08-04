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

    /// <summary>原子更新所有高频变化的运行时事实；兼容实现可按阶段和字节两步退化写入。</summary>
    async Task UpdateRuntimeSnapshotAsync(TaskRuntimeSnapshot snapshot)
    {
        await UpdateStageProgressAsync(
            snapshot.TaskId,
            snapshot.Progress,
            snapshot.Status,
            snapshot.VideoProgress,
            snapshot.AudioProgress,
            snapshot.MergeProgress,
            snapshot.SpeedText);
        await UpdateBytesAsync(snapshot.TaskId, snapshot.VideoBytes, snapshot.AudioBytes);
    }

    /// <summary>更新媒体预期长度和完整性验证事实</summary>
    Task UpdateIntegrityAsync(
        string taskId,
        long expectedVideoBytes,
        long expectedAudioBytes,
        bool videoIntegrityPassed,
        bool audioIntegrityPassed,
        DateTime lastUpdatedAt);

    /// <summary>原子标记任务完成并保存最终输出事实</summary>
    Task MarkCompletedAsync(
        string taskId,
        string outputFilePath,
        string? extrasResultSummary,
        DateTime lastUpdatedAt);

    /// <summary>原子标记任务失败并保存错误分类事实</summary>
    Task MarkFailedAsync(
        string taskId,
        double progress,
        string? errorMessage,
        string? errorType,
        bool isRetryable,
        DateTime lastUpdatedAt);

    /// <summary>更新临时目录路径</summary>
    Task UpdateTempDirectoryAsync(string taskId, string tempDirectory);

    /// <summary>按 task_id 删除单条记录</summary>
    Task DeleteByIdAsync(string taskId);

    /// <summary>按 task_id 列表批量删除记录</summary>
    Task DeleteByIdsAsync(IEnumerable<string> taskIds);

    /// <summary>删除已完成的任务</summary>
    Task DeleteDoneAsync();

    /// <summary>更新附加资源执行结果</summary>
    Task UpdateExtrasResultAsync(string taskId, string? extrasResultSummary);

    /// <summary>
    /// 为通过 G6 校验的旧任务固化最终路径并重新排队。实现必须让路径保留与任务更新处于同一事务，
    /// 否则恢复任务可能与另一个 Document 同时获得同一输出路径。
    /// </summary>
    Task PrepareVerifiedResumeAsync(
        string taskId,
        string outputFilePath,
        string outputPathKey,
        FileConflictPolicy conflictPolicy,
        long estimatedRequiredBytes);

    /// <summary>
    /// 验证任务是否仍持有指定输出路径。默认实现服务于旧测试仓储；SQLite 实现必须查询保留表，
    /// 合并重试只有在保留仍归属当前任务时才能发布成品。
    /// </summary>
    Task<bool> OwnsOutputPathReservationAsync(string taskId, string outputPathKey)
        => Task.FromResult(true);

    /// <summary>
    /// 在一个事务中把任务迁移到新输出路径：旧保留只有在新保留成功后才会被替换。
    /// 实现必须清除旧错误和旧覆盖确认，避免把针对旧文件的授权复用到新位置。
    /// </summary>
    Task RelocateOutputAsync(
        string taskId,
        string outputDirectory,
        string outputFilePath,
        string outputPathKey)
        => Task.CompletedTask;
}
