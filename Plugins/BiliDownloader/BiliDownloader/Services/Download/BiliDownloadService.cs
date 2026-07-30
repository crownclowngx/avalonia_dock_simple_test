using System.Text;
using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 下载与合并服务：HTTP 流式下载（支持断点续传）+ ffmpeg 音视频合并
/// </summary>
public class BiliDownloadService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly MultiConnectionDownloader _multiDownloader;
    private readonly IBiliDataPaths _paths;
    private readonly IFfmpegService _ffmpegService;

    public BiliDownloadService(
        IBiliDataPaths paths,
        IFfmpegService ffmpegService,
        IBiliHttpClientFactory httpClientFactory,
        IDownloadRuntime runtime,
        int chunkCount = 4)
    {
        _paths = paths;
        _ffmpegService = ffmpegService;
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
        CancellationToken ct)
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
        var safeTitle = SanitizeFileName(task.ItemTitle);
        var outputPath = GetUniqueFilePath(actualOutputDir, safeTitle, "mp4");

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
            throw new Exception("未找到匹配的视频流");

        // 选择音频流：优先按用户指定的音频 ID 选择，没有则回退最高码率
        var audioStream = (task.AudioQualityId > 0
                ? dashResult.AudioStreams.FirstOrDefault(a => a.Id == task.AudioQualityId)
                : null)
            ?? dashResult.AudioStreams.OrderByDescending(a => a.Bandwidth).FirstOrDefault();
        if (audioStream == null)
            throw new Exception("未找到音频流");

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

        // 4. ffmpeg 合并
        mergeProgress = 10;
        ReportProgress("merging");
        await MergeAsync(videoTmp, audioTmp, outputPath, ct);
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
        await _ffmpegService.MergeAsync(videoPath, audioPath, outputPath, ct);
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
    /// 文件名非法字符替换
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().TrimEnd('.');
    }

    /// <summary>
    /// 获取唯一文件路径（重名时追加序号）
    /// </summary>
    private static string GetUniqueFilePath(string dir, string name, string ext)
    {
        var path = Path.Combine(dir, $"{name}.{ext}");
        if (!File.Exists(path)) return path;

        for (int i = 1; i < 10000; i++)
        {
            path = Path.Combine(dir, $"{name} ({i}).{ext}");
            if (!File.Exists(path)) return path;
        }
        return path;
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
