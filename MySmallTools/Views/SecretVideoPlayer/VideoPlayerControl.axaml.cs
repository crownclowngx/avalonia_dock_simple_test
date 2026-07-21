using Avalonia.Controls;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Views.SecretVideoPlayer;

/// <summary>
/// 视频播放器控件
/// 独立的播放器组件，可以在不同的视图中复用
/// </summary>
public partial class VideoPlayerControl : UserControl
{
    private VideoPlayerControlViewModel? _boundViewModel;

    /// <summary>
    /// 播放器控件的 ViewModel
    /// </summary>
    public VideoPlayerControlViewModel? ViewModel => DataContext as VideoPlayerControlViewModel;

    public VideoPlayerControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        VideoSurface.SurfaceReadyChanged += OnSurfaceReadyChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_boundViewModel, DataContext))
        {
            return;
        }

        // 控件回收给另一个文档时，先通知旧 ViewModel 表面已经不可用，
        // 再把当前 HWND 状态交给新的 ViewModel，避免两个播放器同时认为自己拥有同一个输出窗口。
        _boundViewModel?.SetVideoSurfaceReady(false);

        // MediaPlayer 不使用 XAML 继承 DataContext 绑定，而是在这里按严格顺序切换：
        // 先暂停旧播放器，再让 VideoView 清除旧 Hwnd，最后绑定新播放器。
        // 这可避免 DataContext 向子控件传播得更早时，旧播放器在仍播放的状态下突然失去输出句柄。
        _boundViewModel = DataContext as VideoPlayerControlViewModel;
        VideoSurface.MediaPlayer = _boundViewModel?.MediaPlayer;
        _boundViewModel?.SetVideoSurfaceReady(VideoSurface.IsSurfaceReady);
    }

    private void OnSurfaceReadyChanged(object? sender, VideoSurfaceReadyChangedEventArgs e)
    {
        // 事件由 DestroyNativeControlCore 在清除 HWND 前同步触发，
        // 所以此处不可异步排队，必须立即让 ViewModel 暂停正在播放的媒体。
        _boundViewModel?.SetVideoSurfaceReady(e.IsReady);
    }

    /// <summary>
    /// 加载媒体文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="password">解密密码</param>
    /// <returns>是否加载成功</returns>
    public async Task<bool> LoadMediaAsync(string filePath, string password)
    {
        if (ViewModel != null)
        {
            return await ViewModel.LoadMediaAsync(filePath, password);
        }
        return false;
    }

    /// <summary>
    /// 清理当前媒体
    /// </summary>
    public void CleanupMedia()
    {
        ViewModel?.CleanupMedia();
    }

    /// <summary>
    /// 获取当前播放状态
    /// </summary>
    public bool IsPlaying => ViewModel?.IsPlaying ?? false;

    /// <summary>
    /// 获取当前暂停状态
    /// </summary>
    public bool IsPaused => ViewModel?.IsPaused ?? false;
}
