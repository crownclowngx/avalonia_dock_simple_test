using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Views.SecretVideoPlayer;

public partial class SecretVideoPlayerView : UserControl
{
    public SecretVideoPlayerView()
    {
        InitializeComponent();
    }

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SecretVideoPlayerViewModel viewModel)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择加密视频文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("加密视频文件")
                {
                    Patterns = new[] { "*.secvid" }
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count > 0)
        {
            await viewModel.SelectFileAsync(files[0].Path.LocalPath);
        }
    }


}
