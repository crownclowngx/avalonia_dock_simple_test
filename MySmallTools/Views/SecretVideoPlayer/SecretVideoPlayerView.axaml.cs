using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using MySmallTools.ViewModels.SecretVideoPlayer;
using System;
using Avalonia;

namespace MySmallTools.Views.SecretVideoPlayer;

/// <summary>
/// 加密视频播放器视图
/// </summary>
public partial class SecretVideoPlayerView : UserControl
{
    private Slider _sliderControl;
    private bool _isDragging = false;
    private SecretVideoPlayerViewModel _viewModel;

    public SecretVideoPlayerView()
    {
        InitializeComponent();
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
}