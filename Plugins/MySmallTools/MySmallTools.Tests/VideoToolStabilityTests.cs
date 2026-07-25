using System.Runtime.CompilerServices;
using MyAvaloniaManagementCommon.DocumentCreation;
using MySmallTools.Business.SecretVideoPlayer.Encryption;
using MySmallTools.Business.SecretVideoPlayer.Library;
using MySmallTools.Business.SecretVideoPlayer.Playback;
using MySmallTools.Constants;
using MySmallTools.InitPlug.SecretVideoPlayer;
using MySmallTools.ViewModels.SecretVideoPlayer;
using Xunit;
using Dock.Model.Mvvm.Controls;

namespace MySmallTools.Tests;

public sealed class VideoToolStabilityTests
{
    [Fact]
    public void VideoDocuments_TogglePasswordVisibility()
    {
        var player = RuntimeHelpers.GetUninitializedObject(typeof(VideoPlayerControlViewModel))
            as VideoPlayerControlViewModel;
        Assert.NotNull(player);

        var singlePlayer = new SecretVideoPlayerViewModel(player);
        Assert.False(singlePlayer.ShowPassword);
        singlePlayer.TogglePasswordVisibilityCommand.Execute(null);
        Assert.True(singlePlayer.ShowPassword);

        using var browser = new VideoLibraryBrowserViewModel(new EmptyScanner());
        using var library = new SecretVideoLibraryViewModel(browser, player);
        Assert.False(library.ShowPassword);
        library.TogglePasswordVisibilityCommand.Execute(null);
        Assert.True(library.ShowPassword);
    }

    [Fact]
    public void LibrarySettingsExpansionPersistsWithoutExposingPasswordOrLosingOtherSettings()
    {
        var player = Assert.IsType<VideoPlayerControlViewModel>(
            RuntimeHelpers.GetUninitializedObject(typeof(VideoPlayerControlViewModel)));
        var initial = VideoLibrarySettings.Default with
        {
            RecentFolder = Path.GetFullPath("remembered-library"),
            IncludeSubdirectories = true,
            SortField = VideoLibrarySortField.ModifiedTime,
            SortDirection = VideoLibrarySortDirection.Descending,
            StatusFilter = VideoLibraryStatusFilter.InProgress
        };
        var settings = new RecordingLibrarySettingsStore(initial);
        using var browser = new VideoLibraryBrowserViewModel(
            new EmptyScanner(),
            settingsStore: settings);
        using var library = new SecretVideoLibraryViewModel(
            browser,
            player,
            settingsStore: settings);

        Assert.False(library.IsLibrarySettingsExpanded);
        Assert.Equal("密码未输入", library.PasswordStateText);

        library.Password = "must-not-appear-in-summary";
        library.IsLibrarySettingsExpanded = true;

        Assert.Equal("密码已输入", library.PasswordStateText);
        Assert.DoesNotContain(
            "must-not-appear",
            library.PasswordStateText,
            StringComparison.Ordinal);
        Assert.True(settings.CurrentSettings.IsLibrarySettingsExpanded);
        Assert.Equal(initial.RecentFolder, settings.CurrentSettings.RecentFolder);
        Assert.True(settings.CurrentSettings.IncludeSubdirectories);
        Assert.Equal(initial.SortField, settings.CurrentSettings.SortField);
        Assert.Equal(initial.SortDirection, settings.CurrentSettings.SortDirection);
        Assert.Equal(initial.StatusFilter, settings.CurrentSettings.StatusFilter);
    }

    [Fact]
    public void VideoEncryptorDocument_DefaultTitleAndVideoTitle_AreIndependent()
    {
        var strategy = CreateVideoEncryptorStrategy();
        var document = strategy.CreateDocument(
            new DocumentCreationParams(DocumentTypeIdConstant.VideoEncryptorDocumentId));
        var viewModel = Assert.IsType<VideoEncryptorViewModel>(document);

        Assert.Equal("视频文件加密器", document.Title);
        Assert.Empty(viewModel.VideoTitle);

        viewModel.VideoTitle = "容器内公开标题";
        Assert.Equal("视频文件加密器", document.Title);

        viewModel.ClearAllCommand.Execute(null);
        Assert.Empty(viewModel.VideoTitle);
        Assert.Equal("视频文件加密器", document.Title);
    }

    [Fact]
    public void VideoEncryptorDocument_PreservesCustomDocumentTitle()
    {
        var strategy = CreateVideoEncryptorStrategy();
        var document = strategy.CreateDocument(new DocumentCreationParams(
            DocumentTypeIdConstant.VideoEncryptorDocumentId)
        {
            Title = "自定义加密任务"
        });

        Assert.Equal("自定义加密任务", document.Title);
        Assert.Empty(Assert.IsType<VideoEncryptorViewModel>(document).VideoTitle);
    }

    [Fact]
    public void SurfaceRecoveryPolicy_RecordsPlayingStateAndConsumesOnlyOnce()
    {
        var policy = new VideoSurfaceRecoveryPolicy();

        var request = policy.OnSurfaceLost(
            mediaGeneration: 3,
            positionMs: 12_345,
            hasMedia: true,
            isPlaying: true,
            isPaused: false);

        Assert.NotNull(request);
        Assert.Equal(3, request.Value.MediaGeneration);
        Assert.Equal(12_345, request.Value.PositionMs);
        Assert.Equal(VideoSurfacePlaybackMode.Playing, request.Value.PlaybackMode);
        Assert.True(policy.HasPendingRecovery);

        Assert.Equal(request, policy.ConsumeRecovery(mediaGeneration: 3));
        Assert.False(policy.HasPendingRecovery);
        Assert.Null(policy.ConsumeRecovery(mediaGeneration: 3));
    }

    [Fact]
    public void SurfaceRecoveryPolicy_RecordsPausedStateForFrameRestoration()
    {
        var policy = new VideoSurfaceRecoveryPolicy();

        var request = policy.OnSurfaceLost(
            mediaGeneration: 8,
            positionMs: 4_200,
            hasMedia: true,
            isPlaying: false,
            isPaused: true);

        Assert.NotNull(request);
        Assert.Equal(VideoSurfacePlaybackMode.Paused, request.Value.PlaybackMode);
        Assert.Equal(4_200, request.Value.PositionMs);
    }

    [Fact]
    public void SurfaceRecoveryPolicy_InternalStopPreservesRequest_ButUserStopCancelsIt()
    {
        var policy = new VideoSurfaceRecoveryPolicy();

        policy.OnSurfaceLost(1, 100, hasMedia: true, isPlaying: true, isPaused: false);
        policy.OnPlaybackStopped(isSurfaceTransition: true);
        Assert.True(policy.HasPendingRecovery);

        policy.OnPlaybackStopped(isSurfaceTransition: false);
        Assert.False(policy.HasPendingRecovery);
    }

    [Fact]
    public void SurfaceRecoveryPolicy_MediaGenerationRejectsStaleRequest()
    {
        var policy = new VideoSurfaceRecoveryPolicy();

        policy.OnSurfaceLost(4, 100, hasMedia: true, isPlaying: true, isPaused: false);

        Assert.Null(policy.ConsumeRecovery(mediaGeneration: 5));
        Assert.False(policy.HasPendingRecovery);
    }

    [Fact]
    public void SurfaceRecoveryPolicy_RapidSurfaceLossKeepsOnlyLatestSnapshot()
    {
        var policy = new VideoSurfaceRecoveryPolicy();

        var first = policy.OnSurfaceLost(7, 100, hasMedia: true, isPlaying: true, isPaused: false);
        var second = policy.OnSurfaceLost(7, 900, hasMedia: true, isPlaying: false, isPaused: true);
        var consumed = policy.ConsumeRecovery(7);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(second.Value.RequestId > first.Value.RequestId);
        Assert.Equal(second, consumed);
        Assert.Null(policy.ConsumeRecovery(7));
    }

    [Fact]
    public void SurfaceRecoveryPolicy_MissingMediaOrStoppedStateDoesNotRecover()
    {
        var policy = new VideoSurfaceRecoveryPolicy();

        Assert.Null(policy.OnSurfaceLost(1, 0, hasMedia: false, isPlaying: true, isPaused: false));
        Assert.Null(policy.OnSurfaceLost(1, 0, hasMedia: true, isPlaying: false, isPaused: false));
        Assert.False(policy.HasPendingRecovery);

        policy.OnSurfaceLost(1, 0, hasMedia: true, isPlaying: true, isPaused: false);
        policy.Cancel();
        Assert.Null(policy.ConsumeRecovery(1));
    }

    [Fact]
    public async Task SurfaceRestoreSequence_PausedModeWaitsForFrameBeforePausing()
    {
        var operations = new RecordingSurfaceRestoreOperations(length: 10_000);

        var restored = await VideoSurfaceRestoreSequence.ExecuteAsync(
            operations,
            positionMs: 4_200,
            restorePaused: true,
            CancellationToken.None);

        Assert.True(restored);
        Assert.Equal(
            ["Play", "WaitForVideoOutput", "Seek:4200", "WaitForFrame", "Pause"],
            operations.Calls);
    }

    [Fact]
    public async Task SurfaceRestoreSequence_PlayingModeKeepsPlayingAfterSeek()
    {
        var operations = new RecordingSurfaceRestoreOperations(length: 10_000);

        var restored = await VideoSurfaceRestoreSequence.ExecuteAsync(
            operations,
            positionMs: 9_999,
            restorePaused: false,
            CancellationToken.None);

        Assert.True(restored);
        // 位置会避开媒体末尾，防止刚恢复就触发 EndReached。
        Assert.Equal(
            ["Play", "WaitForVideoOutput", "Seek:9750", "WaitForFrame"],
            operations.Calls);
    }

    [Fact]
    public async Task VideoEncryptorDocument_DisposeCancelsEncryptionAndRemovesPartialFile()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "MySmallTools-DI-Cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var inputPath = Path.Combine(temporaryDirectory, "source.mp4");
        var outputPath = Path.Combine(temporaryDirectory, "output.secvid");

        try
        {
            // 使用稀疏文件保证加密操作不会在 Dispose 发出取消前完成，同时避免测试占用大量磁盘空间。
            using (var input = new FileStream(inputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.SetLength(64L * 1024 * 1024);
            }

            var service = new VideoEncryptorService(new Secvid03Encryptor());
            var viewModel = new VideoEncryptorViewModel(service)
            {
                SelectedFilePath = inputPath,
                OutputFilePath = outputPath,
                Password = "123456",
                ConfirmPassword = "123456",
            };

            var encryption = viewModel.StartEncryptionCommand.ExecuteAsync(null);
            viewModel.Dispose();
            await encryption;

            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.GetFiles(temporaryDirectory, "*.partial-*"));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task VideoEncryptorDocument_ExplicitCancelIsOnlyAvailableWhileRunning()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "MySmallTools-Explicit-Cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var inputPath = Path.Combine(temporaryDirectory, "source.mp4");
        var outputPath = Path.Combine(temporaryDirectory, "output.secvid");

        try
        {
            using (var input = new FileStream(inputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                input.SetLength(64L * 1024 * 1024);

            using var viewModel = new VideoEncryptorViewModel(
                new VideoEncryptorService(new Secvid03Encryptor()))
            {
                SelectedFilePath = inputPath,
                OutputFilePath = outputPath,
                Password = "123456",
                ConfirmPassword = "123456"
            };

            Assert.False(viewModel.CancelEncryptionCommand.CanExecute(null));
            var encryption = viewModel.StartEncryptionCommand.ExecuteAsync(null);
            Assert.True(viewModel.CancelEncryptionCommand.CanExecute(null));

            viewModel.CancelEncryptionCommand.Execute(null);
            await encryption;

            Assert.False(viewModel.IsEncrypting);
            Assert.False(viewModel.CancelEncryptionCommand.CanExecute(null));
            Assert.Contains("加密已取消", viewModel.StatusMessage);
            Assert.False(File.Exists(outputPath));
            Assert.Empty(Directory.GetFiles(temporaryDirectory, "*.partial-*"));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class RecordingSurfaceRestoreOperations(long length) : IVideoSurfaceRestoreOperations
    {
        public List<string> Calls { get; } = [];
        public long Length { get; } = length;

        public bool Play()
        {
            Calls.Add("Play");
            return true;
        }

        public Task WaitForVideoOutputAsync(CancellationToken cancellationToken)
        {
            Calls.Add("WaitForVideoOutput");
            return Task.CompletedTask;
        }

        public Task SeekAsync(long positionMs, bool waitForFrame, CancellationToken cancellationToken)
        {
            Calls.Add($"Seek:{positionMs}");
            if (waitForFrame)
            {
                Calls.Add("WaitForFrame");
            }
            return Task.CompletedTask;
        }

        public Task PauseAtAsync(long positionMs, CancellationToken cancellationToken)
        {
            Calls.Add("Pause");
            return Task.CompletedTask;
        }
    }

    private static VideoEncryptorDocumentStrategy CreateVideoEncryptorStrategy() =>
        new(new VideoToolTestDocumentScopeFactory());

    private sealed class EmptyScanner : IVideoLibraryScanner
    {
        public async IAsyncEnumerable<VideoLibraryScanResult> ScanAsync(
            string folderPath,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class RecordingLibrarySettingsStore(VideoLibrarySettings initial)
        : IVideoLibrarySettingsStore
    {
        public VideoLibrarySettings CurrentSettings { get; private set; } = initial;

        public void UpdateSettings(VideoLibrarySettings settings) =>
            CurrentSettings = settings;
    }

    /// <summary>
    /// 这些测试只验证策略对 Dock Title 与视频公开标题的映射，不验证宿主 Scope 实现。
    /// Scope 的创建、独立性和释放由 PluginTests 中的 DocumentScopeManager 测试覆盖。
    /// </summary>
    private sealed class VideoToolTestDocumentScopeFactory : IDocumentScopeFactory
    {
        public TDocument CreateDocument<TDocument>() where TDocument : Document
        {
            if (typeof(TDocument) == typeof(VideoEncryptorViewModel))
            {
                var service = new VideoEncryptorService(new Secvid03Encryptor());
                return (TDocument)(Document)new VideoEncryptorViewModel(service);
            }

            throw new NotSupportedException($"测试未注册 Document: {typeof(TDocument).FullName}");
        }
    }
}
