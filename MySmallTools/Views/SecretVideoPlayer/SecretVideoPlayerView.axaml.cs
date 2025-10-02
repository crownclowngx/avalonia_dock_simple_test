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
    
    public SecretVideoPlayerView()
    {
        InitializeComponent();
        DataContext = new SecretVideoPlayerViewModel();
        // 注册Loaded事件
        this.Loaded += OnViewLoaded;
    }
    
    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        ProgressSlider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, handledEventsToo: true);
        ProgressSlider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, handledEventsToo: true);
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
    
    /// <summary>
    /// 进度条按下事件 - 开始拖拽
    /// </summary>
    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isDragging = true;
        if (DataContext is SecretVideoPlayerViewModel viewModel)
        {
            // 暂停位置更新定时器，避免拖拽时位置被覆盖
            viewModel.PausePositionUpdates();
        }
    }
    
    /// <summary>
    /// 进度条释放事件 - 结束拖拽，执行跳转
    /// </summary>
    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging && DataContext is SecretVideoPlayerViewModel viewModel)
        {
            _isDragging = false;
            
            // 获取当前滑块的值并执行跳转
            if (sender is Slider slider)
            {
                viewModel.SeekToPosition(slider.Value);
            }
            
            // 恢复位置更新定时器
            viewModel.ResumePositionUpdates();
        }
    }
}