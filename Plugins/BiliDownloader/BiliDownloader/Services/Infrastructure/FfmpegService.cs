using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BiliDownloader.Models;

namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// ffmpeg 本地运行时适配器：实现路径发现、进程探测与媒体封装。
/// 安装职责位于 <see cref="FfmpegPackageInstaller"/>；这里仅消费已经存在的运行时，
/// 因而提交预检和媒体下载不会意外触发网络请求或磁盘安装。
/// </summary>
public sealed class FfmpegService : IFfmpegService
{
    private readonly IFfmpegProcessFactory _processFactory;
    private readonly IBiliDataPaths _paths;
    private string? _customPath;
    private FfmpegRuntimeStatus? _lastStatus;
    private (string ExecutablePath, MediaMuxerCapabilities Capabilities)? _lastMuxerCapabilities;

    public FfmpegService(IFfmpegProcessFactory processFactory, IBiliDataPaths? paths = null)
    {
        _processFactory = processFactory;
        _paths = paths ?? new BiliDataPaths();
    }

    public string? CustomPath
    {
        get => _customPath;
        set
        {
            _customPath = value;
            _lastStatus = null;
            _lastMuxerCapabilities = null;
        }
    }

    // 严格就绪状态只能来自进程探测，不能把一个同名空文件误报为可用运行时。
    public bool IsReady => _lastStatus?.IsReady == true;
    public string? ResolvedPath => ResolveFfmpegPath();

    public string? ResolveFfmpegPath()
        => EnumerateCandidates().Select(candidate => candidate.Path).FirstOrDefault(File.Exists);

    public async Task<FfmpegRuntimeStatus> DetectAsync(CancellationToken ct = default)
    {
        foreach (var candidate in EnumerateCandidates().Where(candidate => File.Exists(candidate.Path)))
        {
            ct.ThrowIfCancellationRequested();
            var probe = await ProbeAsync(candidate.Path, ct);
            if (probe.IsValid)
            {
                return _lastStatus = new(true, candidate.Path, probe.Version, candidate.Source,
                    $"ffmpeg {probe.Version ?? "未知版本"} 已就绪");
            }
        }

        return _lastStatus = new(false, null, null, FfmpegRuntimeSource.None,
            "未找到可用的 ffmpeg，可安装内置版本或选择自定义路径。");
    }

    public async Task<bool> ValidatePathAsync(string path, CancellationToken ct = default)
        => (await ProbeAsync(path, ct)).IsValid;

    public async Task MergeAsync(
        string videoPath,
        string audioPath,
        string outputPath,
        CancellationToken ct = default)
        => await MuxCoreAsync(new MediaMuxRequest(
            videoPath, audioPath, outputPath, OutputContainer.Mp4, OutputMediaMode.AudioVideo),
            legacyArguments: true, ct);

    public async Task MuxAsync(MediaMuxRequest request, CancellationToken ct = default)
        => await MuxCoreAsync(request, legacyArguments: false, ct);

    private async Task MuxCoreAsync(
        MediaMuxRequest request,
        bool legacyArguments,
        CancellationToken ct)
    {
        ValidateMuxRequest(request);
        // 合并只能消费最近一次通过进程验证的路径；配置变化会清空该状态。若尚未探测，则在本地
        // 重新枚举所有候选。这样“存在但损坏”的自定义文件不会遮蔽后面的托管可用版本。
        var runtime = _lastStatus?.IsReady == true ? _lastStatus : await DetectAsync(ct);
        var ffmpegPath = runtime.ExecutablePath;
        if (!runtime.IsReady || string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new FfmpegUnavailableException("ffmpeg 未就绪，请安装内置版本或选择有效的自定义路径。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-nostats", "-loglevel", "warning",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(request.VideoPath!);
        if (request.OutputMediaMode == OutputMediaMode.AudioVideo)
        {
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(request.AudioPath!);
        }
        if (!legacyArguments)
        {
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0:v:0");
            if (request.OutputMediaMode == OutputMediaMode.AudioVideo)
            {
                startInfo.ArgumentList.Add("-map");
                startInfo.ArgumentList.Add("1:a:0");
            }
            else
            {
                startInfo.ArgumentList.Add("-an");
            }
        }
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("copy");
        if (request.OutputMediaMode == OutputMediaMode.AudioVideo)
            startInfo.ArgumentList.Add("-shortest");
        startInfo.ArgumentList.Add(request.OutputPath);

        IFfmpegProcess process;
        try
        {
            process = _processFactory.Start(startInfo);
        }
        catch (Exception ex)
        {
            throw new FfmpegUnavailableException("无法启动 ffmpeg，请重新检测或修复运行时。", ex);
        }

        using (process)
        using (ct.Register(() => TryKill(process)))
        {
            var stdout = process.ReadStandardOutputAsync(CancellationToken.None);
            var stderr = process.ReadStandardErrorAsync(CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(ct);
                await Task.WhenAll(stdout, stderr);
                if (process.ExitCode != 0)
                {
                    DeleteIncompleteOutput(request.OutputPath);
                    throw new MediaMergeException(
                        $"ffmpeg 合并失败（退出码 {process.ExitCode}）：{await stderr}");
                }
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await DrainOutputAsync(stdout, stderr);
                DeleteIncompleteOutput(request.OutputPath);
                throw;
            }
            catch (MediaMergeException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TryKill(process);
                await DrainOutputAsync(stdout, stderr);
                DeleteIncompleteOutput(request.OutputPath);
                throw new MediaMergeException("ffmpeg 合并过程异常终止，已保留输入媒体。", ex);
            }
        }
    }

    /// <summary>
    /// 使用 ffmpeg 自身的 muxer 列表验证容器能力。结果按已验证可执行路径缓存；
    /// 自定义路径变化时缓存立即失效，防止沿用另一套运行时的能力事实。
    /// </summary>
    public async Task<MediaMuxerCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var runtime = _lastStatus?.IsReady == true ? _lastStatus : await DetectAsync(ct);
        if (!runtime.IsReady || string.IsNullOrWhiteSpace(runtime.ExecutablePath))
            return new MediaMuxerCapabilities(false, false);
        if (_lastMuxerCapabilities is { } cached
            && string.Equals(cached.ExecutablePath, runtime.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            return cached.Capabilities;

        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-muxers");
        using var process = _processFactory.Start(startInfo);
        using var registration = ct.Register(() => TryKill(process));
        var stdout = process.ReadStandardOutputAsync(CancellationToken.None);
        var stderr = process.ReadStandardErrorAsync(CancellationToken.None);
        await process.WaitForExitAsync(ct);
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0) return new MediaMuxerCapabilities(false, false);
        var text = await stdout;
        var capabilities = new MediaMuxerCapabilities(
            text.Contains(" mp4 ", StringComparison.OrdinalIgnoreCase)
            || text.Contains(" mov,mp4", StringComparison.OrdinalIgnoreCase),
            text.Contains(" matroska", StringComparison.OrdinalIgnoreCase));
        _lastMuxerCapabilities = (runtime.ExecutablePath, capabilities);
        return capabilities;
    }

    private static void ValidateMuxRequest(MediaMuxRequest request)
    {
        if (request.OutputMediaMode == OutputMediaMode.AudioOnly)
            throw new ArgumentException("NativeAudio 不经过 ffmpeg 封装。", nameof(request));
        if (string.IsNullOrWhiteSpace(request.VideoPath))
            throw new ArgumentException("视频输出缺少视频输入。", nameof(request));
        if (request.OutputMediaMode == OutputMediaMode.AudioVideo && string.IsNullOrWhiteSpace(request.AudioPath))
            throw new ArgumentException("音视频输出缺少音频输入。", nameof(request));
        if (request.OutputContainer is not (OutputContainer.Mp4 or OutputContainer.Mkv))
            throw new ArgumentException("ffmpeg 封装只接受 MP4 或 MKV。", nameof(request));
    }

    private IEnumerable<(string Path, FfmpegRuntimeSource Source)> EnumerateCandidates()
    {
        if (!string.IsNullOrWhiteSpace(CustomPath))
            yield return (CustomPath, FfmpegRuntimeSource.Custom);

        var managed = TryReadManagedPath();
        if (!string.IsNullOrWhiteSpace(managed))
            yield return (managed, FfmpegRuntimeSource.Managed);

        var assemblyDir = Path.GetDirectoryName(typeof(FfmpegService).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDir))
        {
            // 插件目录只检查约定位置，避免旧实现递归扫描整个宿主目录造成不可控延迟。
            yield return (Path.Combine(assemblyDir, "ffmpeg.exe"), FfmpegRuntimeSource.Plugin);
            yield return (Path.Combine(assemblyDir, "ffmpeg", "bin", "ffmpeg.exe"), FfmpegRuntimeSource.Plugin);
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string? candidate = null;
            try
            {
                candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
            }
            catch
            {
                // 单个损坏的 PATH 项不能阻止后续候选探测。
            }
            if (!string.IsNullOrWhiteSpace(candidate))
                yield return (candidate, FfmpegRuntimeSource.Path);
        }
    }

    private string? TryReadManagedPath()
    {
        try
        {
            if (!File.Exists(_paths.FfmpegCurrentPointerPath)) return null;
            var pointer = JsonSerializer.Deserialize<FfmpegInstallPointer>(
                File.ReadAllText(_paths.FfmpegCurrentPointerPath));
            if (pointer is null || string.IsNullOrWhiteSpace(pointer.RelativeExecutablePath)) return null;

            var root = Path.GetFullPath(_paths.FfmpegDependencyDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(
                _paths.FfmpegDependencyDirectory, pointer.RelativeExecutablePath));
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? candidate : null;
        }
        catch
        {
            // 指针损坏时回退到插件目录和 PATH；修复命令会创建新的可信指针。
            return null;
        }
    }

    private async Task<(bool IsValid, string? Version)> ProbeAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return (false, null);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
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
                var text = await stdout;
                if (process.ExitCode != 0 || !text.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase))
                    return (false, null);
                var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                var version = firstLine?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(2).FirstOrDefault();
                return (true, version);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return (false, null);
            }
        }
        catch
        {
            return (false, null);
        }
    }

    private static void TryKill(IFfmpegProcess process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // 进程可能已在状态检查和 Kill 之间退出。
        }
    }

    private static void DeleteIncompleteOutput(string outputPath)
    {
        try { if (File.Exists(outputPath)) File.Delete(outputPath); }
        catch { /* 原始合并错误优先，清理失败由日志诊断。 */ }
    }

    private static async Task DrainOutputAsync(params Task<string>[] outputTasks)
    {
        try { await Task.WhenAll(outputTasks); }
        catch { /* 原始进程错误或取消优先。 */ }
    }
}

/// <summary>托管安装活动指针；只保存依赖根目录下的相对路径，禁止写入任意绝对路径。</summary>
public sealed record FfmpegInstallPointer(
    string Version,
    string RelativeExecutablePath,
    string PackageSha256,
    DateTimeOffset ActivatedAt);
