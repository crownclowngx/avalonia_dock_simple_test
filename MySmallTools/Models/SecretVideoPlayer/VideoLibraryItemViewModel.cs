using MySmallTools.Business.SecretVideoPlayer;

namespace MySmallTools.Models.SecretVideoPlayer;

/// <summary>
/// 视频库列表中的单个 SECVID03 文件。
/// </summary>
public sealed class VideoLibraryItemViewModel
{
    public VideoLibraryItemViewModel(VideoLibraryScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        FilePath = result.FilePath;
        FileNameWithoutExtension = result.FileNameWithoutExtension;
        PublicTitle = result.PublicTitle;
        PublicDescription = result.PublicDescription;
        MetadataState = result.State;
        ErrorMessage = result.ErrorMessage;
    }

    public string FilePath { get; }
    public string FileNameWithoutExtension { get; }
    public string PublicTitle { get; }
    public string PublicDescription { get; }
    public VideoLibraryMetadataState MetadataState { get; }
    public string ErrorMessage { get; }
    public bool HasError => MetadataState == VideoLibraryMetadataState.Failed;
    public bool HasPublicTitle => !string.IsNullOrWhiteSpace(PublicTitle);

    public string DisplayName => string.IsNullOrWhiteSpace(PublicTitle)
        ? FileNameWithoutExtension
        : $"{FileNameWithoutExtension}（{PublicTitle}）";
}
