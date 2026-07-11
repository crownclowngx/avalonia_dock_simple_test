using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BiliDownloader.ViewModels;

namespace BiliDownloader.Views;

public partial class BiliDownloaderView : UserControl
{
    private bool _hasCheckedLogin;
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
            // 首次附加到视觉树时异步触发登录检查
            if (!_hasCheckedLogin)
            {
                _hasCheckedLogin = true;
                _ = vm.EnsureLoggedInAsync();
            }

            // 首次附加时从 SQLite 恢复未完成任务状态
            if (!_hasRecoveredTasks)
            {
                _hasRecoveredTasks = true;
                _ = vm.RecoverTasksFromStoreAsync();
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // 点击视图时检查登录状态
        if (DataContext is BiliDownloaderViewModel { IsLoggedIn: false } vm)
        {
            _ = vm.EnsureLoggedInAsync();
        }
    }
}
