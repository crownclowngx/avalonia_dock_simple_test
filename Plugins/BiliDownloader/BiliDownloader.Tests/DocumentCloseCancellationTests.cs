using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.ViewModels.BiliDownloader;

namespace BiliDownloader.Tests;

public sealed class DocumentCloseCancellationTests
{
    [Fact]
    public async Task DisposingBrowserCancelsInFlightPageLoad()
    {
        var provider = new BlockingContentProvider();
        var registry = new ContentSourceProviderRegistry([provider]);
        var browser = new ContentSourceBrowserViewModel(
            registry,
            new VideoParseResultFactory(new StubMediaProbe(), new StubCredentials(string.Empty)),
            _ => { });
        var descriptor = new ContentSourceDescriptor(
            ContentSourceKind.Uploader,
            "uploader:close-test",
            "close-test",
            null,
            1);

        var opening = browser.OpenAsync(descriptor);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        browser.Dispose();
        await opening.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(provider.CancellationObserved);
        Assert.Empty(browser.Items);
    }

    [Fact]
    public async Task CoordinatorDisposeAllowsActiveLeaseToReturn()
    {
        var coordinator = new ContentQueryCoordinator();
        var lease = await coordinator.EnterAsync(CancellationToken.None);

        coordinator.Dispose();

        var exception = Record.Exception(lease.Dispose);
        Assert.Null(exception);
    }

    private sealed class BlockingContentProvider : IContentSourceProvider
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public ContentSourceKind Kind => ContentSourceKind.Uploader;
        public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsPaging;
        public int CapabilityVersion => 1;

        public ValueTask<ContentSourceDescriptor> NormalizeAsync(
            string input,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ContentSourceDescriptor(Kind, input, input, null, 1));

        public async Task<ContentPage> GetPageAsync(
            ContentSourceDescriptor descriptor,
            ContentPageRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new ContentPage([], null, false);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }

        public Task<BiliVideoCollection> ResolveItemAsync(
            ContentSourceDescriptor descriptor,
            ContentSourceItem item,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
