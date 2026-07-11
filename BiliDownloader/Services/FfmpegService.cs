using System.Diagnostics;

namespace BiliDownloader.Services;

/// <summary>
/// ffmpeg 管理服务：发现、自动下载、路径验证
/// </summary>
public static class FfmpegService
{
    /// <summary>
    /// 自动下载目录: %AppData%/BiliDownloader/ffmpeg/
    /// </summary>
    private static readonly string DefaultDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BiliDownloader", "ffmpeg");

    /// <summary>
    /// GitHub Release 下载 URL（BtbN 构建的 Windows x64 ffmpeg）
    /// </summary>
    private const string DownloadUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    /// <summary>
    /// 自定义路径（用户在 Tool 中手动设置时使用）
    /// </summary>
    public static string? CustomPath { get; set; }

    /// <summary>
    /// ffmpeg 是否就绪
    /// </summary>
    public static bool IsReady => ResolveFfmpegPath() != null;

    /// <summary>
    /// 查找 ffmpeg 路径。优先级：CustomPath -> 自动下载目录 -> 系统 PATH
    /// </summary>
    public static string? ResolveFfmpegPath()
    {
        // 1. 用户自定义路径
        if (!string.IsNullOrEmpty(CustomPath) && File.Exists(CustomPath))
            return CustomPath;

        // 2. 自动下载目录
        var autoPath = Path.Combine(DefaultDir, "ffmpeg.exe");
        if (File.Exists(autoPath))
            return autoPath;

        // 3. 系统 PATH
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in pathDirs)
        {
            var trimmed = dir.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            try
            {
                var candidate = Path.Combine(trimmed, "ffmpeg.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { /* 忽略无效路径 */ }
        }

        return null;
    }

    /// <summary>
    /// 从 GitHub 自动下载 ffmpeg（BtbN 构建）
    /// </summary>
    public static async Task EnsureDownloadedAsync(Action<string>? onStatus = null, CancellationToken ct = default)
    {
        if (IsReady)
        {
            onStatus?.Invoke("ffmpeg 已就绪");
            return;
        }

        onStatus?.Invoke("正在下载 ffmpeg（约 80MB）...");
        Directory.CreateDirectory(DefaultDir);

        var zipPath = Path.Combine(DefaultDir, "ffmpeg.zip");

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);
            httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            // 下载 zip 文件
            using var response = await httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192);

            var buffer = new byte[8192];
            long downloaded = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloaded += bytesRead;
                if (totalBytes > 0)
                {
                    var pct = (double)downloaded / totalBytes * 100;
                    onStatus?.Invoke($"下载 ffmpeg: {pct:F0}% ({downloaded / (1024 * 1024)}MB / {totalBytes / (1024 * 1024)}MB)");
                }
            }

            onStatus?.Invoke("正在解压 ffmpeg...");

            // 解压 zip，找到 ffmpeg.exe
            await ExtractFfmpegFromZip(zipPath, DefaultDir);

            // 清理 zip
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            onStatus?.Invoke(IsReady ? "ffmpeg 下载完成" : "ffmpeg 解压后未找到可执行文件");
        }
        catch (OperationCanceledException)
        {
            onStatus?.Invoke("ffmpeg 下载已取消");
            // 清理部分下载的文件
            if (File.Exists(zipPath))
                try { File.Delete(zipPath); } catch { }
            throw;
        }
        catch (Exception ex)
        {
            onStatus?.Invoke($"ffmpeg 下载失败: {ex.Message}");
            if (File.Exists(zipPath))
                try { File.Delete(zipPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// 从 zip 中提取 ffmpeg.exe（zip 内有嵌套目录结构）
    /// </summary>
    private static async Task ExtractFfmpegFromZip(string zipPath, string targetDir)
    {
        await Task.Run(() =>
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, targetDir, true);

            // BtbN 构建的 zip 内有 ffmpeg-master-latest-win64-gpl/bin/ffmpeg.exe 的嵌套结构
            // 需要找到 ffmpeg.exe 并移动到 targetDir 根目录
            var ffmpegExe = FindFileInDirectory(targetDir, "ffmpeg.exe");
            if (ffmpegExe != null)
            {
                var finalPath = Path.Combine(targetDir, "ffmpeg.exe");
                if (!string.Equals(ffmpegExe, finalPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(ffmpegExe, finalPath, true);
                }
            }

            // 同样提取 ffprobe.exe（如果存在）
            var ffprobeExe = FindFileInDirectory(targetDir, "ffprobe.exe");
            if (ffprobeExe != null)
            {
                var finalPath = Path.Combine(targetDir, "ffprobe.exe");
                if (!string.Equals(ffprobeExe, finalPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(ffprobeExe, finalPath, true);
                }
            }
        });
    }

    /// <summary>
    /// 递归查找目录中的文件
    /// </summary>
    private static string? FindFileInDirectory(string dir, string fileName)
    {
        try
        {
            var found = Directory.GetFiles(dir, fileName, SearchOption.AllDirectories);
            return found.Length > 0 ? found[0] : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 验证指定路径是否为有效的 ffmpeg 可执行文件
    /// </summary>
    public static async Task<bool> ValidatePathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var exited = process.WaitForExit(5000);
            return exited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
