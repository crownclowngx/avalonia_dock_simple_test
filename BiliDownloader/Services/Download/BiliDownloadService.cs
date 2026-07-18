using System.Diagnostics;
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

    public BiliDownloadService(int chunkCount = 4)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", HttpConstants.UserAgent);
        _httpClient.DefaultRequestHeaders.Add("Referer", HttpConstants.Referer);
        _httpClient.DefaultRequestHeaders.Add("Origin", HttpConstants.Origin);
        _httpClient.Timeout = TimeSpan.FromMinutes(60);

        _multiDownloader = new MultiConnectionDownloader(chunkCount);
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
    public async Task<string> DownloadItemAsync(
        DownloadTaskRecord task,
        BiliApiService apiService,
        Action<DownloadProgressInfo> onProgress,
        Action<long, long>? onBytesUpdate,
        CancellationToken ct)
    {
        // 确保临时目录和输出目录存在
        if (string.IsNullOrWhiteSpace(task.TempDirectory))
        {
            var tempBase = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BiliDownloader", "temp");
            task.TempDirectory = Path.Combine(tempBase, task.TaskId);
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
        var dashResult = await apiService.GetDashResultAsync(task.Aid, task.Cid, task.QualityId, task.Cookie);

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
        await _multiDownloader.DownloadAsync(
            videoUrls, videoTmp, task.Cookie,
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
        ReportProgress("video");

        // 3. 下载音频流（多连接加速）
        var audioUrls = CdnUrlHelper.FilterAndSortUrls(audioStream.BaseUrl, audioStream.BackupUrls);
        await _multiDownloader.DownloadAsync(
            audioUrls, audioTmp, task.Cookie,
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
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookie);

        // 断点续传：设置 Range 头
        if (existingBytes > 0 && File.Exists(outputPath))
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // 校验 Range 响应：续传时服务器忽略 Range 返回 200 OK，删除已有文件从头开始
        if (existingBytes > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            existingBytes = 0;
        }

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        // 如果是续传，totalBytes 是剩余字节数，需要加上已下载的
        if (existingBytes > 0 && totalBytes > 0)
            totalBytes += existingBytes;

        using var stream = await response.Content.ReadAsStreamAsync(ct);

        // 续传时 Append，否则 Create
        var fileMode = existingBytes > 0 && File.Exists(outputPath)
            ? FileMode.Append
            : FileMode.Create;

        using var fileStream = new FileStream(outputPath, fileMode, FileAccess.Write, FileShare.None, 8192);
        var buffer = new byte[8192];
        var downloaded = existingBytes;
        int bytesRead;

        // 速度计算
        var lastBytes = existingBytes;
        var lastTime = DateTime.UtcNow;
        var speedText = "";

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloaded += bytesRead;

            // 每 500ms 更新一次速度
            var now = DateTime.UtcNow;
            var elapsed = (now - lastTime).TotalSeconds;
            if (elapsed >= 0.5)
            {
                var bytesPerSecond = (downloaded - lastBytes) / elapsed;
                speedText = FormatSpeed((long)bytesPerSecond);
                lastBytes = downloaded;
                lastTime = now;
            }

            onProgress(totalBytes, downloaded, speedText);
        }
    }

    /// <summary>
    /// ffmpeg 合并视频和音频（委托给 FfmpegService）
    /// </summary>
    public async Task MergeAsync(string videoPath, string audioPath, string outputPath, CancellationToken ct = default)
    {
        await FfmpegService.MergeAsync(videoPath, audioPath, outputPath, ct);
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
