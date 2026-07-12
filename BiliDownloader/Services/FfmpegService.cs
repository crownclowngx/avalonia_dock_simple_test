using System.Diagnostics;

namespace BiliDownloader.Services;

/// <summary>
/// ffmpeg 管理服务：本地路径发现、路径验证
/// </summary>
public static class FfmpegService
{
    /// <summary>
    /// 本地 ffmpeg 存放根目录（单独打包，含子目录递归查找）
    /// </summary>
    private const string LocalFfmpegDir = @"D:\soft\FFMEPG";

    /// <summary>
    /// 自定义路径（用户在 Tool 中手动设置时使用）
    /// </summary>
    public static string? CustomPath { get; set; }

    /// <summary>
    /// ffmpeg 是否就绪
    /// </summary>
    public static bool IsReady => ResolveFfmpegPath() != null;

    /// <summary>
    /// 查找 ffmpeg 路径。优先级：CustomPath -> 本地目录(D:\soft\FFMEPG) -> 插件目录 -> 系统 PATH
    /// </summary>
    public static string? ResolveFfmpegPath()
    {
        // 1. 用户自定义路径
        if (!string.IsNullOrEmpty(CustomPath) && File.Exists(CustomPath))
            return CustomPath;

        // 2. 本地 ffmpeg 目录（D:\soft\FFMEPG，递归子目录）
        try
        {
            if (Directory.Exists(LocalFfmpegDir))
            {
                var found = Directory.GetFiles(LocalFfmpegDir, "ffmpeg.exe", SearchOption.AllDirectories);
                if (found.Length > 0)
                    return found[0];
            }
        }
        catch { /* 忽略本地目录查找失败 */ }

        // 3. 插件目录（程序集所在目录及其子目录）
        try
        {
            var assemblyDir = Path.GetDirectoryName(typeof(FfmpegService).Assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                var found = Directory.GetFiles(assemblyDir, "ffmpeg.exe", SearchOption.AllDirectories);
                if (found.Length > 0)
                    return found[0];
            }
        }
        catch { /* 忽略插件目录查找失败 */ }

        // 4. 系统 PATH
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
