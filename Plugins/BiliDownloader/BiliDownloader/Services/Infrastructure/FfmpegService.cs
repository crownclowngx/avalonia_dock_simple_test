using System.Diagnostics;

namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// ffmpeg 管理服务：实例级路径状态、路径验证和可取消合并。
/// </summary>
public sealed class FfmpegService : IFfmpegService
{
    private readonly IFfmpegProcessFactory _processFactory;

    public FfmpegService(IFfmpegProcessFactory processFactory)
    {
        _processFactory = processFactory;
    }

    public string? CustomPath { get; set; }

    public bool IsReady => ResolvedPath is not null;

    public string? ResolvedPath => ResolveFfmpegPath();

    public string? ResolveFfmpegPath()
    {
        if (!string.IsNullOrEmpty(CustomPath) && File.Exists(CustomPath))
        {
            return CustomPath;
        }

        try
        {
            var assemblyDir = Path.GetDirectoryName(typeof(FfmpegService).Assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                var found = Directory.GetFiles(assemblyDir, "ffmpeg.exe", SearchOption.AllDirectories);
                if (found.Length > 0)
                {
                    return found[0];
                }
            }
        }
        catch
        {
            // 插件目录不可访问时继续检查 PATH。
        }

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in pathDirs)
        {
            var trimmed = dir.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(trimmed, "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // 忽略单个无效 PATH 项。
            }
        }

        return null;
    }

    public async Task<bool> ValidatePathAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-version");

            using var process = _processFactory.Start(startInfo);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var stdout = process.ReadStandardOutputAsync(timeout.Token);
            var stderr = process.ReadStandardErrorAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(stdout, stderr);
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task MergeAsync(
        string videoPath,
        string audioPath,
        string outputPath,
        CancellationToken ct = default)
    {
        var ffmpegPath = ResolveFfmpegPath();
        if (ffmpegPath is null)
        {
            throw new InvalidOperationException(
                "ffmpeg 未就绪，请在调度器工具中配置 ffmpeg 路径或等待自动下载完成");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-nostats", "-loglevel", "warning",
            "-i", videoPath, "-i", audioPath,
            "-c", "copy", "-shortest", outputPath,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = _processFactory.Start(startInfo);
        using var cancellation = ct.Register(() => TryKill(process));
        var stdout = process.ReadStandardOutputAsync(CancellationToken.None);
        var stderr = process.ReadStandardErrorAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0)
            {
                DeleteIncompleteOutput(outputPath);
                throw new InvalidOperationException(
                    $"ffmpeg 合并失败 (exit {process.ExitCode}): {await stderr}");
            }
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await DrainOutputAsync(stdout, stderr);
            DeleteIncompleteOutput(outputPath);
            throw;
        }
        catch
        {
            TryKill(process);
            await DrainOutputAsync(stdout, stderr);
            DeleteIncompleteOutput(outputPath);
            throw;
        }
    }

    private static void TryKill(IFfmpegProcess process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 进程可能已在检查与 Kill 之间退出。
        }
    }

    private static void DeleteIncompleteOutput(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
        catch
        {
            // 原始合并错误优先。
        }
    }

    private static async Task DrainOutputAsync(params Task<string>[] outputTasks)
    {
        try
        {
            await Task.WhenAll(outputTasks);
        }
        catch
        {
            // 原始进程错误或取消优先。
        }
    }
}
