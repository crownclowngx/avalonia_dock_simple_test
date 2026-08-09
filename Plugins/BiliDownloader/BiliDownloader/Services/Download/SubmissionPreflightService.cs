using System.Security.Cryptography;
using System.Text;
using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Naming;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 提交预检边界。调用方只能获得结构化报告，不能在检查过程中直接创建任务。
/// 这种拆分使 UI 确认与后台事实提交之间保持清晰边界，也便于测试每一种失败路径。
/// </summary>
public interface ISubmissionPreflightService
{
    Task<SubmissionPreflightReport> InspectAsync(
        DownloadSubmission submission,
        CancellationToken cancellationToken = default);
}

/// <summary>媒体峰值空间估算边界；测试可替换为确定性结果，无需访问真实 Bilibili。</summary>
public interface IMediaSizeEstimator
{
    Task<long?> EstimatePeakBytesAsync(
        DownloadSubmissionItem item,
        DownloadProfileSnapshot profile,
        CancellationToken cancellationToken);
}

/// <summary>磁盘容量查询边界，隔离 DriveInfo 的平台差异和测试环境波动。</summary>
public interface IStorageCapacityProvider
{
    long? GetAvailableBytes(string path);
}

public sealed class SystemStorageCapacityProvider : IStorageCapacityProvider
{
    public long? GetAvailableBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            // 某些挂载点或沙箱文件系统不支持 DriveInfo。未知容量由预检转换为可确认警告，
            // 不能在这里伪造为 0，也不能静默视为无限空间。
            return null;
        }
    }
}

/// <summary>
/// 使用提交时重新取得的 DASH 元数据估算峰值空间。这里只持久化字节估算，不保存带签名 URL，
/// 避免扩大凭据与临时播放地址的持久化范围。
/// </summary>
public sealed class DashMediaSizeEstimator : IMediaSizeEstimator
{
    private readonly BiliApiService _apiService;
    private readonly IBiliCredentialProvider _credentials;
    private readonly IMediaStreamSelectionPolicy _selectionPolicy;
    private readonly IMediaSizeCalculator _sizeCalculator;

    public DashMediaSizeEstimator(
        BiliApiService apiService,
        IBiliCredentialProvider credentials,
        IMediaStreamSelectionPolicy? selectionPolicy = null,
        IMediaSizeCalculator? sizeCalculator = null)
    {
        _apiService = apiService;
        _credentials = credentials;
        _selectionPolicy = selectionPolicy ?? new MediaStreamSelectionPolicy(new OutputArtifactPolicy());
        _sizeCalculator = sizeCalculator ?? new MediaSizeCalculator();
    }

    public async Task<long?> EstimatePeakBytesAsync(
        DownloadSubmissionItem item,
        DownloadProfileSnapshot profile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item.Duration <= 0) return null;
        var dash = await _apiService.GetDashResultAsync(
            item.Aid, item.Cid, profile.VideoQualityId, _credentials.GetCookieHeader(),
            item.MediaType, item.EpId, item.SeasonId);
        var selection = _selectionPolicy.Select(dash, new MediaSelectionRequest(
            profile.VideoQualityId, profile.AudioQualityId, profile.VideoCodecPreference,
            profile.OutputContainer, profile.OutputMediaMode,
            profile.VideoDynamicRangePreference, profile.AudioFeaturePreference));
        return selection is { Success: true, OutputPlan: not null }
            ? _sizeCalculator.EstimatePeakBytes(selection.OutputPlan, item.Duration)
            : null;
    }
}

/// <summary>
/// G6 预检 Facade：集中编排登录、依赖、目录、容量、冲突与续传检查，
/// 但把网络估算和磁盘查询留在小接口之后，遵守依赖倒置并保持单元测试确定性。
/// </summary>
public sealed class SubmissionPreflightService : ISubmissionPreflightService
{
    private readonly IBiliCredentialProvider _credentials;
    private readonly IFfmpegRuntimeLocator _ffmpeg;
    private readonly IDownloadTaskRepository _tasks;
    private readonly IMediaSizeEstimator _sizeEstimator;
    private readonly IStorageCapacityProvider _capacity;
    private readonly IReadOnlyDictionary<FileConflictPolicy, IFileConflictStrategy> _strategies;
    private readonly IMediaPreflightAnalyzer? _mediaAnalyzer;
    private readonly IOutputArtifactPolicy _outputPolicy;
    private readonly IMediaMuxerCapabilityProvider? _muxerCapabilities;

    public SubmissionPreflightService(
        IBiliCredentialProvider credentials,
        IFfmpegRuntimeLocator ffmpeg,
        IDownloadTaskRepository tasks,
        IMediaSizeEstimator sizeEstimator,
        IStorageCapacityProvider capacity,
        IEnumerable<IFileConflictStrategy>? strategies = null,
        IMediaPreflightAnalyzer? mediaAnalyzer = null,
        IOutputArtifactPolicy? outputPolicy = null,
        IMediaMuxerCapabilityProvider? muxerCapabilities = null)
    {
        _credentials = credentials;
        _ffmpeg = ffmpeg;
        _tasks = tasks;
        _sizeEstimator = sizeEstimator;
        _capacity = capacity;
        _strategies = (strategies ?? CreateDefaultStrategies())
            .ToDictionary(strategy => strategy.Policy);
        _mediaAnalyzer = mediaAnalyzer;
        _outputPolicy = outputPolicy ?? new OutputArtifactPolicy();
        _muxerCapabilities = muxerCapabilities;
    }

    public async Task<SubmissionPreflightReport> InspectAsync(
        DownloadSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var global = new List<PreflightIssue>();
        var subtitleOptions = submission.Profile.EffectiveSubtitleOptions;
        var danmakuOptions = submission.Profile.EffectiveDanmakuOptions;
        try
        {
            subtitleOptions.Validate();
            danmakuOptions.Validate();
        }
        catch (ArgumentException ex)
        {
            global.Add(Block("invalid_extras_configuration", ex.Message));
        }
        var requestsSoftSubtitles = subtitleOptions.SelectionMode != SubtitleSelectionMode.None
            && subtitleOptions.DeliveryMode is SubtitleDeliveryMode.SoftMuxed
                or SubtitleDeliveryMode.ExternalAndSoftMuxed;
        if (requestsSoftSubtitles && submission.Profile.OutputMediaMode == OutputMediaMode.AudioOnly)
            global.Add(Block("soft_subtitle_audio_only", "原生音频不能封装软字幕；请改为外置字幕或选择包含视频的输出模式。"));
        if (requestsSoftSubtitles
            && submission.Profile.OutputContainer == OutputContainer.Mkv
            && subtitleOptions.OutputFormat == SubtitleOutputFormat.Vtt)
            global.Add(Block("soft_subtitle_mkv_vtt", "MKV 软字幕仅支持 SRT/ASS；VTT 仍可作为外置字幕输出。"));
        if (!_outputPolicy.IsValidCombination(
                submission.Profile.OutputMediaMode, submission.Profile.OutputContainer))
            global.Add(Block("invalid_output_combination", "输出模式与容器组合不合法。音视频/仅视频只支持 MP4、MKV；仅音频只支持原生音频。"));
        if (submission.Profile.OutputMediaMode != OutputMediaMode.AudioOnly)
        {
            if (!_ffmpeg.IsReady)
                global.Add(Block("ffmpeg", "ffmpeg 未就绪，请先在调度器工具中完成配置。"));
            else if (_muxerCapabilities is not null)
            {
                var capabilities = await _muxerCapabilities.GetCapabilitiesAsync(cancellationToken);
                if (!capabilities.Supports(submission.Profile.OutputContainer))
                    global.Add(Block("ffmpeg_muxer", $"当前 ffmpeg 不支持 {submission.Profile.OutputContainer} 封装。"));
            }
        }

        var outputDirectory = NormalizeAndProbeDirectory(submission.Profile.OutputDirectory, global);
        var globallyBlocked = global.Any(issue => issue.Severity == PreflightIssueSeverity.Blocking);
        var existingTasks = await _tasks.GetAllAsync();
        var activeKeys = existingTasks
            .Where(task => DownloadTaskStatusMapper.FromStorageString(task.Status) is not
                    (DownloadTaskStatus.Completed or DownloadTaskStatus.Canceled))
            .Select(GetExistingTaskPathKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(GetPathComparer());
        var allocated = new HashSet<string>(activeKeys, GetPathComparer());
        var itemResults = new List<PreflightItemResult>();
        var fingerprintParts = new List<string>();
        fingerprintParts.Add(
            $"PROFILE|{submission.Profile.VideoDynamicRangePreference}|{submission.Profile.AudioFeaturePreference}|{submission.Profile.OutputMediaMode}|{submission.Profile.OutputContainer}");
        var batchRenditions = new HashSet<string>(StringComparer.Ordinal);
        var expectedNew = submission.IncrementalExpectation?.ExpectedNewRenditionFingerprints
            .ToHashSet(StringComparer.Ordinal) ?? [];
        if (submission.IncrementalExpectation is { ComparisonToken: var comparisonToken } &&
            !comparisonToken.StartsWith("cmp1:", StringComparison.Ordinal))
            global.Add(Block("stale_comparison", "增量比较标识无效，请重新检查更新。"));
        long estimatedTotal = 0;
        var hasUnknownEstimate = false;

        foreach (var item in submission.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var issues = new List<PreflightIssue>();
            MediaOutputPlan? outputPlan = null;
            long? analyzedEstimate = null;
            var mediaBlocked = false;
            if (!globallyBlocked && _mediaAnalyzer is not null)
            {
                try
                {
                    var analysis = await _mediaAnalyzer.AnalyzeAsync(item, submission.Profile, cancellationToken);
                    if (!analysis.Selection.Success || analysis.Selection.OutputPlan is null)
                    {
                        issues.Add(Block(
                            "media_selection_" + analysis.Selection.FailureCode.ToString().ToLowerInvariant(),
                            $"“{item.Title}”：{analysis.Selection.Message}", item.ItemId));
                        mediaBlocked = true;
                    }
                    else
                    {
                        outputPlan = analysis.Selection.OutputPlan;
                        analyzedEstimate = analysis.EstimatedPeakBytes;
                        fingerprintParts.Add($"OUTPUT|{item.Aid}|{item.Cid}|{outputPlan.ActualVideoCodec}|{outputPlan.ActualAudioCodec}|{outputPlan.OutputContainer}|{outputPlan.OutputMediaMode}|{outputPlan.FileExtension}|{outputPlan.ExpectedMediaFeatures}");
                    }
                }
                catch (MediaAuthorizationException)
                {
                    issues.Add(Block("login", $"“{item.Title}”需要登录或更高账号权限。", item.ItemId));
                    mediaBlocked = true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    issues.Add(Block("media_probe", $"“{item.Title}”无法验证可用媒体流：{ex.Message}", item.ItemId));
                    mediaBlocked = true;
                }
            }
            outputPlan ??= CreateCompatibilityOutputPlan(submission.Profile);
            RenditionFingerprint? rendition = null;
            if (item.Aid > 0 && item.Cid > 0
                && (submission.Profile.VideoQualityId > 0
                    || submission.Profile.OutputMediaMode == OutputMediaMode.AudioOnly))
            {
                rendition = RenditionFingerprint.Create(
                    new Models.ContentSources.MediaUnitKey(item.Aid, item.Cid),
                    submission.Profile.ToRenditionSpecification());
                fingerprintParts.Add("RENDITION|" + rendition.Value.Value);
            }
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                itemResults.Add(new(item, string.Empty, string.Empty,
                    ShouldSubmit: false, ShouldSkip: false, IsResume: false, ResumeTaskId: null,
                    HasConflict: false, EstimatedRequiredBytes: 0, Issues: issues, OutputPlan: outputPlan));
                continue;
            }
            var actualDirectory = submission.Profile.UseGroupFolder
                ? Path.Combine(outputDirectory, FileNameSanitizer.Sanitize(submission.SeriesTitle))
                : outputDirectory;
            var baseName = FileNameSanitizer.Sanitize(item.Title);
            var outputPath = Path.Combine(actualDirectory, baseName + outputPlan.FileExtension);
            bool hasConflict;
            try
            {
                hasConflict = HasArtifactConflict(
                    outputPath, submission.Profile, allocated, fingerprintParts);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(Block("conflict_probe_failed",
                    $"“{item.Title}”无法检查已有文件：{ex.Message}", item.ItemId));
                itemResults.Add(new(item, outputPath, NormalizePathKey(outputPath),
                    ShouldSubmit: false, ShouldSkip: false, IsResume: false, ResumeTaskId: null,
                    HasConflict: false, EstimatedRequiredBytes: 0, Issues: issues, OutputPlan: outputPlan));
                continue;
            }
            var shouldSubmit = true;
            var shouldSkip = false;
            var isResume = false;
            string? resumeTaskId = null;
            var candidate = submission.Profile.ConflictPolicy == FileConflictPolicy.ResumeVerified
                ? FindResumeCandidate(existingTasks, submission, item)
                : null;
            var resumeReason = string.Empty;
            var resumeValid = candidate is not null && TryValidateResumeCandidate(candidate, out resumeReason);
            var strategy = _strategies[submission.Profile.ConflictPolicy];
            FileConflictDecision decision;
            try
            {
                decision = strategy.Decide(new FileConflictContext(
                    item, outputPath, hasConflict, candidate, resumeValid, resumeReason,
                    () => AllocateNumberedPath(
                        actualDirectory, baseName, outputPlan.FileExtension,
                        submission.Profile, allocated, fingerprintParts)));
            }
            catch (IOException ex)
            {
                decision = new(outputPath, ShouldSubmit: false, Issues:
                [
                    Block("path_allocation_failed", $"“{item.Title}”无法分配唯一输出路径：{ex.Message}", item.ItemId),
                ]);
            }
            outputPath = decision.OutputFilePath;
            shouldSubmit = decision.ShouldSubmit;
            shouldSkip = decision.ShouldSkip;
            isResume = decision.IsResume;
            resumeTaskId = decision.ResumeTaskId;
            issues.AddRange(decision.EffectiveIssues);
            if (mediaBlocked) shouldSubmit = false;

            if (rendition.HasValue && !batchRenditions.Add(rendition.Value.Value))
            {
                issues.Add(Warn("duplicate_in_batch", $"“{item.Title}”与同批另一项目是相同输出版本，已合并跳过。", item.ItemId));
                shouldSubmit = false;
                shouldSkip = true;
            }

            if (rendition.HasValue)
            {
                if (submission.IncrementalExpectation is not null && !expectedNew.Contains(rendition.Value.Value))
                {
                    issues.Add(Block("stale_comparison",
                        $"“{item.Title}”的输出设置已不同于增量预览，请重新分类。", item.ItemId));
                    shouldSubmit = false;
                }
                var exactTasks = existingTasks.Where(task => string.Equals(
                    task.RenditionFingerprint, rendition.Value.Value, StringComparison.Ordinal)).ToArray();
                var occupied = exactTasks.Any(IsTrustedRenditionOccupant);
                if (occupied)
                {
                    if (submission.IncrementalExpectation is not null && expectedNew.Contains(rendition.Value.Value))
                        issues.Add(Block("stale_comparison", $"“{item.Title}”的任务事实已变化，请刷新增量分类。", item.ItemId));
                    else
                    {
                        issues.Add(Warn("rendition_exists", $"“{item.Title}”已存在相同输出版本，已跳过。", item.ItemId));
                        shouldSkip = true;
                    }
                    shouldSubmit = false;
                }

                if (existingTasks.Any(task => task.Aid == item.Aid && task.Cid == item.Cid &&
                    task.QualityId == submission.Profile.VideoQualityId &&
                    task.AudioQualityId == submission.Profile.AudioQualityId &&
                    string.IsNullOrWhiteSpace(task.RenditionFingerprint)))
                    issues.Add(Warn("legacy_identity_incomplete",
                        $"“{item.Title}”存在输出身份不完整的旧任务；确认后仍可提交。", item.ItemId));
            }

            // task_id 是任务事实主键。旧实现的 OR REPLACE 会把历史任务静默抹掉；G6 明确要求
            // 重复身份只能走任务中心“重来”或校验续传，不能借文件策略隐式替换数据库事实。
            if (!isResume && existingTasks.Any(task => task.TaskId == item.ItemId))
            {
                issues.Add(Block("task_exists",
                    $"“{item.Title}”已经存在同一任务记录，请在任务中心选择重来，或改用校验续传。",
                    item.ItemId));
                shouldSubmit = false;
            }

            var key = NormalizePathKey(outputPath);
            if (shouldSubmit && !isResume && !allocated.Add(key))
            {
                issues.Add(Block("reservation", $"“{item.Title}”与同批或活动任务争用同一输出路径。", item.ItemId));
                shouldSubmit = false;
            }

            long estimate = 0;
            if (shouldSubmit && !globallyBlocked)
            {
                try
                {
                    // 新分析器已经在一次 DASH 请求内完成选择与估算；未知时不得二次请求。
                    estimate = _mediaAnalyzer is not null
                        ? analyzedEstimate ?? 0
                        : await _sizeEstimator.EstimatePeakBytesAsync(item, submission.Profile, cancellationToken) ?? 0;
                }
                catch (OperationCanceledException) { throw; }
                catch (MediaAuthorizationException)
                {
                    issues.Add(Block("login", $"“{item.Title}”需要登录或更高账号权限。", item.ItemId));
                    shouldSubmit = false;
                }
                catch { estimate = 0; }
                if (estimate <= 0) hasUnknownEstimate = true;
                else estimatedTotal = checked(estimatedTotal + estimate);
            }
            itemResults.Add(new(item, outputPath, key, shouldSubmit, shouldSkip, isResume, resumeTaskId,
                hasConflict, estimate, issues, outputPlan));
            fingerprintParts.Add($"PLAN|{key}|{shouldSubmit}|{shouldSkip}|{isResume}");
        }

        var available = globallyBlocked || string.IsNullOrWhiteSpace(outputDirectory)
            ? null
            : _capacity.GetAvailableBytes(outputDirectory);
        if (!globallyBlocked)
        {
            if (hasUnknownEstimate || available is null)
                global.Add(Warn("disk_unknown", "部分媒体大小或磁盘可用空间无法可靠取得；确认后可提交，写入前仍会再次检查。"));
            else if (available < estimatedTotal)
                global.Add(Block("disk_insufficient", $"磁盘空间不足：预计峰值需要 {FormatBytes(estimatedTotal)}，可用 {FormatBytes(available.Value)}。"));
        }

        var fingerprint = ComputeFingerprint(fingerprintParts
            .Concat(activeKeys.Select(key => "ACTIVE|" + key))
            .Append(outputDirectory)
            .Append(submission.Profile.ConflictPolicy.ToString()));
        return new(submission, itemResults, global, fingerprint, available);
    }

    private static string NormalizeAndProbeDirectory(string path, List<PreflightIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            issues.Add(Block("output_empty", "请选择输出目录。"));
            return string.Empty;
        }
        try
        {
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(fullPath);
            var probe = Path.Combine(fullPath, $".bili-preflight-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(probe);
            return fullPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            issues.Add(Block("output_unwritable", $"输出目录不可写：{ex.Message}"));
            return string.Empty;
        }
    }

    /// <summary>
    /// 只有可信成品或仍可能产生成品的活动/可恢复任务占用输出身份。
    /// 已取消、不可恢复失败以及成品丢失的完成任务允许用户重新下载。
    /// </summary>
    private static bool IsTrustedRenditionOccupant(DownloadTaskRecord task)
    {
        var status = DownloadTaskStatusMapper.FromStorageString(task.Status);
        return status == DownloadTaskStatus.Completed && File.Exists(task.OutputFilePath) ||
            status is DownloadTaskStatus.Ready or DownloadTaskStatus.FetchingMetadata or
                DownloadTaskStatus.DownloadingVideo or DownloadTaskStatus.VideoReady or
                DownloadTaskStatus.DownloadingAudio or DownloadTaskStatus.AudioReady or
                DownloadTaskStatus.Merging or DownloadTaskStatus.Paused or
                DownloadTaskStatus.Interrupted or DownloadTaskStatus.WaitingForLogin ||
            status == DownloadTaskStatus.Failed && task.IsRetryable;
    }

    private static bool HasArtifactConflict(
        string mediaPath,
        DownloadProfileSnapshot profile,
        ISet<string> allocated,
        ICollection<string> fingerprint)
    {
        var directory = Path.GetDirectoryName(mediaPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(mediaPath);
        var paths = new List<string>
        {
            mediaPath,
            // P0 起即把这两个历史附加文件视为主媒体冲突域。即使本次没有勾选，也不能因
            // G9 结构化配置而降低既有覆盖保护；否则旧文件可能在用户切换配置后被静默复用。
            Path.Combine(directory, stem + ".xml"),
            Path.Combine(directory, stem + "_cover.jpg"),
        };

        // 弹幕文件没有语言后缀，因此可以根据最终格式精确计算冲突。旧布尔配置通过
        // EffectiveDanmakuOptions 映射为 XML，保证旧 Document 的提交行为不发生变化。
        foreach (var format in profile.EffectiveDanmakuOptions.Formats)
            paths.Add(Path.Combine(directory, stem + "." + format.ToString().ToLowerInvariant()));

        var subtitle = profile.EffectiveSubtitleOptions;
        var hasExternalSubtitle = subtitle.SelectionMode != SubtitleSelectionMode.None
            && subtitle.DeliveryMode is SubtitleDeliveryMode.External
                or SubtitleDeliveryMode.ExternalAndSoftMuxed;
        if (hasExternalSubtitle)
        {
            var extension = "." + subtitle.OutputFormat.ToString().ToLowerInvariant();
            if (subtitle.SelectionMode == SubtitleSelectionMode.SelectedLanguages)
            {
                // 文件系统名使用安全语言键；原始稳定键仍由执行摘要和字幕元数据保存。
                // 在预检阶段使用与发布阶段相同的净化器，可避免“预检无冲突、发布时覆盖”的竞态。
                paths.AddRange(subtitle.LanguageKeys.Select(language => Path.Combine(
                    directory, $"{stem}.{FileNameSanitizer.Sanitize(language)}{extension}")));
            }
            else if (Directory.Exists(directory))
            {
                // All 模式的实际语言只有主动提交时联网取得目录后才能完全确定。这里重新枚举
                // 当前目录中同格式的语言文件，将任何既有轨视为整组冲突，禁止静默覆盖缓存外文件。
                paths.AddRange(Directory.EnumerateFiles(directory, $"{stem}.*{extension}"));
            }
        }

        var conflict = allocated.Contains(NormalizePathKey(mediaPath));
        foreach (var path in paths.Distinct(GetPathComparer()))
        {
            if (!File.Exists(path)) continue;
            var info = new FileInfo(path);
            fingerprint.Add($"{NormalizePathKey(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
            conflict = true;
        }
        return conflict;
    }

    private static string AllocateNumberedPath(
        string directory, string baseName, string extension,
        DownloadProfileSnapshot profile,
        ISet<string> allocated, ICollection<string> fingerprint)
    {
        for (var index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");
            if (!HasArtifactConflict(candidate, profile, allocated, fingerprint)) return candidate;
        }
        throw new IOException("自动序号已达到 9999，无法继续分配输出路径。");
    }

    private static DownloadTaskRecord? FindResumeCandidate(
        IEnumerable<DownloadTaskRecord> tasks, DownloadSubmission submission, DownloadSubmissionItem item)
        => tasks.FirstOrDefault(task =>
            task.DocumentId == submission.DocumentId && task.Aid == item.Aid && task.Cid == item.Cid
            && task.EpId == item.EpId && task.QualityId == submission.Profile.VideoQualityId
            && task.AudioQualityId == submission.Profile.AudioQualityId
            && (task.SelectedVideoCodec ?? VideoCodecPreference.AutoCompatibility) == submission.Profile.VideoCodecPreference
            && (task.SelectedOutputContainer ?? OutputContainer.Mp4) == submission.Profile.OutputContainer
            && (task.SelectedOutputMediaMode ?? OutputMediaMode.AudioVideo) == submission.Profile.OutputMediaMode
            && DownloadTaskStatusMapper.FromStorageString(task.Status) is
                DownloadTaskStatus.Paused or DownloadTaskStatus.Interrupted or DownloadTaskStatus.Failed);

    private static string GetExistingTaskPathKey(DownloadTaskRecord task)
    {
        if (!string.IsNullOrWhiteSpace(task.OutputPathKey)) return task.OutputPathKey;
        if (!string.IsNullOrWhiteSpace(task.OutputFilePath)) return NormalizePathKey(task.OutputFilePath);
        if (string.IsNullOrWhiteSpace(task.OutputDirectory)) return string.Empty;
        var directory = string.IsNullOrWhiteSpace(task.SubFolder)
            ? task.OutputDirectory
            : Path.Combine(task.OutputDirectory, task.SubFolder);
        var extension = task.SelectedOutputMediaMode == OutputMediaMode.AudioOnly
            ? ".m4a"
            : task.SelectedOutputContainer == OutputContainer.Mkv ? ".mkv" : ".mp4";
        return NormalizePathKey(Path.Combine(directory, FileNameSanitizer.Sanitize(task.ItemTitle) + extension));
    }

    private static bool TryValidateResumeCandidate(DownloadTaskRecord task, out string reason)
    {
        if (string.IsNullOrWhiteSpace(task.TempDirectory) || !Directory.Exists(task.TempDirectory))
        {
            reason = "临时目录不存在";
            return false;
        }
        var mode = task.SelectedOutputMediaMode ?? OutputMediaMode.AudioVideo;
        var requiresVideo = mode is OutputMediaMode.AudioVideo or OutputMediaMode.VideoOnly;
        var requiresAudio = mode is OutputMediaMode.AudioVideo or OutputMediaMode.AudioOnly;
        var legacySnapshot = task.SubmissionSnapshotVersion == 0;
        if (legacySnapshot
            ? task.ExpectedVideoBytes <= 0 && task.ExpectedAudioBytes <= 0
            : (requiresVideo && task.ExpectedVideoBytes <= 0)
            || (requiresAudio && task.ExpectedAudioBytes <= 0))
        {
            reason = "缺少可信的预期长度";
            return false;
        }
        var hasData = false;
        var expectedInputs = new List<(string Name, long Expected)>();
        if (legacySnapshot || requiresVideo) expectedInputs.Add(("video.tmp", task.ExpectedVideoBytes));
        if (legacySnapshot || requiresAudio) expectedInputs.Add(("audio.tmp", task.ExpectedAudioBytes));
        foreach (var (name, expected) in expectedInputs)
        {
            var files = Directory.EnumerateFiles(task.TempDirectory, name + "*")
                .Where(path => path == Path.Combine(task.TempDirectory, name)
                    || Path.GetFileName(path).StartsWith(name + ".chunk", StringComparison.Ordinal))
                .OrderBy(path => path).ToArray();
            if (files.Length == 0) continue;
            hasData = true;
            var length = files.Sum(path => new FileInfo(path).Length);
            if (expected > 0 && length > expected)
            {
                reason = $"{name} 长度超过预期";
                return false;
            }
        }
        if (!hasData)
        {
            reason = "没有可恢复的临时数据";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    public static string NormalizePathKey(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string ComputeFingerprint(IEnumerable<string> parts)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts.Order()))));

    private static PreflightIssue Warn(string code, string message, string? itemId = null)
        => new(code, PreflightIssueSeverity.Warning, message, itemId);
    private static PreflightIssue Block(string code, string message, string? itemId = null)
        => new(code, PreflightIssueSeverity.Blocking, message, itemId);
    private static string FormatBytes(long value) => $"{value / 1024d / 1024d / 1024d:F2} GB";

    private static IFileConflictStrategy[] CreateDefaultStrategies() =>
    [
        new SkipConflictStrategy(),
        new OverwriteConflictStrategy(),
        new ResumeVerifiedConflictStrategy(),
        new AutoNumberConflictStrategy(),
    ];

    private MediaOutputPlan CreateCompatibilityOutputPlan(DownloadProfileSnapshot profile)
    {
        var audioCodec = profile.OutputMediaMode == OutputMediaMode.AudioOnly ? AudioCodec.Aac : AudioCodec.Unknown;
        if (!_outputPolicy.IsValidCombination(profile.OutputMediaMode, profile.OutputContainer))
            return new MediaOutputPlan(VideoCodec.Unknown, audioCodec, profile.OutputContainer,
                profile.OutputMediaMode, ".invalid", 0, 0);
        return new MediaOutputPlan(
            VideoCodec.Unknown,
            audioCodec,
            profile.OutputContainer,
            profile.OutputMediaMode,
            _outputPolicy.GetFileExtension(profile.OutputMediaMode, profile.OutputContainer, audioCodec),
            0,
            0);
    }
}
