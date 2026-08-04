using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// 编译期固定的 ffmpeg 供应链清单。版本、下载地址和摘要必须在同一次代码评审中更新，
/// 运行时不得从下载站读取“最新哈希”，否则被篡改的包和哈希可能同时被接受。
/// </summary>
public sealed record FfmpegPackageManifest(string Version, Uri DownloadUri, string Sha256)
{
    /// <summary>
    /// 经过代码评审固定的 Windows x64 essentials 清单。更新此值时必须同时核对版本、URL 与摘要，
    /// 不允许只替换其中一个字段后继续信任运行时下载内容。
    /// </summary>
    public static FfmpegPackageManifest GyanReleaseEssentials812 { get; } = new(
        "8.1.2",
        new Uri("https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip"),
        "db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec");
}

/// <summary>安装进度采用稳定阶段和值，UI 不需要解析中文状态文本。</summary>
public sealed record FfmpegInstallProgress(string Stage, double Percentage, string Message);

/// <summary>安装结果显式表达成功、活动路径和失败原因，预期失败不使用异常控制 UI 分支。</summary>
public sealed record FfmpegInstallResult(bool Success, string Message, string? ExecutablePath = null)
{
    public static FfmpegInstallResult Failed(string message) => new(false, message);
}

/// <summary>用户主动安装或修复 ffmpeg 的应用边界。</summary>
public interface IFfmpegPackageInstaller
{
    /// <summary>当前是否有安装事务持有互斥锁；该值只用于 UI 禁用重复按钮。</summary>
    bool IsInstalling { get; }
    /// <summary>安装阶段或进度变化通知；订阅者不得用展示文本驱动业务状态。</summary>
    event Action<FfmpegInstallProgress>? ProgressChanged;
    /// <summary>
    /// 执行一次用户主动触发的安装或修复。预期校验失败以结果返回，取消继续传播
    /// <see cref="OperationCanceledException"/>，以便调用方区分失败和用户取消。
    /// </summary>
    Task<FfmpegInstallResult> InstallOrRepairAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 安装包网络传输边界。安装器只依赖目标文件和大小上限，测试可以使用内存 ZIP，
/// 不需要访问真实网络，也不会把 Bilibili 请求头带到第三方下载站。
/// </summary>
public interface IFfmpegPackageDownloader
{
    /// <summary>
    /// 将固定来源下载到本次事务独有的目标文件，并在写入过程中强制限制最大字节数。
    /// 实现不得自行解析版本、摘要或激活安装包。
    /// </summary>
    Task DownloadAsync(
        Uri source,
        string destination,
        long maximumBytes,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}

/// <summary>系统 HTTP 实现，使用响应头和实际读取字节双重执行大小限制。</summary>
public sealed class HttpFfmpegPackageDownloader : IFfmpegPackageDownloader, IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task DownloadAsync(
        Uri source,
        string destination,
        long maximumBytes,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0 and var declared && declared > maximumBytes)
            throw new InvalidDataException($"ffmpeg 安装包声明大小 {declared} 字节，超过安全上限。");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total = checked(total + read);
            if (total > maximumBytes)
                throw new InvalidDataException("ffmpeg 安装包实际大小超过安全上限。");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            if (response.Content.Headers.ContentLength is > 0 and var length)
                progress?.Report((double)total / length * 100);
        }
        await output.FlushAsync(cancellationToken);
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>隔离平台判断，使非 Windows 分支可以在任意 CI 系统上确定性测试。</summary>
public interface IFfmpegInstallPlatform
{
    /// <summary>当前平台和进程架构是否与编译期固定安装包完全匹配。</summary>
    bool SupportsManagedInstallation { get; }
}

public sealed class SystemFfmpegInstallPlatform : IFfmpegInstallPlatform
{
    public bool SupportsManagedInstallation
        => OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64;
}

/// <summary>
/// ffmpeg 可信安装 Facade。它集中维护“下载、固定摘要校验、安全解压、进程验证、原子激活”顺序，
/// 任何一步失败都不能修改当前活动指针，这是安装流程最重要的不变量。
/// </summary>
public sealed class FfmpegPackageInstaller : IFfmpegPackageInstaller
{
    internal const long MaximumPackageBytes = 256L * 1024 * 1024;
    internal const long MaximumExpandedBytes = 1024L * 1024 * 1024;

    private readonly IFfmpegPackageDownloader _downloader;
    private readonly IFfmpegRuntimeLocator _locator;
    private readonly IBiliDataPaths _paths;
    private readonly IFfmpegInstallPlatform _platform;
    private readonly FfmpegPackageManifest _manifest;
    private readonly SemaphoreSlim _installLock = new(1, 1);
    private int _isInstalling;

    public FfmpegPackageInstaller(
        IFfmpegPackageDownloader downloader,
        IFfmpegRuntimeLocator locator,
        IBiliDataPaths paths,
        IFfmpegInstallPlatform platform,
        FfmpegPackageManifest manifest)
    {
        _downloader = downloader;
        _locator = locator;
        _paths = paths;
        _platform = platform;
        _manifest = manifest;
    }

    public bool IsInstalling => Volatile.Read(ref _isInstalling) != 0;
    public event Action<FfmpegInstallProgress>? ProgressChanged;

    public async Task<FfmpegInstallResult> InstallOrRepairAsync(CancellationToken cancellationToken = default)
    {
        if (!_platform.SupportsManagedInstallation)
            return FfmpegInstallResult.Failed("内置安装仅支持 Windows x64，请选择适用于当前平台的 ffmpeg。");

        await _installLock.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref _isInstalling, 1);
        var operationId = Guid.NewGuid().ToString("N");
        var operationRoot = Path.Combine(_paths.TempDirectory, $"ffmpeg-install-{operationId}");
        var archivePath = Path.Combine(operationRoot, "package.zip.part");
        var extractedPath = Path.Combine(operationRoot, "extracted");
        var previousPointer = TryReadPointerText();
        var previousCustomPath = _locator.CustomPath;
        var pointerActivated = false;
        string? installedVersionDirectory = null;
        try
        {
            Directory.CreateDirectory(operationRoot);
            Report("download", 0, $"正在下载 ffmpeg {_manifest.Version} essentials…");
            var transferProgress = new Progress<double>(value =>
                Report("download", Math.Clamp(value, 0, 100) * 0.55, "正在下载 ffmpeg 安装包…"));
            await _downloader.DownloadAsync(
                _manifest.DownloadUri, archivePath, MaximumPackageBytes, transferProgress, cancellationToken);

            Report("verify", 58, "正在校验固定 SHA-256…");
            await VerifyHashAsync(archivePath, cancellationToken);

            Report("extract", 65, "正在安全解压安装包…");
            var extractedExecutable = ExtractSafely(archivePath, extractedPath, cancellationToken);

            Report("probe", 82, "正在验证 ffmpeg 可执行文件…");
            if (!await _locator.ValidatePathAsync(extractedExecutable, cancellationToken))
                throw new InvalidDataException("安装包中的 ffmpeg 未通过版本探测。");

            Directory.CreateDirectory(_paths.FfmpegDependencyDirectory);
            var versionsRoot = Path.Combine(_paths.FfmpegDependencyDirectory, "versions");
            Directory.CreateDirectory(versionsRoot);
            var versionDirectory = Path.Combine(versionsRoot, $"{_manifest.Version}-{operationId}");
            Directory.Move(extractedPath, versionDirectory);
            installedVersionDirectory = versionDirectory;

            var movedExecutable = Path.Combine(versionDirectory,
                Path.GetRelativePath(extractedPath, extractedExecutable));
            var relativeExecutable = Path.GetRelativePath(_paths.FfmpegDependencyDirectory, movedExecutable);
            var pointer = new FfmpegInstallPointer(
                _manifest.Version, relativeExecutable, _manifest.Sha256, DateTimeOffset.UtcNow);

            Report("activate", 92, "正在原子切换活动版本…");
            ActivatePointer(pointer, operationId);
            pointerActivated = true;
            // 用户主动安装内置版本意味着本次会话应切换到托管运行时；设置层会同步清空旧自定义配置。
            _locator.CustomPath = null;
            var detected = await _locator.DetectAsync(cancellationToken);
            if (!detected.IsReady || !Path.GetFullPath(detected.ExecutablePath!).Equals(
                    Path.GetFullPath(movedExecutable), StringComparison.OrdinalIgnoreCase))
            {
                // 指针已经原子写入，但探测不一致说明环境发生并发变化。此时必须进入统一回滚分支，
                // 只有旧指针恢复成功后才删除本次版本，避免指针指向已被清理的目录。
                throw new InvalidOperationException("活动版本切换后重新探测失败。");
            }

            Report("complete", 100, $"ffmpeg {_manifest.Version} 已安装并启用。");
            CleanupOldVersions(versionDirectory);
            return new(true, $"ffmpeg {_manifest.Version} 已安装并启用。", movedExecutable);
        }
        catch (OperationCanceledException)
        {
            if (pointerActivated)
            {
                if (RestorePointer(previousPointer, operationId) && installedVersionDirectory is not null)
                    TryDeleteDirectory(installedVersionDirectory);
                _locator.CustomPath = previousCustomPath;
            }
            Report("cancelled", 0, "ffmpeg 安装已取消，原有版本未改变。");
            throw;
        }
        catch (Exception ex)
        {
            var message = SensitiveDataSanitizer.Sanitize(ex.Message);
            if (pointerActivated)
            {
                if (RestorePointer(previousPointer, operationId) && installedVersionDirectory is not null)
                    TryDeleteDirectory(installedVersionDirectory);
                _locator.CustomPath = previousCustomPath;
            }
            Report("failed", 0, $"ffmpeg 安装失败：{message}");
            return FfmpegInstallResult.Failed($"安装失败：{message}。原有可用版本未改变。");
        }
        finally
        {
            TryDeleteDirectory(operationRoot);
            Interlocked.Exchange(ref _isInstalling, 0);
            _installLock.Release();
        }
    }

    private async Task VerifyHashAsync(string archivePath, CancellationToken cancellationToken)
    {
        var expected = Convert.FromHexString(_manifest.Sha256);
        await using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken);
        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new InvalidDataException("ffmpeg 安装包 SHA-256 与内置信任清单不一致。");
    }

    private static string ExtractSafely(string archivePath, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var executables = new List<string>();
        long expandedBytes = 0;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.FullName)) continue;
            var normalizedName = entry.FullName.Replace('\\', '/');
            if (normalizedName.StartsWith('/') || Path.IsPathRooted(normalizedName))
                throw new InvalidDataException("安装包包含绝对路径条目。");
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("安装包包含不允许的符号链接条目。");

            var target = Path.GetFullPath(Path.Combine(destination,
                normalizedName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装包包含越过目标目录的路径。");
            if (!targets.Add(target))
                throw new InvalidDataException("安装包包含重复目标路径。");

            if (normalizedName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaximumExpandedBytes)
                throw new InvalidDataException("ffmpeg 安装包解压后大小超过安全上限。");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            if (normalizedName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                executables.Add(target);
        }

        if (executables.Count != 1)
            throw new InvalidDataException("安装包必须且只能包含一个 bin/ffmpeg.exe。");
        return executables[0];
    }

    private void ActivatePointer(FfmpegInstallPointer pointer, string operationId)
    {
        Directory.CreateDirectory(_paths.FfmpegDependencyDirectory);
        var temporaryPointer = Path.Combine(_paths.FfmpegDependencyDirectory, $"current-{operationId}.json.tmp");
        File.WriteAllText(temporaryPointer, JsonSerializer.Serialize(pointer, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        // 临时指针与正式指针位于同一目录，File.Move(overwrite:true) 才能提供同卷原子替换语义。
        File.Move(temporaryPointer, _paths.FfmpegCurrentPointerPath, overwrite: true);
    }

    private string? TryReadPointerText()
    {
        try
        {
            return File.Exists(_paths.FfmpegCurrentPointerPath)
                ? File.ReadAllText(_paths.FfmpegCurrentPointerPath)
                : null;
        }
        catch
        {
            // 旧指针不可读时不能把它视为可恢复版本；安装仍可创建新的可信指针。
            return null;
        }
    }

    private bool RestorePointer(string? previousPointer, string operationId)
    {
        try
        {
            if (previousPointer is null)
            {
                if (File.Exists(_paths.FfmpegCurrentPointerPath))
                    File.Delete(_paths.FfmpegCurrentPointerPath);
                return true;
            }
            var rollback = Path.Combine(_paths.FfmpegDependencyDirectory, $"rollback-{operationId}.json.tmp");
            File.WriteAllText(rollback, previousPointer);
            File.Move(rollback, _paths.FfmpegCurrentPointerPath, overwrite: true);
            return true;
        }
        catch
        {
            // 回滚失败会保留已通过固定哈希和进程验证的新版本；错误仍会被返回并写入日志。
            // 该分支只可能由文件系统级故障触发，不能用删除整个依赖目录扩大损害。
            return false;
        }
    }

    private void CleanupOldVersions(string activeDirectory)
    {
        try
        {
            var versionsRoot = Path.Combine(_paths.FfmpegDependencyDirectory, "versions");
            var inactive = Directory.GetDirectories(versionsRoot)
                .Where(path => !Path.GetFullPath(path).Equals(Path.GetFullPath(activeDirectory),
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .Skip(1);
            foreach (var directory in inactive) TryDeleteDirectory(directory);
        }
        catch
        {
            // 旧进程可能仍占用 exe；清理是优化，不得影响已完成的活动指针切换。
        }
    }

    private void Report(string stage, double percentage, string message)
        => ProgressChanged?.Invoke(new(stage, percentage, message));

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* 失败残留位于专用缓存/版本目录，下次修复仍使用新的唯一目录。 */ }
    }
}
