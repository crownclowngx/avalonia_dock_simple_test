using MySmallTools.Business.SecretVideoPlayer;
using MySmallTools.Constants;
using MySmallTools.InitPlug.SecretVideoPlayer;
using MySmallTools.ViewModels.SecretVideoPlayer;
using Xunit;

namespace MySmallTools.Tests;

[Collection(Secvid03Collection.Name)]
public sealed class VideoLibraryTests(Secvid03Fixture fixture)
{
    [Fact]
    public async Task Scanner_ReadsOnlyTopLevelSecvidFiles_AndKeepsPerFileErrors()
    {
        var directory = CreateTestDirectory();
        try
        {
            var unicodePath = Path.Combine(directory, "乙视频.secvid");
            File.Copy(fixture.EncryptedPath, unicodePath);
            EncryptedVideoContainer.UpdatePublicInfo(unicodePath, "公开标题😀", "隐藏描述关键字");

            var invalidPath = Path.Combine(directory, "甲损坏.SECVID");
            await File.WriteAllTextAsync(invalidPath, "not a SECVID03 container");
            await File.WriteAllTextAsync(Path.Combine(directory, "忽略.txt"), "ignored");

            var child = Directory.CreateDirectory(Path.Combine(directory, "child")).FullName;
            File.Copy(fixture.EncryptedPath, Path.Combine(child, "子目录.secvid"));

            var results = await ReadAllAsync(new VideoLibraryScanner().ScanAsync(directory, CancellationToken.None));

            Assert.Equal(2, results.Count);
            var invalid = Assert.Single(results, item => item.FilePath == invalidPath);
            Assert.Equal(VideoLibraryMetadataState.Failed, invalid.State);
            Assert.NotEmpty(invalid.ErrorMessage);

            var valid = Assert.Single(results, item => item.FilePath == unicodePath);
            Assert.Equal(VideoLibraryMetadataState.Ready, valid.State);
            Assert.Equal("公开标题😀", valid.PublicTitle);
            Assert.Equal("隐藏描述关键字", valid.PublicDescription);

            File.Delete(unicodePath);
            File.Delete(invalidPath);
            Assert.False(File.Exists(unicodePath));
            Assert.False(File.Exists(invalidPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Browser_SortsAndSearchesFileNameTitleAndHiddenDescription()
    {
        using var browser = new VideoLibraryBrowserViewModel(new FixedScanner(
        [
            Ready("unique-file-77.secvid", "unique-file-77", "Third", "ordinary"),
            Ready("a.secvid", "a", "First Alpha", "ordinary"),
            Ready("b.secvid", "b", "Second", "secret Needle text")
        ]));

        Assert.False(browser.HasFolder);
        Assert.False(browser.HasVisibleItems);
        await browser.LoadFolderAsync("virtual-library");
        Assert.True(browser.HasFolder);
        Assert.True(browser.HasVisibleItems);
        Assert.Equal(["a", "b", "unique-file-77"], browser.VisibleItems.Select(item => item.FileNameWithoutExtension));
        Assert.Equal("a（First Alpha）", browser.VisibleItems[0].DisplayName);

        browser.SearchText = "ALPHA";
        await WaitForFilterAsync();
        Assert.Equal("a", Assert.Single(browser.VisibleItems).FileNameWithoutExtension);

        browser.SearchText = "needle";
        await WaitForFilterAsync();
        Assert.Equal("b", Assert.Single(browser.VisibleItems).FileNameWithoutExtension);

        browser.SearchText = "no-match";
        await WaitForFilterAsync();
        Assert.False(browser.HasVisibleItems);

        browser.SearchText = "77";
        await WaitForFilterAsync();
        Assert.Equal("unique-file-77", Assert.Single(browser.VisibleItems).FileNameWithoutExtension);

        browser.SearchText = string.Empty;
        await WaitForFilterAsync();
        Assert.Equal(3, browser.VisibleItems.Count);
    }

    [Fact]
    public async Task Browser_RejectsLateResultsFromPreviousFolderAndAfterDispose()
    {
        var scanner = new SwitchingScanner();
        var browser = new VideoLibraryBrowserViewModel(scanner);

        var oldScan = browser.LoadFolderAsync("old-library");
        await Task.Delay(20);
        await browser.LoadFolderAsync("new-library");
        await oldScan;

        Assert.Equal("new", Assert.Single(browser.VisibleItems).FileNameWithoutExtension);

        var disposedBrowser = new VideoLibraryBrowserViewModel(scanner);
        var pending = disposedBrowser.LoadFolderAsync("old-library");
        await Task.Delay(20);
        disposedBrowser.Dispose();
        await pending;
        Assert.Empty(disposedBrowser.VisibleItems);
    }

    [Fact]
    public void LibraryStrategy_ExposesStableMenuMetadata()
    {
        var strategy = new SecretVideoLibraryDocumentStrategy(new ThrowingDocumentScopeFactory());
        var metadata = strategy.GetMetadata();

        Assert.Equal(DocumentTypeIdConstant.SecretVideoLibraryDocumentId, metadata.DocumentTypeId);
        Assert.Equal("加密视频库播放器", metadata.DisplayName);
        Assert.Equal("视频工具", metadata.MenuCategory);
    }

    private string CreateTestDirectory()
    {
        var path = Path.Combine(fixture.DirectoryPath, "library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static VideoLibraryScanResult Ready(
        string path,
        string fileName,
        string title,
        string description) =>
        new(path, fileName, title, description, VideoLibraryMetadataState.Ready, string.Empty);

    private static async Task<List<VideoLibraryScanResult>> ReadAllAsync(
        IAsyncEnumerable<VideoLibraryScanResult> results)
    {
        var items = new List<VideoLibraryScanResult>();
        await foreach (var result in results)
            items.Add(result);
        return items;
    }

    private static Task WaitForFilterAsync() => Task.Delay(250);

    private sealed class FixedScanner(IReadOnlyList<VideoLibraryScanResult> results) : IVideoLibraryScanner
    {
        public async IAsyncEnumerable<VideoLibraryScanResult> ScanAsync(
            string folderPath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return result;
                await Task.Yield();
            }
        }
    }

    private sealed class SwitchingScanner : IVideoLibraryScanner
    {
        public async IAsyncEnumerable<VideoLibraryScanResult> ScanAsync(
            string folderPath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (folderPath.EndsWith("old-library", StringComparison.OrdinalIgnoreCase))
            {
                // Deliberately ignore cancellation to prove that the browser's generation guard rejects stale work.
                await Task.Delay(100);
                yield return Ready("old.secvid", "old", "Old", string.Empty);
                yield break;
            }

            yield return Ready("new.secvid", "new", "New", string.Empty);
        }
    }

    private sealed class ThrowingDocumentScopeFactory : MyAvaloniaManagementCommon.DocumentCreation.IDocumentScopeFactory
    {
        public TDocument CreateDocument<TDocument>() where TDocument : Dock.Model.Mvvm.Controls.Document =>
            throw new NotSupportedException();
    }
}
