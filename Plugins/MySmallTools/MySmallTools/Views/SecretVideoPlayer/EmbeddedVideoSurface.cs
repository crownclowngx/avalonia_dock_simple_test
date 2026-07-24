using Avalonia.Platform;
using LibVLCSharp.Avalonia;
using MySmallTools.Business.SecretVideoPlayer.Playback;

namespace MySmallTools.Views.SecretVideoPlayer;

/// <summary>
/// 保证 LibVLC 始终绑定到 Avalonia 原生子窗口的内嵌视频表面。
/// </summary>
/// <remarks>
/// LibVLCSharp.Avalonia 3.9.4 只会在 MediaPlayer 属性变化或控件 Initialized 时尝试绑定 HWND。
/// Dock 的视图回收可能让这两个时机都早于 NativeControlHost 创建原生句柄，最终使 Hwnd 保持为零，
/// LibVLC 随后会回退创建独立的 Direct3D11 输出窗口。本控件在句柄真正创建后再次显式绑定，
/// 并在句柄销毁前同步通知播放器暂停，从根源上消除该时序竞争。
/// </remarks>
public sealed class EmbeddedVideoSurface : VideoView
{
    private long _surfaceGeneration;

    /// <summary>
    /// 原生视频表面的可用状态发生变化时触发。
    /// </summary>
    public event EventHandler<VideoSurfaceReadyChangedEventArgs>? SurfaceReadyChanged;

    /// <summary>
    /// 当前是否已经创建非零 HWND，并可安全启动视频输出。
    /// </summary>
    public bool IsSurfaceReady { get; private set; }

    /// <summary>当前真实 HWND 及其单调递增代次。</summary>
    public VideoSurfaceToken? CurrentSurfaceToken { get; private set; }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var control = base.CreateNativeControlCore(parent);

        // 首期明确只支持 Windows x64。必须在基类完成内部 _platformHandle 赋值后再设置 Hwnd，
        // 这样后续 MediaPlayer 属性变化时，基类原有 Attach 逻辑也仍然能够正常工作。
        if (OperatingSystem.IsWindows() && control.Handle != IntPtr.Zero)
        {
            if (MediaPlayer is not null)
            {
                MediaPlayer.Hwnd = control.Handle;
            }

            CurrentSurfaceToken = new VideoSurfaceToken(
                Interlocked.Increment(ref _surfaceGeneration),
                control.Handle);
            SetSurfaceReady(true, CurrentSurfaceToken);
        }

        return control;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // 状态事件必须在基类把 MediaPlayer.Hwnd 清零之前同步发出。
        // ViewModel 会在该回调中暂停正在播放的媒体，避免活动中的 vout 在 HWND 消失后创建独立窗口。
        var destroyedSurface = CurrentSurfaceToken;
        SetSurfaceReady(false, destroyedSurface);
        base.DestroyNativeControlCore(control);
        CurrentSurfaceToken = null;
    }

    private void SetSurfaceReady(bool value, VideoSurfaceToken? surface)
    {
        if (IsSurfaceReady == value)
        {
            return;
        }

        IsSurfaceReady = value;
        SurfaceReadyChanged?.Invoke(
            this,
            new VideoSurfaceReadyChangedEventArgs(value, surface));
    }
}

/// <summary>
/// 视频表面可用状态事件参数。
/// </summary>
public sealed class VideoSurfaceReadyChangedEventArgs(
    bool isReady,
    VideoSurfaceToken? surface) : EventArgs
{
    public bool IsReady { get; } = isReady;
    public VideoSurfaceToken? Surface { get; } = surface;
}
