using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BiliDownloader.Views.BiliScheduler;

public partial class SchedulerTaskListView : UserControl
{
    private const double CompactWidth = 480;

    public SchedulerTaskListView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => Classes.Set("compact", args.NewSize.Width <= 0 || args.NewSize.Width < CompactWidth);
        Classes.Set("compact", true);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
