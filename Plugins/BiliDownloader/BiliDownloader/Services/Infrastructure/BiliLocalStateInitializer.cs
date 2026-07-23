namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// 初始化 G1 的全新本地存储纪元。
/// </summary>
public interface IBiliLocalStateInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// G1 明确不迁移旧任务、设置、凭据、日志和临时文件。
/// 标记写入前的失败会在下次启动继续清理，避免新旧状态混用。
/// </summary>
public sealed class BiliLocalStateInitializer : IBiliLocalStateInitializer
{
    private const string EpochValue = "2";
    private readonly IBiliDataPaths _paths;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public BiliLocalStateInitializer(IBiliDataPaths paths)
    {
        _paths = paths;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            if (File.Exists(_paths.StorageEpochMarkerPath))
            {
                _initialized = true;
                EnsureRuntimeDirectories();
                return;
            }

            foreach (var directory in _paths.ResetDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var safeDirectory = ValidateResetDirectory(directory);
                if (Directory.Exists(safeDirectory))
                {
                    Directory.Delete(safeDirectory, recursive: true);
                }
            }

            EnsureRuntimeDirectories();

            var markerTempPath = _paths.StorageEpochMarkerPath + ".tmp";
            await File.WriteAllTextAsync(markerTempPath, EpochValue, cancellationToken);
            File.Move(markerTempPath, _paths.StorageEpochMarkerPath, overwrite: true);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private void EnsureRuntimeDirectories()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        Directory.CreateDirectory(_paths.LogDirectory);
        Directory.CreateDirectory(_paths.TempDirectory);
    }

    private static string ValidateResetDirectory(string directory)
    {
        var fullPath = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(fullPath)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(fullPath)
            || string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                Path.GetFileName(fullPath),
                "BiliDownloader",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"拒绝清理非 BiliDownloader 专属目录: {fullPath}");
        }

        return fullPath;
    }
}
