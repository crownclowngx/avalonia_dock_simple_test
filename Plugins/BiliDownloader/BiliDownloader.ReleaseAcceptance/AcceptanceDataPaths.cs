using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.ReleaseAcceptance;

/// <summary>
/// 将真实验收产生的数据库、日志、下载和 ffmpeg 安装全部限制在 artifacts 沙箱中。
/// 正式验收需要真实依赖，但绝不能读取、迁移或覆盖操作者日常使用的本地数据。
/// </summary>
internal sealed class AcceptanceDataPaths : IBiliDataPaths
{
    public AcceptanceDataPaths(string root)
    {
        DataDirectory = Path.Combine(Path.GetFullPath(root), "data");
        LogDirectory = Path.Combine(Path.GetFullPath(root), "logs");
        TempDirectory = Path.Combine(Path.GetFullPath(root), "temp");
        DownloadTaskDatabasePath = Path.Combine(DataDirectory, "bili_download_tasks.db");
        CredentialDatabasePath = Path.Combine(DataDirectory, "credentials.db");
        CredentialKeyPath = Path.Combine(DataDirectory, "credential.key");
        StorageEpochMarkerPath = Path.Combine(DataDirectory, "storage_epoch_v2");
        FfmpegDependencyDirectory = Path.Combine(DataDirectory, "dependencies", "ffmpeg");
        FfmpegCurrentPointerPath = Path.Combine(FfmpegDependencyDirectory, "current.json");
        ResetDirectories = [DataDirectory, LogDirectory, TempDirectory];
    }

    public string DataDirectory { get; }
    public string LogDirectory { get; }
    public string TempDirectory { get; }
    public string FfmpegDependencyDirectory { get; }
    public string FfmpegCurrentPointerPath { get; }
    public string DownloadTaskDatabasePath { get; }
    public string CredentialDatabasePath { get; }
    public string CredentialKeyPath { get; }
    public string StorageEpochMarkerPath { get; }
    public IReadOnlyList<string> ResetDirectories { get; }
}
