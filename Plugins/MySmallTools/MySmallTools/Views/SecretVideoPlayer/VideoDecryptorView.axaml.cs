using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Views.SecretVideoPlayer;

public partial class VideoDecryptorView : UserControl
{
    private bool _isFilePickerOpen;
    private bool _isFolderPickerOpen;

    public VideoDecryptorView()
    {
        InitializeComponent();
    }

    private async void OnAddFilesClick(object? sender, RoutedEventArgs e)
    {
        if (_isFilePickerOpen || DataContext is not VideoDecryptorViewModel viewModel || viewModel.IsBusy)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        _isFilePickerOpen = true;
        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要解密的 SECVID03 视频",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("SECVID03 加密视频")
                    {
                        Patterns = ["*.secvid"],
                        MimeTypes = ["application/octet-stream"]
                    }
                ]
            });

            if (files.Count > 0 && ReferenceEquals(DataContext, viewModel))
                await viewModel.AddFilesAsync(files.Select(file => file.Path.LocalPath).ToArray());
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(DataContext, viewModel))
                viewModel.StatusMessage = $"选择视频失败：{ex.Message}";
        }
        finally
        {
            _isFilePickerOpen = false;
        }
    }

    private async void OnChooseOutputDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (_isFolderPickerOpen || DataContext is not VideoDecryptorViewModel viewModel || viewModel.IsBusy)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        _isFolderPickerOpen = true;
        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择解密视频输出目录",
                AllowMultiple = false
            });

            if (folders.Count > 0 && ReferenceEquals(DataContext, viewModel))
                viewModel.SetOutputDirectory(folders[0].Path.LocalPath);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(DataContext, viewModel))
                viewModel.StatusMessage = $"选择输出目录失败：{ex.Message}";
        }
        finally
        {
            _isFolderPickerOpen = false;
        }
    }
}
