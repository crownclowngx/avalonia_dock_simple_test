using Avalonia.Controls;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Views.SecretVideoPlayer;

/// <summary>
/// 视频播放器控件
/// 独立的播放器组件，可以在不同的视图中复用
/// </summary>
public partial class VideoPlayerControl : UserControl
{
    /// <summary>
    /// 播放器控件的 ViewModel
    /// </summary>
    public VideoPlayerControlViewModel? ViewModel => DataContext as VideoPlayerControlViewModel;

    public VideoPlayerControl()
    {
        InitializeComponent();
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