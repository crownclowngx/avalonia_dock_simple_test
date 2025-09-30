using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Views.SecretVideoPlayer;

public partial class VideoEncryptorView : UserControl
{
    public VideoEncryptorView()
    {
        InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is VideoEncryptorViewModel viewModel)
        {
            // 订阅文件选择请求事件
            viewModel.FileSelectionRequested += OnFileSelectionRequested;
        }
    }

    private async void OnFileSelectionRequested(object? sender, EventArgs e)
    {
        if (DataContext is VideoEncryptorViewModel viewModel)
        {
            await SelectFileAsync(viewModel);
        }
    }

    private async Task SelectFileAsync(VideoEncryptorViewModel viewModel)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要加密的视频文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("视频文件")
                    {
                        Patterns = new[] { "*.mp4", "*.avi", "*.mkv", "*.mov", "*.wmv", "*.flv", "*.webm", "*.m4v" }
                    },
                    new FilePickerFileType("所有文件")
                    {
                        Patterns = new[] { "*.*" }
                    }
                }
            });

            if (files.Count > 0)
            {
                var selectedFile = files[0];
                if (selectedFile.Path.IsFile)
                {
                    viewModel.SelectedFilePath = selectedFile.Path.LocalPath;
                }
            }
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"选择文件时出错: {ex.Message}";
        }
    }
}