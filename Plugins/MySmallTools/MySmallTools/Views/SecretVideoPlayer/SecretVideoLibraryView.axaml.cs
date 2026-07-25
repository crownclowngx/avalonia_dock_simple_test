using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Views.SecretVideoPlayer;

public partial class SecretVideoLibraryView : UserControl
{
    private const double InlinePaneMinimumWidth = 960;
    private bool _isFolderPickerOpen;

    public SecretVideoLibraryView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        SizeChanged += OnLibraryViewSizeChanged;
    }

    private void OnLibraryViewSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // 420px 的舒适侧栏在窄 Document 中若继续 Inline，会把原生视频表面压缩到几乎不可用。
        // 低于阈值时只改变 SplitView 的呈现方式，不修改持久化的开关状态；窗口再次变宽后
        // 自动回到并排布局，业务 ViewModel 因而无需感知像素尺寸或宿主窗口结构。
        LibrarySplitView.DisplayMode = e.NewSize.Width >= InlinePaneMinimumWidth
            ? SplitViewDisplayMode.CompactInline
            : SplitViewDisplayMode.CompactOverlay;
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is not SecretVideoLibraryViewModel viewModel)
            return;
        try
        {
            await viewModel.InitializeAsync();
        }
        catch
        {
            if (ReferenceEquals(DataContext, viewModel))
                viewModel.StatusMessage = "恢复最近媒体目录失败，请重新选择文件夹";
        }
    }

    private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_isFolderPickerOpen ||
            DataContext is not SecretVideoLibraryViewModel viewModel ||
            viewModel.IsOpening)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        _isFolderPickerOpen = true;
        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择加密视频文件夹",
                AllowMultiple = false
            });

            // Dock 等待系统对话框时可能替换 DataContext，结果只能写回发起请求的 Document。
            if (folders.Count > 0 && ReferenceEquals(DataContext, viewModel))
                await viewModel.OpenFolderAsync(folders[0].Path.LocalPath);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(DataContext, viewModel))
                viewModel.StatusMessage = $"选择文件夹失败: {ex.Message}";
        }
        finally
        {
            _isFolderPickerOpen = false;
        }
    }

    private async void OnVideoListDoubleTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        await ExecuteActivateSelectedAsync();
    }

    private async void OnVideoListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await ExecuteActivateSelectedAsync();
    }

    private async Task ExecuteActivateSelectedAsync()
    {
        if (DataContext is not SecretVideoLibraryViewModel viewModel)
            return;

        var command = viewModel.ActivateSelectedCommand;
        if (command.CanExecute(null))
            await command.ExecuteAsync(null);
    }
}
