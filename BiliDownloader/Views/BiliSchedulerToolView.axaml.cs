using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BiliDownloader.ViewModels;

namespace BiliDownloader.Views;

public partial class BiliSchedulerToolView : UserControl
{
    private bool _hasInitialized;

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

        // 首次附加时初始化调度器（加载未完成任务、自动恢复）
        if (!_hasInitialized && DataContext is BiliSchedulerToolViewModel vm)
        {
            _hasInitialized = true;
            _ = vm.InitializeAsync();
        }
    }
}
