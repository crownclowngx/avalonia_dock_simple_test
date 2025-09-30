using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Views.SecretVideoPlayer;

/// <summary>
/// 加密视频播放器视图
/// </summary>
public partial class SecretVideoPlayerView : UserControl
{
    public SecretVideoPlayerView()
    {
        InitializeComponent();
        DataContext = new SecretVideoPlayerViewModel();
    }
    
    /// <summary>
    /// 浏览文件按钮点击事件
    /// </summary>
    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择加密视频文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("视频文件")
                {
                    Patterns = new[] { "*.mp4", "*.avi", "*.mkv", "*.mov", "*.wmv", "*.flv", "*.webm" }
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });
        
        if (files.Count > 0 && DataContext is SecretVideoPlayerViewModel viewModel)
        {
            viewModel.FilePath = files[0].Path.LocalPath;
        }
    }
    
    // 资源清理将在ViewModel的Dispose方法中处理
}