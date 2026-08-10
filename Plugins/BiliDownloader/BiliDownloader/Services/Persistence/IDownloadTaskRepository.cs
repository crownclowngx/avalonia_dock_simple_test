using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;

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

    /// <summary>
    /// 按媒体身份或完整输出指纹查询相关任务。默认实现仅用于旧测试替身；SQLite 实现必须使用索引查询，
    /// 不能让一次增量检查退化为读取全部历史任务。
    /// </summary>
    async Task<List<DownloadTaskRecord>> GetByIdentityAsync(
        IReadOnlyCollection<MediaUnitKey> mediaUnitKeys,
        IReadOnlyCollection<string> renditionFingerprints,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var media = mediaUnitKeys.ToHashSet();
        var fingerprints = renditionFingerprints.ToHashSet(StringComparer.Ordinal);
        return (await GetAllAsync()).Where(task =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hasMedia = task.Aid > 0 && task.Cid > 0 && media.Contains(new MediaUnitKey(task.Aid, task.Cid));
            return hasMedia || (!string.IsNullOrWhiteSpace(task.RenditionFingerprint)
                && fingerprints.Contains(task.RenditionFingerprint));
        }).ToList();
    }

    /// <summary>更新任务进度和状态</summary>
    Task UpdateProgressAsync(string taskId, double progress, string status, string? errorMessage = null);

    /// <summary>更新任务分段进度和速度</summary>
    Task UpdateStageProgressAsync(
        string taskId, double progress, string status,
        double videoProgress, double audioProgress,
        double mergeProgress, string speedText);

    /// <summary>更新断点续传字节数</summary>
    Task UpdateBytesAsync(string taskId, long videoBytes, long audioBytes);

    /// <summary>
    /// 更新非终态任务的主媒体限速快照。实现必须先持久化，再由协调器更新活动 limiter，
    /// 从而保证崩溃恢复后仍沿用用户最后确认的值；已完成任务的历史事实不可改写。
    /// </summary>
    Task UpdateTaskRateLimitAsync(string taskId, long bytesPerSecond, DateTime lastUpdatedAt)
        => Task.CompletedTask;

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

    /// <summary>
    /// 在下载媒体前原子更新本次重新解析观察到的实际视频编码。旧完成任务不会通过扩展名反推该值。
    /// 默认实现用于兼容旧测试仓储；生产 SQLite 实现必须覆盖。
    /// </summary>
    Task UpdateActualVideoCodecAsync(string taskId, string actualVideoCodec, DateTime lastUpdatedAt)
        => Task.CompletedTask;

    /// <summary>保存本次可信 DASH 选择得到的预期高规格，供崩溃恢复和仅合并重试使用。</summary>
    Task UpdateExpectedMediaFeaturesAsync(
        string taskId,
        MediaFeatureFlags expectedFeatures,
        DateTime lastUpdatedAt)
        => Task.CompletedTask;

    /// <summary>
    /// 只在 staging 已通过发布前验证后写入实际高规格。默认实现保持测试替身的源代码兼容性。
    /// </summary>
    Task UpdateActualMediaFeaturesAsync(
        string taskId,
        MediaFeatureFlags actualFeatures,
        DateTime lastUpdatedAt)
        => Task.CompletedTask;

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

/// <summary>
/// 历史读取专用持久化端口。下载写入仓储与历史查询通过两个接口隔离，
/// 使查询服务不会获得删除、重排队等写权限，也便于使用内存替身验证筛选和导出。
/// </summary>
public interface ITaskHistoryReadRepository
{
    Task<TaskHistoryPage> QueryHistoryPageAsync(
        TaskHistoryQuery query,
        TaskHistoryPageRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TaskHistoryEntry> StreamHistoryAsync(
        TaskHistoryQuery query,
        IReadOnlyCollection<string>? taskIds = null,
        CancellationToken cancellationToken = default);

    Task<DownloadTaskRecord?> GetTaskByIdAsync(
        string taskId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskHistoryDocumentOption>> GetHistoryDocumentOptionsAsync(
        CancellationToken cancellationToken = default);
}
