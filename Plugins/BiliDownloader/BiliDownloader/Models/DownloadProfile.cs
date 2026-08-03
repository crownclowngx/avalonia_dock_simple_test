namespace BiliDownloader.Models;

/// <summary>Complete reusable download intent, independent from UI controls.</summary>
public sealed record DownloadProfile(
    string QualityPreference,
    int AudioQualityId,
    bool UseGroupFolder,
    bool AddIndexToTitle,
    bool DownloadDanmaku,
    bool DownloadSubtitle,
    bool DownloadCover,
    string NamingTemplate,
    string OutputDirectory,
    FileConflictPolicy ConflictPolicy = FileConflictPolicy.AutoNumber)
{
    public static DownloadProfile Default { get; } = new(
        "720p", 0, false, true, false, false, false, "{index}.{title}", "",
        FileConflictPolicy.AutoNumber);
}
