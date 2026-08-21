using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BiliDownloader.Views;

public partial class BiliDownloaderView : UserControl
{
    public BiliDownloaderView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

}
