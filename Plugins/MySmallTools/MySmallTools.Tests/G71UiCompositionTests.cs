using System.Runtime.CompilerServices;
using MySmallTools.Business.SecretVideoPlayer.Encryption;
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

    [Fact]
    public void CapturedUiSchedulerPostsCrossContextWorkExactlyOnce()
    {
        // 调度器的跨上下文分支不能依赖 xUnit 是否为当前用例安装同步上下文，也不能依赖
        // 周期 Timer 恰好在覆盖率采集结束前回调。测试显式安装一个只记录 Post 的上下文，
        // 在另一个上下文调用调度器，再由测试主动排空队列，从而固定投递与执行两个阶段。
        var previous = SynchronizationContext.Current;
        var context = new RecordingSynchronizationContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            var scheduler = new CapturedUiScheduler();
            SynchronizationContext.SetSynchronizationContext(null);
            var invocationCount = 0;

            scheduler.Post(() => invocationCount++);

            Assert.Equal(0, invocationCount);
            Assert.Equal(1, context.PendingCount);
            context.RunNext();
            Assert.Equal(1, invocationCount);
            Assert.Equal(0, context.PendingCount);
        }
        finally
        {
            // 同步上下文是进程级线程状态，必须恢复 xUnit 原值，避免本用例污染后续测试。
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void EncryptionProgressAlwaysUpdatesTextAndCompatibilityProjection()
    {
        // 直接设置公开总体进度，验证 ObservableProperty 回调本身；不启动真实加密任务，
        // 因而该分支不再依赖后台 IProgress 回调的调度时机。
        using var lifetime = new TestDocumentLifetime();
        using var document = TestViewModelFactory.CreateEncryptor(
            new VideoEncryptorService(new Secvid03Encryptor()),
            lifetime);
        var changedProperties = new List<string?>();
        document.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        document.OverallProgress = 42.25;

        Assert.Equal(42.25, document.Progress);
        Assert.Equal("42.2%", document.ProgressText);
        Assert.Contains(nameof(document.Progress), changedProperties);
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

    /// <summary>
    /// 只保存异步投递，不创建线程也不自动执行。它让测试拥有回调何时运行的决定权，
    /// 同时证明生产调度器只依赖 BCL <see cref="SynchronizationContext"/> 抽象。
    /// </summary>
    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _pending = new();

        public int PendingCount => _pending.Count;

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            _pending.Enqueue((d, state));
        }

        public void RunNext()
        {
            var work = _pending.Dequeue();
            work.Callback(work.State);
        }
    }
}
