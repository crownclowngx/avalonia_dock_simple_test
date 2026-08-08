using System.Text;
using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Naming;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 下载与合并服务：HTTP 流式下载（支持断点续传）+ ffmpeg 音视频合并
/// </summary>
public class BiliDownloadService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly MultiConnectionDownloader _multiDownloader;
    private readonly IBiliDataPaths _paths;
    private readonly IMediaMuxer _mediaMuxer;
    private readonly IStorageCapacityProvider _capacity;
    private readonly IMediaStreamSelectionPolicy _selectionPolicy;
    private readonly IOutputArtifactPolicy _outputPolicy;
    private readonly INativeAudioPublisher _nativeAudioPublisher;
    private readonly IMediaOutputVerifier _mediaOutputVerifier;

    public BiliDownloadService(
        IBiliDataPaths paths,
        IMediaMuxer mediaMuxer,
        IBiliHttpClientFactory httpClientFactory,
        IDownloadRuntime runtime,
        IStorageCapacityProvider? capacity = null,
        int chunkCount = 4,
        IMediaStreamSelectionPolicy? selectionPolicy = null,
        IOutputArtifactPolicy? outputPolicy = null,
        INativeAudioPublisher? nativeAudioPublisher = null,
        IMediaOutputVerifier? mediaOutputVerifier = null)
    {
        _paths = paths;
        _mediaMuxer = mediaMuxer;
        _capacity = capacity ?? new SystemStorageCapacityProvider();
        _outputPolicy = outputPolicy ?? new OutputArtifactPolicy();
        _selectionPolicy = selectionPolicy ?? new MediaStreamSelectionPolicy(_outputPolicy);
        _nativeAudioPublisher = nativeAudioPublisher ?? new NativeAudioPublisher();
        _mediaOutputVerifier = mediaOutputVerifier
            ?? (mediaMuxer is IFfmpegRuntimeLocator locator
                ? new FfprobeMediaOutputVerifier(locator, new FfmpegProcessFactory())
                : new UnavailableMediaOutputVerifier());
        _httpClient = httpClientFactory.CreateMediaClient();
        _multiDownloader = new MultiConnectionDownloader(_httpClient, runtime, chunkCount);
    }

    /// <summary>兼容独立构造；插件生产路径始终通过 DI 注入依赖。</summary>
    public BiliDownloadService(IBiliDataPaths paths, int chunkCount = 4)
        : this(
            paths,
            new FfmpegService(new FfmpegProcessFactory()),
            new BiliHttpClientFactory(),
            new SystemDownloadRuntime(),
            new SystemStorageCapacityProvider(),
            chunkCount)
    {
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _multiDownloader.Dispose();
    }

    /// <summary>
    /// 下载单个视频项的完整流程：获取DASH流 -> 下载视频 -> 下载音频 -> ffmpeg合并 -> 清理
    /// </summary>
    /// <param name="task">任务记录（从 SQLite 加载，含所有参数）</param>
    /// <param name="apiService">API 服务（获取 DASH 流）</param>
    /// <param name="onProgress">进度回调（分段进度 + 速度信息）</param>
    /// <param name="onBytesUpdate">字节数更新回调（用于断点续传持久化）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>最终输出文件路径</returns>
    public async Task<BiliDownloadItemResult> DownloadItemAsync(
        DownloadTaskRecord task,
        BiliApiService apiService,
        string cookieHeader,
        Action<DownloadProgressInfo> onProgress,
        Action<long, long>? onBytesUpdate,
        CancellationToken ct,
        Func<MediaReadyCheckpoint, Task>? onMediaReadyAsync = null,
        Func<MediaOutputPlan, Task>? onMediaSelectionResolvedAsync = null)
    {
        // 确保临时目录和输出目录存在
        if (string.IsNullOrWhiteSpace(task.TempDirectory))
        {
            task.TempDirectory = Path.Combine(_paths.TempDirectory, task.TaskId);
        }
        Directory.CreateDirectory(task.TempDirectory);

        // 分组文件夹逻辑：当 SubFolder 非空时，实际输出目录拼接子文件夹
        var actualOutputDir = string.IsNullOrEmpty(task.SubFolder)
            ? task.OutputDirectory
            : Path.Combine(task.OutputDirectory, task.SubFolder);
        Directory.CreateDirectory(actualOutputDir);

        var mode = task.SelectedOutputMediaMode ?? OutputMediaMode.AudioVideo;
        var container = task.SelectedOutputContainer ?? OutputContainer.Mp4;
        var codecPreference = task.SelectedVideoCodec ?? VideoCodecPreference.AutoCompatibility;
        var dynamicRangePreference = task.SelectedVideoDynamicRangePreference ?? VideoDynamicRangePreference.Auto;
        var audioFeaturePreference = task.SelectedAudioFeaturePreference ?? AudioFeaturePreference.Auto;
        var videoTmp = Path.Combine(task.TempDirectory, "video.tmp");
        var audioTmp = Path.Combine(task.TempDirectory, "audio.tmp");
        var safeTitle = FileNameSanitizer.Sanitize(task.ItemTitle);
        var fallbackExtension = _outputPolicy.GetFileExtension(
            mode, container, mode == OutputMediaMode.AudioOnly ? AudioCodec.Aac : AudioCodec.Unknown);
        var outputPath = string.IsNullOrWhiteSpace(task.OutputFilePath)
            ? Path.Combine(actualOutputDir, safeTitle + fallbackExtension)
            : task.OutputFilePath;
        // staging 必须与最终文件位于同一目录，才能使用同卷原子移动；放在插件临时目录时，
        // 用户把输出设到其他磁盘会退化为跨卷移动并在发布阶段失败。
        var stagingPath = BuildStagingPath(outputPath, task.TaskId);
        if (File.Exists(outputPath) && task.ConflictPolicy != FileConflictPolicy.Overwrite)
            throw new OutputConflictException(outputPath);
        if (task.ConflictPolicy == FileConflictPolicy.Overwrite && !task.OverwriteConfirmed && File.Exists(outputPath))
            throw new OutputConflictException(outputPath);
        EnsureSufficientSpace(task, actualOutputDir);

        // 权重由输出模式决定；不需要的阶段保持 0，供 UI 显示“不适用”。
        double videoProgress = 0, audioProgress = 0, mergeProgress = 0;
        string currentSpeed = "";

        void ReportProgress(string stage)
        {
            var overall = mode switch
            {
                OutputMediaMode.VideoOnly => videoProgress * 0.90 + mergeProgress * 0.10,
                OutputMediaMode.AudioOnly => audioProgress,
                _ => videoProgress * 0.45 + audioProgress * 0.45 + mergeProgress * 0.10,
            };
            onProgress(new DownloadProgressInfo
            {
                Stage = stage,
                OverallProgress = overall,
                VideoProgress = videoProgress,
                AudioProgress = audioProgress,
                MergeProgress = mergeProgress,
                SpeedText = currentSpeed,
            });
        }

        // 1. 获取 DASH 流
        ReportProgress("fetching");
        var mediaType = Enum.TryParse<BiliMediaType>(task.MediaType, true, out var mt) ? mt : BiliMediaType.Video;
        var dashResult = await apiService.GetDashResultAsync(
            task.Aid, task.Cid, task.QualityId, cookieHeader,
            mediaType, task.EpId, task.SeasonId);

        var selection = _selectionPolicy.Select(dashResult, new MediaSelectionRequest(
            task.QualityId, task.AudioQualityId, codecPreference, container, mode,
            dynamicRangePreference, audioFeaturePreference));
        if (!selection.Success || selection.OutputPlan is null)
            throw new ResourceUnavailableException(selection.Message);
        var outputPlan = selection.OutputPlan;
        if (!Path.GetExtension(outputPath).Equals(outputPlan.FileExtension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("预留输出路径扩展名与运行时媒体选择不一致，请重新预检。");
        if (onMediaSelectionResolvedAsync is not null)
            await onMediaSelectionResolvedAsync(outputPlan);

        var videoStream = selection.SelectedVideo;
        var audioStream = selection.SelectedAudio;

        // 2. 下载视频流（多连接加速）
        var videoTransfer = new DownloadTransferResult(0, 0, false);
        if (outputPlan.RequiresVideo)
        {
            var videoUrls = CdnUrlHelper.FilterAndSortUrls(videoStream!.BaseUrl, videoStream.BackupUrls);
            videoTransfer = await _multiDownloader.DownloadAsync(
                videoUrls, videoTmp, cookieHeader,
                (total, downloaded, speed) =>
                {
                    task.VideoBytesDownloaded = downloaded;
                    videoProgress = total > 0 ? (double)downloaded / total * 100 : 0;
                    currentSpeed = speed;
                    ReportProgress("video");
                    onBytesUpdate?.Invoke(task.VideoBytesDownloaded, task.AudioBytesDownloaded);
                }, ct);
            currentSpeed = "";
            videoProgress = 100;
            ReportProgress("video");
            EnsureSufficientSpace(task, actualOutputDir);
        }
        task.ExpectedVideoBytes = videoTransfer.ExpectedBytes;
        task.VideoIntegrityPassed = videoTransfer.IntegrityPassed;

        // 3. 下载音频流（多连接加速）
        var audioTransfer = new DownloadTransferResult(0, 0, false);
        if (outputPlan.RequiresAudio)
        {
            var audioUrls = CdnUrlHelper.FilterAndSortUrls(audioStream!.BaseUrl, audioStream.BackupUrls);
            audioTransfer = await _multiDownloader.DownloadAsync(
                audioUrls, audioTmp, cookieHeader,
                (total, downloaded, speed) =>
                {
                    task.AudioBytesDownloaded = downloaded;
                    audioProgress = total > 0 ? (double)downloaded / total * 100 : 0;
                    currentSpeed = speed;
                    ReportProgress("audio");
                    onBytesUpdate?.Invoke(task.VideoBytesDownloaded, task.AudioBytesDownloaded);
                }, ct);
            currentSpeed = "";
            audioProgress = 100;
            ReportProgress("audio");
            EnsureSufficientSpace(task, actualOutputDir);
        }
        task.ExpectedAudioBytes = audioTransfer.ExpectedBytes;
        task.AudioIntegrityPassed = audioTransfer.IntegrityPassed;

        // G7：检查点必须在启动 ffmpeg 前完成持久化。若此处写库失败，任务应失败并保留输入，
        // 绝不能先合并后再补写事实，否则进程崩溃后无法证明临时媒体是否可信。
        if (onMediaReadyAsync is not null)
        {
            await onMediaReadyAsync(new MediaReadyCheckpoint(
                videoTransfer.ExpectedBytes,
                audioTransfer.ExpectedBytes,
                videoTransfer.IntegrityPassed,
                audioTransfer.IntegrityPassed));
        }

        // 4. ffmpeg 合并
        if (File.Exists(stagingPath)) File.Delete(stagingPath);
        var published = false;
        if (outputPlan.RequiresMuxer)
        {
            mergeProgress = 10;
            ReportProgress("merging");
            await _mediaMuxer.MuxAsync(new MediaMuxRequest(
                outputPlan.RequiresVideo ? videoTmp : null,
                outputPlan.RequiresAudio ? audioTmp : null,
                stagingPath, container, mode), ct);
            mergeProgress = 100;
        }
        else
        {
            if (outputPlan.ExpectedMediaFeatures != MediaFeatureFlags.None)
            {
                await CopyToStagingAsync(audioTmp, stagingPath, ct);
            }
            else
            {
                try
                {
                    await _nativeAudioPublisher.PublishAsync(
                        audioTmp, stagingPath, outputPath,
                        task.ConflictPolicy == FileConflictPolicy.Overwrite && task.OverwriteConfirmed, ct);
                }
                catch (IOException) when (File.Exists(outputPath)
                                          && task.ConflictPolicy != FileConflictPolicy.Overwrite)
                {
                    throw new OutputConflictException(outputPath);
                }
                published = true;
            }
        }

        if (outputPlan.ExpectedMediaFeatures != MediaFeatureFlags.None)
        {
            try
            {
                task.ActualMediaFeatures = await _mediaOutputVerifier.VerifyAsync(
                    stagingPath, outputPlan.ExpectedMediaFeatures, ct);
            }
            catch
            {
                // staging 来自刚完成的 mux/copy，但没有通过媒体事实验证，必须删除；已通过长度校验的
                // video.tmp/audio.tmp 仍保留，使用户可以在修复 ffprobe 或依赖后安全重试。
                TryDeleteUntrustedStaging(stagingPath);
                throw;
            }
        }
        else
        {
            task.ActualMediaFeatures = MediaFeatureFlags.None;
        }
        try
        {
            if (!published)
                File.Move(stagingPath, outputPath,
                    overwrite: task.ConflictPolicy == FileConflictPolicy.Overwrite && task.OverwriteConfirmed);
        }
        catch (IOException) when (File.Exists(outputPath)
            && task.ConflictPolicy != FileConflictPolicy.Overwrite)
        {
            throw new OutputConflictException(outputPath);
        }
        ReportProgress("done");

        // 5. 清理临时文件
        try
        {
            if (outputPlan.RequiresVideo && File.Exists(videoTmp)) File.Delete(videoTmp);
            if (outputPlan.RequiresAudio && File.Exists(audioTmp)) File.Delete(audioTmp);
            if (Directory.Exists(task.TempDirectory) &&
                Directory.GetFiles(task.TempDirectory).Length == 0)
                Directory.Delete(task.TempDirectory);
        }
        catch { /* 忽略清理失败 */ }

        return new BiliDownloadItemResult(outputPath, videoTransfer, audioTransfer, outputPlan);
    }

    /// <summary>
    /// 复用已经通过 Coordinator 校验的临时媒体，只执行合并和最终文件发布。
    /// 本方法不持有 API 服务或 Cookie，因此从类型边界上保证不会重新获取 DASH 或下载主媒体。
    /// </summary>
    public async Task<string> MergeDownloadedMediaAsync(
        DownloadTaskRecord task,
        Action<DownloadProgressInfo> onProgress,
        CancellationToken ct)
    {
        var mode = task.SelectedOutputMediaMode ?? OutputMediaMode.AudioVideo;
        if (mode == OutputMediaMode.AudioOnly)
            throw new InvalidOperationException("仅音频任务没有可重试的合并阶段。");
        var container = task.SelectedOutputContainer ?? OutputContainer.Mp4;
        var videoTmp = Path.Combine(task.TempDirectory, "video.tmp");
        var audioTmp = Path.Combine(task.TempDirectory, "audio.tmp");
        var actualOutputDir = string.IsNullOrWhiteSpace(task.OutputFilePath)
            ? (string.IsNullOrEmpty(task.SubFolder)
                ? task.OutputDirectory
                : Path.Combine(task.OutputDirectory, task.SubFolder))
            : Path.GetDirectoryName(task.OutputFilePath) ?? task.OutputDirectory;
        Directory.CreateDirectory(actualOutputDir);
        var outputPath = string.IsNullOrWhiteSpace(task.OutputFilePath)
            ? Path.Combine(actualOutputDir, FileNameSanitizer.Sanitize(task.ItemTitle)
                + _outputPolicy.GetFileExtension(mode, container))
            : task.OutputFilePath;
        var stagingPath = BuildStagingPath(outputPath, task.TaskId);

        EnsureSufficientSpace(task, actualOutputDir);
        if (File.Exists(stagingPath)) File.Delete(stagingPath);
        onProgress(new DownloadProgressInfo
        {
            Stage = "merging",
            OverallProgress = 91,
            VideoProgress = 100,
            AudioProgress = mode == OutputMediaMode.AudioVideo ? 100 : 0,
            MergeProgress = 10,
        });
        await _mediaMuxer.MuxAsync(new MediaMuxRequest(
            videoTmp,
            mode == OutputMediaMode.AudioVideo ? audioTmp : null,
            stagingPath,
            container,
            mode), ct);
        if ((task.ExpectedMediaFeatures ?? MediaFeatureFlags.None) != MediaFeatureFlags.None)
        {
            try
            {
                task.ActualMediaFeatures = await _mediaOutputVerifier.VerifyAsync(
                    stagingPath, task.ExpectedMediaFeatures!.Value, ct);
            }
            catch
            {
                TryDeleteUntrustedStaging(stagingPath);
                throw;
            }
        }
        try
        {
            File.Move(stagingPath, outputPath,
                overwrite: task.ConflictPolicy == FileConflictPolicy.Overwrite && task.OverwriteConfirmed);
        }
        catch (IOException) when (File.Exists(outputPath)
            && task.ConflictPolicy != FileConflictPolicy.Overwrite)
        {
            throw new OutputConflictException(outputPath);
        }
        onProgress(new DownloadProgressInfo
        {
            Stage = "done",
            OverallProgress = 100,
            VideoProgress = 100,
            AudioProgress = mode == OutputMediaMode.AudioVideo ? 100 : 0,
            MergeProgress = 100,
        });
        CleanupVerifiedInputs(task.TempDirectory,
            mode == OutputMediaMode.AudioVideo ? [videoTmp, audioTmp] : [videoTmp]);
        return outputPath;
    }

    private static async Task CopyToStagingAsync(
        string sourcePath,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);
    }

    private static void TryDeleteUntrustedStaging(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>
    /// HTTP 流式下载，支持断点续传（Range 请求），带速度计算
    /// </summary>
    /// <param name="url">下载 URL</param>
    /// <param name="outputPath">输出文件路径</param>
    /// <param name="cookie">Cookie</param>
    /// <param name="existingBytes">已下载字节数（用于续传，0=从头开始）</param>
    /// <param name="onProgress">进度回调 (总字节, 已下载字节, 速度文本)</param>
    /// <param name="ct">取消令牌</param>
    public async Task DownloadStreamAsync(
        string url,
        string outputPath,
        string cookie,
        long existingBytes,
        Action<long, long, string> onProgress,
        CancellationToken ct)
    {
        if (File.Exists(outputPath) && new FileInfo(outputPath).Length != existingBytes)
        {
            throw new InvalidOperationException("传入的续传字节数与现有文件长度不一致");
        }
        await _multiDownloader.DownloadAsync([url], outputPath, cookie, onProgress, ct);
    }

    /// <summary>
    /// ffmpeg 合并视频和音频（委托给 FfmpegService）
    /// </summary>
    public async Task MergeAsync(string videoPath, string audioPath, string outputPath, CancellationToken ct = default)
    {
        await _mediaMuxer.MergeAsync(videoPath, audioPath, outputPath, ct);
    }

    /// <summary>
    /// 临时输出与最终文件保持相同扩展名，确保 ffmpeg 能根据扩展名选择封装格式。
    /// 临时文件仍位于最终目录，发布时可以使用同卷原子移动。
    /// </summary>
    internal static string BuildStagingPath(string outputPath, string taskId)
    {
        var extension = Path.GetExtension(outputPath);
        return string.IsNullOrEmpty(extension)
            ? outputPath + $".staging-{taskId}"
            : Path.ChangeExtension(outputPath, $".staging-{taskId}{extension}");
    }

    /// <summary>
    /// 使用预检估算进行执行阶段硬检查。未知估算不在这里伪装为确定值；下载器仍会让操作系统
    /// 报告真实写入错误。已下载断点字节会从需求中扣除，避免合法续传被重复按全量空间阻止。
    /// </summary>
    private void EnsureSufficientSpace(DownloadTaskRecord task, string directory)
    {
        if (task.EstimatedRequiredBytes <= 0) return;
        var available = _capacity.GetAvailableBytes(directory);
        var required = Math.Max(0,
            task.EstimatedRequiredBytes - task.VideoBytesDownloaded - task.AudioBytesDownloaded);
        if (available is long value && value < required)
            throw new InsufficientDiskSpaceException(required, value);
    }

    private static void CleanupVerifiedInputs(string tempDirectory, params string[] files)
    {
        try
        {
            foreach (var file in files) if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(tempDirectory) && Directory.GetFiles(tempDirectory).Length == 0)
                Directory.Delete(tempDirectory);
        }
        catch
        {
            // 成品已经原子发布，临时文件清理失败不能把成功任务回滚为失败。
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static string FormatSpeed(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "";
        if (bytesPerSecond < 1024) return $"{bytesPerSecond} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024.0:F1} KB/s";
        return $"{bytesPerSecond / (1024.0 * 1024):F1} MB/s";
    }
}
