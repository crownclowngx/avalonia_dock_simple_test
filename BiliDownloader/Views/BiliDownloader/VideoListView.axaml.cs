using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BiliDownloader.Views.BiliDownloader;

public partial class VideoListView : UserControl
{
    public VideoListView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
