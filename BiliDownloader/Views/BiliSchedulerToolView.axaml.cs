using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BiliDownloader.ViewModels;

namespace BiliDownloader.Views;

public partial class BiliSchedulerToolView : UserControl
{
    public BiliSchedulerToolView()
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

        // 视觉树只负责激活界面投影；Coordinator 的初始化和关闭由宿主插件生命周期统一管理。
        if (DataContext is BiliSchedulerToolViewModel vm)
        {
            _ = vm.ActivateAsync();
        }
    }
}
