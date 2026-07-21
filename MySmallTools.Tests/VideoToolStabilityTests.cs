using MyAvaloniaManagementCommon.DocumentCreation;
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
    public void SurfaceResumePolicy_OnlyRecordsLossWhilePlaying()
    {
        var policy = new VideoSurfaceResumePolicy();

        Assert.False(policy.OnSurfaceLost(isPlaying: false));
        Assert.False(policy.HasPendingResume);
        Assert.False(policy.ConsumeResumeRequest(hasMedia: true));

        Assert.True(policy.OnSurfaceLost(isPlaying: true));
        Assert.True(policy.HasPendingResume);
        Assert.True(policy.ConsumeResumeRequest(hasMedia: true));
        Assert.False(policy.HasPendingResume);

        // 请求采用消费语义，同一次 HWND 恢复只能触发一次续播。
        Assert.False(policy.ConsumeResumeRequest(hasMedia: true));
    }

    [Fact]
    public void SurfaceResumePolicy_UserActionOrMissingMedia_CancelsResume()
    {
        var policy = new VideoSurfaceResumePolicy();

        policy.OnSurfaceLost(isPlaying: true);
        policy.Cancel();
        Assert.False(policy.ConsumeResumeRequest(hasMedia: true));

        policy.OnSurfaceLost(isPlaying: true);
        Assert.False(policy.ConsumeResumeRequest(hasMedia: false));
        Assert.False(policy.HasPendingResume);
    }
}
