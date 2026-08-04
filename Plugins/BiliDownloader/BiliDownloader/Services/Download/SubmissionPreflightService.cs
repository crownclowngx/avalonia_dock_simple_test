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

    public DashMediaSizeEstimator(BiliApiService apiService, IBiliCredentialProvider credentials)
    {
        _apiService = apiService;
        _credentials = credentials;
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
        var video = dash.VideoStreams
            .Where(stream => stream.Id == profile.VideoQualityId)
            .OrderBy(stream => stream.Codecid == 7 ? 0 : 1)
            .ThenByDescending(stream => stream.Bandwidth)
            .FirstOrDefault()
            ?? dash.VideoStreams.OrderByDescending(stream => stream.Bandwidth).FirstOrDefault();
        var audio = (profile.AudioQualityId > 0
                ? dash.AudioStreams.FirstOrDefault(stream => stream.Id == profile.AudioQualityId)
                : null)
            ?? dash.AudioStreams.OrderByDescending(stream => stream.Bandwidth).FirstOrDefault();
        if (video is null || audio is null || video.Bandwidth <= 0 || audio.Bandwidth <= 0) return null;

        var streamBytes = checked((video.Bandwidth + audio.Bandwidth) * (long)item.Duration / 8L);
        // 下载期间同时存在音视频临时流与最终 MP4。使用两倍流大小并追加 10% 安全余量，
        // 宁可提示用户拆小批次，也不以乐观估算制造可预见的磁盘写满故障。
        return checked(streamBytes * 22L / 10L);
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

    public SubmissionPreflightService(
        IBiliCredentialProvider credentials,
        IFfmpegRuntimeLocator ffmpeg,
        IDownloadTaskRepository tasks,
        IMediaSizeEstimator sizeEstimator,
        IStorageCapacityProvider capacity,
        IEnumerable<IFileConflictStrategy>? strategies = null)
    {
        _credentials = credentials;
        _ffmpeg = ffmpeg;
        _tasks = tasks;
        _sizeEstimator = sizeEstimator;
        _capacity = capacity;
        _strategies = (strategies ?? CreateDefaultStrategies())
            .ToDictionary(strategy => strategy.Policy);
    }

    public async Task<SubmissionPreflightReport> InspectAsync(
        DownloadSubmission submission,
        CancellationToken cancellationToken = default)
    {
        var global = new List<PreflightIssue>();
        if (!_credentials.IsLoggedIn)
            global.Add(Block("login", "登录状态无效，请重新登录后再提交。"));
        if (!_ffmpeg.IsReady)
            global.Add(Block("ffmpeg", "ffmpeg 未就绪，请先在调度器工具中完成配置。"));

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
        long estimatedTotal = 0;
        var hasUnknownEstimate = false;

        foreach (var item in submission.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var issues = new List<PreflightIssue>();
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                itemResults.Add(new(item, string.Empty, string.Empty,
                    ShouldSubmit: false, ShouldSkip: false, IsResume: false, ResumeTaskId: null,
                    HasConflict: false, EstimatedRequiredBytes: 0, Issues: issues));
                continue;
            }
            var actualDirectory = submission.Profile.UseGroupFolder
                ? Path.Combine(outputDirectory, FileNameSanitizer.Sanitize(submission.SeriesTitle))
                : outputDirectory;
            var baseName = FileNameSanitizer.Sanitize(item.Title);
            var outputPath = Path.Combine(actualDirectory, baseName + ".mp4");
            bool hasConflict;
            try
            {
                hasConflict = HasArtifactConflict(outputPath, allocated, fingerprintParts);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(Block("conflict_probe_failed",
                    $"“{item.Title}”无法检查已有文件：{ex.Message}", item.ItemId));
                itemResults.Add(new(item, outputPath, NormalizePathKey(outputPath),
                    ShouldSubmit: false, ShouldSkip: false, IsResume: false, ResumeTaskId: null,
                    HasConflict: false, EstimatedRequiredBytes: 0, Issues: issues));
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
                    () => AllocateNumberedPath(actualDirectory, baseName, allocated, fingerprintParts)));
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
                    estimate = await _sizeEstimator.EstimatePeakBytesAsync(item, submission.Profile, cancellationToken) ?? 0;
                }
                catch (OperationCanceledException) { throw; }
                catch { estimate = 0; }
                if (estimate <= 0) hasUnknownEstimate = true;
                else estimatedTotal = checked(estimatedTotal + estimate);
            }
            itemResults.Add(new(item, outputPath, key, shouldSubmit, shouldSkip, isResume, resumeTaskId,
                hasConflict, estimate, issues));
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

    private static bool HasArtifactConflict(string mp4Path, ISet<string> allocated, ICollection<string> fingerprint)
    {
        var directory = Path.GetDirectoryName(mp4Path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(mp4Path);
        var paths = new List<string>
        {
            mp4Path,
            Path.Combine(directory, stem + ".xml"),
            Path.Combine(directory, stem + "_cover.jpg"),
        };
        if (Directory.Exists(directory))
            paths.AddRange(Directory.EnumerateFiles(directory, stem + ".*.srt"));
        var conflict = allocated.Contains(NormalizePathKey(mp4Path));
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
        string directory, string baseName, ISet<string> allocated, ICollection<string> fingerprint)
    {
        for (var index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{baseName} ({index}).mp4");
            if (!HasArtifactConflict(candidate, allocated, fingerprint)) return candidate;
        }
        throw new IOException("自动序号已达到 9999，无法继续分配输出路径。");
    }

    private static DownloadTaskRecord? FindResumeCandidate(
        IEnumerable<DownloadTaskRecord> tasks, DownloadSubmission submission, DownloadSubmissionItem item)
        => tasks.FirstOrDefault(task =>
            task.DocumentId == submission.DocumentId && task.Aid == item.Aid && task.Cid == item.Cid
            && task.EpId == item.EpId && task.QualityId == submission.Profile.VideoQualityId
            && task.AudioQualityId == submission.Profile.AudioQualityId
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
        return NormalizePathKey(Path.Combine(directory, FileNameSanitizer.Sanitize(task.ItemTitle) + ".mp4"));
    }

    private static bool TryValidateResumeCandidate(DownloadTaskRecord task, out string reason)
    {
        if (string.IsNullOrWhiteSpace(task.TempDirectory) || !Directory.Exists(task.TempDirectory))
        {
            reason = "临时目录不存在";
            return false;
        }
        if (task.ExpectedVideoBytes <= 0 && task.ExpectedAudioBytes <= 0)
        {
            reason = "缺少可信的预期长度";
            return false;
        }
        var hasData = false;
        foreach (var (name, expected) in new[] { ("video.tmp", task.ExpectedVideoBytes), ("audio.tmp", task.ExpectedAudioBytes) })
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
}
