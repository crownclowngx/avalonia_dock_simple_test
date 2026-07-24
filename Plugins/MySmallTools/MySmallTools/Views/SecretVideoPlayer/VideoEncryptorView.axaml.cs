using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Views.SecretVideoPlayer;

/// <summary>
/// 批量视频加密页面。
/// </summary>
/// <remarks>
/// 文件选择属于窗口级 UI 能力，因此直接由视图调用 StorageProvider。
/// 不再通过 ViewModel 事件转发，避免 Dock 回收视图或重复设置 DataContext 时累计事件订阅。
/// </remarks>
public partial class VideoEncryptorView : UserControl
{
    private bool _isFilePickerOpen;

    public VideoEncryptorView()
    {
        InitializeComponent();
    }

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        // async void 点击处理器在等待系统窗口期间仍可再次收到点击事件，
        // 因此必须显式增加重入保护，保证一个视图实例最多存在一个文件选择窗口。
        if (_isFilePickerOpen || DataContext is not VideoEncryptorViewModel initiatingViewModel ||
            initiatingViewModel.IsBusy)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        _isFilePickerOpen = true;
        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要加入加密队列的视频文件",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("视频文件")
                    {
                        Patterns = ["*.mp4", "*.avi", "*.mkv", "*.mov", "*.wmv", "*.flv", "*.webm", "*.m4v"]
                    },
                    new FilePickerFileType("所有文件")
                    {
                        Patterns = ["*.*"]
                    }
                ]
            });

            // 系统对话框打开期间 Dock 可能切换或回收当前视图。
            // 只有 DataContext 仍然是发起请求的文档时才回写路径，避免污染另一个文档实例。
            if (ReferenceEquals(DataContext, initiatingViewModel) && files.Count > 0)
            {
                var paths = files
                    .Where(file => file.Path.IsFile)
                    .Select(file => file.Path.LocalPath)
                    .ToArray();
                await initiatingViewModel.AddFilesAsync(paths);
            }
        }
        catch
        {
            if (ReferenceEquals(DataContext, initiatingViewModel))
            {
                // 系统文件选择器的原始异常可能包含实现路径。队列 UI 只显示稳定、安全提示，
                // 详细异常保留给调试器，而不进入用户可复制的任务消息。
                initiatingViewModel.StatusMessage = "选择文件失败，请重新打开文件选择器后重试。";
            }
        }
        finally
        {
            _isFilePickerOpen = false;
        }
    }
}
