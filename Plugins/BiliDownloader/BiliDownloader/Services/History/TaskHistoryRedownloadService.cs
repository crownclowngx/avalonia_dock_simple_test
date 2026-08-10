using BiliDownloader.Models;
using BiliDownloader.Services.Download.Extras;
using BiliDownloader.Services.Naming;

namespace BiliDownloader.Services.History;

/// <summary>
/// 把历史事实转换为新的提交意图。服务只负责纯映射和兼容判断，不直接写数据库，
/// 后续仍必须由 IDownloadSubmissionService 完成预检、确认和 Coordinator 锁内提交。
/// </summary>
public interface ITaskHistoryRedownloadService
{
    Task<TaskHistoryRedownloadPlan> CreatePlanAsync(
        string sourceTaskId,
        CancellationToken cancellationToken = default);
}

public sealed class TaskHistoryRedownloadService : ITaskHistoryRedownloadService
{
    private readonly ITaskHistoryQueryService _history;

    public TaskHistoryRedownloadService(ITaskHistoryQueryService history)
    {
        _history = history;
    }

    public async Task<TaskHistoryRedownloadPlan> CreatePlanAsync(
        string sourceTaskId,
        CancellationToken cancellationToken = default)
    {
        var task = await _history.GetTaskByIdAsync(sourceTaskId, cancellationToken)
            ?? throw new InvalidOperationException("历史任务不存在或已经被删除。");
        var status = DownloadTaskStatusMapper.FromStorageString(task.Status);
        if (status is not (DownloadTaskStatus.Completed or DownloadTaskStatus.Failed or DownloadTaskStatus.Canceled))
            throw new InvalidOperationException("只有终态历史任务可以创建新的下载任务。");
        if (!HasStableMediaReference(task))
            throw new InvalidOperationException("旧任务缺少 Aid/Cid、Bvid 或 EpId，无法安全恢复媒体身份。");

        var warnings = new List<string>();
        var hasExactSnapshot = task.SubmissionSnapshotVersion >= 1
            && task.SelectedVideoCodec.HasValue
            && task.SelectedOutputContainer.HasValue
            && task.SelectedOutputMediaMode.HasValue;
        if (!hasExactSnapshot)
        {
            warnings.Add("该任务来自旧版本，编码、容器、输出模式和命名细节缺少完整快照；将使用兼容默认值重新预检。");
        }
        var hasExactHighSpecificationSnapshot = task.SubmissionSnapshotVersion >= 2
            && task.SelectedVideoDynamicRangePreference.HasValue
            && task.SelectedAudioFeaturePreference.HasValue;
        if (!hasExactHighSpecificationSnapshot)
            warnings.Add("该任务没有 G8 高规格偏好快照；将使用 Auto 重新探测，不会根据旧文件或描述反推历史事实。");
        var hasExactExtrasSnapshot = task.SubmissionSnapshotVersion >= 3;
        if (!hasExactExtrasSnapshot)
            warnings.Add("该任务没有 G9 附加资源快照；将按旧布尔字段兼容为全部字幕 SRT 和弹幕 XML。");
        var hasExactRateLimitSnapshot = task.SubmissionSnapshotVersion >= 4;

        var extras = (ExtrasType)task.ExtrasConfig;
        var profile = new DownloadProfileSnapshot(
            task.QualityId,
            task.AudioQualityId,
            task.OutputDirectory,
            hasExactSnapshot ? task.UseGroupFolder : !string.IsNullOrWhiteSpace(task.SubFolder),
            hasExactSnapshot && task.AddIndexToTitle,
            extras.HasFlag(ExtrasType.Danmaku),
            extras.HasFlag(ExtrasType.Subtitle),
            extras.HasFlag(ExtrasType.Cover),
            hasExactSnapshot && !string.IsNullOrWhiteSpace(task.NamingTemplate)
                ? task.NamingTemplate
                : NamingTemplateEngine.DefaultTemplate,
            hasExactSnapshot ? task.PresetId : null,
            task.ConflictPolicy,
            hasExactSnapshot ? task.SelectedVideoCodec!.Value : VideoCodecPreference.AutoCompatibility,
            hasExactSnapshot ? task.SelectedOutputContainer!.Value : OutputContainer.Mp4,
            hasExactSnapshot ? task.SelectedOutputMediaMode!.Value : OutputMediaMode.AudioVideo,
            hasExactHighSpecificationSnapshot
                ? task.SelectedVideoDynamicRangePreference!.Value
                : VideoDynamicRangePreference.Auto,
            hasExactHighSpecificationSnapshot
                ? task.SelectedAudioFeaturePreference!.Value
                : AudioFeaturePreference.Auto,
            hasExactExtrasSnapshot
                ? task.SubtitleOptions
                : extras.HasFlag(ExtrasType.Subtitle) ? SubtitleOptions.LegacyEnabled : SubtitleOptions.None,
            hasExactExtrasSnapshot
                ? task.DanmakuOptions
                : extras.HasFlag(ExtrasType.Danmaku) ? DanmakuOptions.LegacyEnabled : DanmakuOptions.None,
            hasExactRateLimitSnapshot ? task.TaskRateLimitBytesPerSecond : 0);

        var mediaType = Enum.TryParse<BiliMediaType>(task.MediaType, true, out var parsed)
            ? parsed
            : BiliMediaType.Video;
        var newTaskId = Guid.NewGuid().ToString("N");
        var submission = new DownloadSubmission(
            task.DocumentId,
            task.SourceDocumentTitle,
            task.SeriesTitle,
            profile,
            [new DownloadSubmissionItem(
                newTaskId,
                task.ItemTitle,
                task.Aid,
                task.Bvid,
                task.Cid,
                task.DurationSeconds,
                mediaType,
                task.EpId,
                task.SeasonId,
                task.CoverUrl)],
            IncrementalExpectation: null,
            RedownloadedFromTaskId: task.TaskId);
        return new TaskHistoryRedownloadPlan(submission, warnings);
    }

    private static bool HasStableMediaReference(DownloadTaskRecord task) =>
        task.Aid > 0 && task.Cid > 0
        || !string.IsNullOrWhiteSpace(task.Bvid)
        || task.EpId > 0;
}
