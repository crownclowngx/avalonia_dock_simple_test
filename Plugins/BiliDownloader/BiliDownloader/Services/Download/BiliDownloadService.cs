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

    public BiliDownloadService(
        IBiliDataPaths paths,
        IMediaMuxer mediaMuxer,
        IBiliHttpClientFactory httpClientFactory,
        IDownloadRuntime runtime,
        IStorageCapacityProvider? capacity = null,
        int chunkCount = 4)
    {
        _paths = paths;
        _mediaMuxer = mediaMuxer;
        _capacity = capacity ?? new SystemStorageCapacityProvider();
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
        Func<MediaReadyCheckpoint, Task>? onMediaReadyAsync = null)
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

        var videoTmp = Path.Combine(task.TempDirectory, "video.tmp");
        var audioTmp = Path.Combine(task.TempDirectory, "audio.tmp");
        var safeTitle = FileNameSanitizer.Sanitize(task.ItemTitle);
        var outputPath = string.IsNullOrWhiteSpace(task.OutputFilePath)
            ? Path.Combine(actualOutputDir, safeTitle + ".mp4")
            : task.OutputFilePath;
        // staging 必须与最终文件位于同一目录，才能使用同卷原子移动；放在插件临时目录时，
        // 用户把输出设到其他磁盘会退化为跨卷移动并在发布阶段失败。
        var stagingPath = BuildStagingPath(outputPath, task.TaskId);
        if (File.Exists(outputPath) && task.ConflictPolicy != FileConflictPolicy.Overwrite)
            throw new OutputConflictException(outputPath);
        if (task.ConflictPolicy == FileConflictPolicy.Overwrite && !task.OverwriteConfirmed && File.Exists(outputPath))
            throw new OutputConflictException(outputPath);
        EnsureSufficientSpace(task, actualOutputDir);

        // 进度状态容器（跨三个阶段累计）
        double videoProgress = 0, audioProgress = 0, mergeProgress = 0;
        string currentSpeed = "";

        void ReportProgress(string stage)
        {
            var overall = videoProgress * 0.45 + audioProgress * 0.45 + mergeProgress * 0.10;
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

        // 选择视频流：优先 AVC/H.264 (codecid=7)，选用户指定清晰度
        var videoStream = SelectVideoStream(dashResult.VideoStreams, task.QualityId);
        if (videoStream == null)
            throw new ResourceUnavailableException("未找到匹配的视频流。");

        // 选择音频流：优先按用户指定的音频 ID 选择，没有则回退最高码率
        var audioStream = (task.AudioQualityId > 0
                ? dashResult.AudioStreams.FirstOrDefault(a => a.Id == task.AudioQualityId)
                : null)
            ?? dashResult.AudioStreams.OrderByDescending(a => a.Bandwidth).FirstOrDefault();
        if (audioStream == null)
            throw new ResourceUnavailableException("未找到可用的音频流。");

        // 2. 下载视频流（多连接加速）
        var videoUrls = CdnUrlHelper.FilterAndSortUrls(videoStream.BaseUrl, videoStream.BackupUrls);
        var videoTransfer = await _multiDownloader.DownloadAsync(
            videoUrls, videoTmp, cookieHeader,
            (total, downloaded, speed) =>
            {
                task.VideoBytesDownloaded = downloaded;
                videoProgress = total > 0 ? (double)downloaded / total * 100 : 0;
                currentSpeed = speed;
                ReportProgress("video");
                onBytesUpdate?.Invoke(task.VideoBytesDownloaded, task.AudioBytesDownloaded);
            },
            ct);
        currentSpeed = "";
        videoProgress = 100;
        task.ExpectedVideoBytes = videoTransfer.ExpectedBytes;
        task.VideoIntegrityPassed = videoTransfer.IntegrityPassed;
        ReportProgress("video");
        EnsureSufficientSpace(task, actualOutputDir);

        // 3. 下载音频流（多连接加速）
        var audioUrls = CdnUrlHelper.FilterAndSortUrls(audioStream.BaseUrl, audioStream.BackupUrls);
        var audioTransfer = await _multiDownloader.DownloadAsync(
            audioUrls, audioTmp, cookieHeader,
            (total, downloaded, speed) =>
            {
                task.AudioBytesDownloaded = downloaded;
                audioProgress = total > 0 ? (double)downloaded / total * 100 : 0;
                currentSpeed = speed;
                ReportProgress("audio");
                onBytesUpdate?.Invoke(task.VideoBytesDownloaded, task.AudioBytesDownloaded);
            },
            ct);
        currentSpeed = "";
        audioProgress = 100;
        task.ExpectedAudioBytes = audioTransfer.ExpectedBytes;
        task.AudioIntegrityPassed = audioTransfer.IntegrityPassed;
        ReportProgress("audio");
        EnsureSufficientSpace(task, actualOutputDir);

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
        mergeProgress = 10;
        ReportProgress("merging");
        if (File.Exists(stagingPath)) File.Delete(stagingPath);
        await MergeAsync(videoTmp, audioTmp, stagingPath, ct);
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
        mergeProgress = 100;
        ReportProgress("done");

        // 5. 清理临时文件
        try
        {
            if (File.Exists(videoTmp)) File.Delete(videoTmp);
            if (File.Exists(audioTmp)) File.Delete(audioTmp);
            if (Directory.Exists(task.TempDirectory) &&
                Directory.GetFiles(task.TempDirectory).Length == 0)
                Directory.Delete(task.TempDirectory);
        }
        catch { /* 忽略清理失败 */ }

        return new BiliDownloadItemResult(outputPath, videoTransfer, audioTransfer);
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
        var videoTmp = Path.Combine(task.TempDirectory, "video.tmp");
        var audioTmp = Path.Combine(task.TempDirectory, "audio.tmp");
        var actualOutputDir = string.IsNullOrWhiteSpace(task.OutputFilePath)
            ? (string.IsNullOrEmpty(task.SubFolder)
                ? task.OutputDirectory
                : Path.Combine(task.OutputDirectory, task.SubFolder))
            : Path.GetDirectoryName(task.OutputFilePath) ?? task.OutputDirectory;
        Directory.CreateDirectory(actualOutputDir);
        var outputPath = string.IsNullOrWhiteSpace(task.OutputFilePath)
            ? Path.Combine(actualOutputDir, FileNameSanitizer.Sanitize(task.ItemTitle) + ".mp4")
            : task.OutputFilePath;
        var stagingPath = BuildStagingPath(outputPath, task.TaskId);

        EnsureSufficientSpace(task, actualOutputDir);
        if (File.Exists(stagingPath)) File.Delete(stagingPath);
        onProgress(new DownloadProgressInfo
        {
            Stage = "merging",
            OverallProgress = 91,
            VideoProgress = 100,
            AudioProgress = 100,
            MergeProgress = 10,
        });
        await MergeAsync(videoTmp, audioTmp, stagingPath, ct);
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
            AudioProgress = 100,
            MergeProgress = 100,
        });
        CleanupVerifiedInputs(task.TempDirectory, videoTmp, audioTmp);
        return outputPath;
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
    /// 从视频流列表中选择最佳流：优先 AVC (codecid=7)，匹配指定画质
    /// </summary>
    private static BiliDashStream? SelectVideoStream(List<BiliDashStream> streams, int qualityId)
    {
        if (streams.Count == 0) return null;

        // 先找指定画质 + AVC
        var match = streams.FirstOrDefault(s => s.Id == qualityId && s.Codecid == 7);
        if (match != null) return match;

        // 再找指定画质（任意编码）
        match = streams.FirstOrDefault(s => s.Id == qualityId);
        if (match != null) return match;

        // 兜底：找最高画质的 AVC
        match = streams.Where(s => s.Codecid == 7).OrderByDescending(s => s.Id).FirstOrDefault();
        if (match != null) return match;

        // 最终兜底：最高画质
        return streams.OrderByDescending(s => s.Id).FirstOrDefault();
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
