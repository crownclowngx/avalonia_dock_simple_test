namespace MySmallTools.Business.SecretVideoPlayer.Library;

public enum VideoLibraryMetadataState
{
    Ready,
    Failed
}

public sealed record VideoLibraryScanResult(
    string FilePath,
    string FileNameWithoutExtension,
    string PublicTitle,
    string PublicDescription,
    VideoLibraryMetadataState State,
    string ErrorMessage);

public interface IVideoLibraryScanner
{
    IAsyncEnumerable<VideoLibraryScanResult> ScanAsync(
        string folderPath,
        CancellationToken cancellationToken);
}
