namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 管理原生视频表面重建期间是否需要自动续播的纯状态策略。
/// </summary>
/// <remarks>
/// 将该状态从 LibVLC 调用中分离后，可以精确验证“只因表面丢失而暂停才允许自动续播”的规则。
/// 用户主动暂停、停止、切换媒体或关闭文档都会调用 <see cref="Cancel"/>，防止旧请求误触发新媒体播放。
/// </remarks>
internal sealed class VideoSurfaceResumePolicy
{
    public bool HasPendingResume { get; private set; }

    /// <summary>
    /// 记录表面丢失。返回 true 表示调用方需要在销毁 HWND 前同步暂停播放器。
    /// </summary>
    public bool OnSurfaceLost(bool isPlaying)
    {
        if (!isPlaying)
        {
            return false;
        }

        HasPendingResume = true;
        return true;
    }

    /// <summary>
    /// 在表面恢复后消费一次续播请求；没有有效媒体时同时丢弃过期请求。
    /// </summary>
    public bool ConsumeResumeRequest(bool hasMedia)
    {
        if (!hasMedia)
        {
            HasPendingResume = false;
            return false;
        }

        var shouldResume = HasPendingResume;
        HasPendingResume = false;
        return shouldResume;
    }

    /// <summary>
    /// 取消尚未执行的自动续播。
    /// </summary>
    public void Cancel() => HasPendingResume = false;
}
