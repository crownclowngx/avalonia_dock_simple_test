using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BiliDownloader.Views.BiliScheduler;

public partial class SchedulerTaskListView : UserControl
{
    public SchedulerTaskListView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
