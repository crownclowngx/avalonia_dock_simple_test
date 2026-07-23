using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BiliDownloader.Views.BiliScheduler;

public partial class SchedulerSettingsView : UserControl
{
    public SchedulerSettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
