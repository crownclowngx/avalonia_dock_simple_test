using System.Runtime.CompilerServices;
using MySmallTools.Business.SecretVideoPlayer.Library;
using MySmallTools.ViewModels.SecretVideoPlayer;
using MySmallTools.ViewModels.SecretVideoPlayer.Decryption;
using MySmallTools.ViewModels.SecretVideoPlayer.Encryption;
using MySmallTools.ViewModels.SecretVideoPlayer.Library;
using MySmallTools.ViewModels.SecretVideoPlayer.Playback;
using Xunit;

namespace MySmallTools.Tests;

/// <summary>
/// 固定 G7.1 的兼容外壳和功能子包边界，防止后续 G8 又把职责搬回顶层类型。
/// </summary>
public sealed class G71UiCompositionTests
{
    [Fact]
    public void TopLevelDocumentsRemainCompatibleFeatureShells()
    {
        Assert.Equal(typeof(PlaybackCoordinatorViewModel), typeof(VideoPlayerControlViewModel).BaseType);
        Assert.Equal(typeof(LibraryBrowserCoordinatorViewModel), typeof(VideoLibraryBrowserViewModel).BaseType);
        Assert.Equal(typeof(LibraryDocumentCoordinatorViewModel), typeof(SecretVideoLibraryViewModel).BaseType);
        Assert.Equal(typeof(EncryptionBatchViewModel), typeof(VideoEncryptorViewModel).BaseType);
        Assert.Equal(typeof(DecryptionBatchViewModel), typeof(VideoDecryptorViewModel).BaseType);
    }

    [Fact]
    public void BrowserAndLibraryExposeSlicesWithoutCopyingOwnerState()
    {
        using var lifetime = new TestDocumentLifetime();
        using var browser = new VideoLibraryBrowserViewModel(new EmptyScanner(), lifetime);
        Assert.Same(browser, browser.Catalog.Owner);
        Assert.Same(browser, browser.Query.Owner);

        var player = Assert.IsType<VideoPlayerControlViewModel>(
            RuntimeHelpers.GetUninitializedObject(typeof(VideoPlayerControlViewModel)));
        using var library = new SecretVideoLibraryViewModel(browser, player, lifetime);

        Assert.Same(library, library.Playback.Owner);
        Assert.Same(library, library.History.Owner);
        Assert.Same(library, library.Layout.Owner);
    }

    [Fact]
    public void SingleVideoCompatibilityAliasesUseChildStateAndClearPassword()
    {
        var player = Assert.IsType<VideoPlayerControlViewModel>(
            RuntimeHelpers.GetUninitializedObject(typeof(VideoPlayerControlViewModel)));
        using var lifetime = new TestDocumentLifetime();
        using var document = new SecretVideoPlayerViewModel(player, lifetime);

        document.Password = "g7.1-sensitive";
        document.FilePath = "missing.secvid";

        Assert.Equal(document.Password, document.Source.Password);
        Assert.Equal(document.FilePath, document.Source.FilePath);

        document.Dispose();
        Assert.Empty(document.Source.Password);
    }

    private sealed class EmptyScanner : IVideoLibraryScanner
    {
        public async IAsyncEnumerable<VideoLibraryScanResult> ScanAsync(
            string folderPath,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
