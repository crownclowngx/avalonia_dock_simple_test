using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BiliDownloader.Views.BiliDownloader;

public partial class DownloadConfigView : UserControl
{
    public DownloadConfigView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
