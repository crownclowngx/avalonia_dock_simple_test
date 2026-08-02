using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BiliDownloader.ViewModels;

namespace BiliDownloader.Views;

public partial class BiliDownloaderView : UserControl
{
    private bool _hasRecoveredTasks;

    public BiliDownloaderView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is BiliDownloaderViewModel vm)
        {
            // 视觉树只恢复当前 Document 的状态投影，不初始化插件服务，也不自动触发远端登录校验。
            if (!_hasRecoveredTasks)
            {
                _hasRecoveredTasks = true;
                _ = vm.InitializeAsync();
            }
        }
    }

}
