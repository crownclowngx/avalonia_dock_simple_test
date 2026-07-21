using MyAvaloniaManagementCommon.DocumentCreation;
using MySmallTools.Business.SecretVideoPlayer;
using MySmallTools.Constants;
using MySmallTools.InitPlug.SecretVideoPlayer;
using MySmallTools.ViewModels.SecretVideoPlayer;
using Xunit;

namespace MySmallTools.Tests;

public sealed class VideoToolStabilityTests
{
    [Fact]
    public void VideoEncryptorDocument_DefaultTitleAndVideoTitle_AreIndependent()
    {
        var strategy = new VideoEncryptorDocumentStrategy();
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
        var strategy = new VideoEncryptorDocumentStrategy();
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
        Assert.Equal(["Play", "WaitForVideoOutput", "Seek:9750"], operations.Calls);
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

        public void Pause() => Calls.Add("Pause");
    }
}
