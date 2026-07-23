namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// BiliDownloader 本地数据路径。所有持久化服务共享这一处路径决策，
/// 避免各自假定 Windows AppData 目录。
/// </summary>
public interface IBiliDataPaths
{
    string DataDirectory { get; }
    string LogDirectory { get; }
    string TempDirectory { get; }
    string DownloadTaskDatabasePath { get; }
    string CredentialDatabasePath { get; }
    string CredentialKeyPath { get; }
    string StorageEpochMarkerPath { get; }
    IReadOnlyList<string> ResetDirectories { get; }
}

/// <summary>
/// Windows 与 Linux 桌面环境的默认路径实现。
/// </summary>
public sealed class BiliDataPaths : IBiliDataPaths
{
    private const string ApplicationDirectoryName = "BiliDownloader";

    public BiliDataPaths()
    {
        if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            DataDirectory = CombineXdgPath("XDG_DATA_HOME", Path.Combine(home, ".local", "share"));
            LogDirectory = Path.Combine(
                CombineXdgPath("XDG_STATE_HOME", Path.Combine(home, ".local", "state")),
                "logs");
            TempDirectory = Path.Combine(
                CombineXdgPath("XDG_CACHE_HOME", Path.Combine(home, ".cache")),
                "temp");

            ResetDirectories = DistinctDirectories(
                DataDirectory,
                Path.GetDirectoryName(LogDirectory),
                Path.GetDirectoryName(TempDirectory));
        }
        else
        {
            DataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ApplicationDirectoryName);
            LogDirectory = Path.Combine(DataDirectory, "logs");
            TempDirectory = Path.Combine(DataDirectory, "temp");

            // 旧版本把所有数据放在 Roaming AppData。首次切换时一并清理。
            var legacyDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ApplicationDirectoryName);
            ResetDirectories = DistinctDirectories(DataDirectory, legacyDataDirectory);
        }

        DownloadTaskDatabasePath = Path.Combine(DataDirectory, "bili_download_tasks.db");
        CredentialDatabasePath = Path.Combine(DataDirectory, "credentials.db");
        CredentialKeyPath = Path.Combine(DataDirectory, "credential.key");
        StorageEpochMarkerPath = Path.Combine(DataDirectory, "storage_epoch_v2");
    }

    public string DataDirectory { get; }
    public string LogDirectory { get; }
    public string TempDirectory { get; }
    public string DownloadTaskDatabasePath { get; }
    public string CredentialDatabasePath { get; }
    public string CredentialKeyPath { get; }
    public string StorageEpochMarkerPath { get; }
    public IReadOnlyList<string> ResetDirectories { get; }

    private static string CombineXdgPath(string variableName, string fallbackRoot)
    {
        var configuredRoot = Environment.GetEnvironmentVariable(variableName);
        var root = string.IsNullOrWhiteSpace(configuredRoot) ? fallbackRoot : configuredRoot;
        return Path.Combine(root, ApplicationDirectoryName);
    }

    private static IReadOnlyList<string> DistinctDirectories(params string?[] directories)
        => directories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
