using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BiliDownloader.ViewModels;

namespace BiliDownloader.Views;

public partial class BiliDownloaderView : UserControl
{
    private bool _hasCheckedLogin;

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

        // 首次附加到视觉树时异步触发登录检查
        if (!_hasCheckedLogin && DataContext is BiliDownloaderViewModel vm)
        {
            _hasCheckedLogin = true;
            _ = vm.EnsureLoggedInAsync();
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
