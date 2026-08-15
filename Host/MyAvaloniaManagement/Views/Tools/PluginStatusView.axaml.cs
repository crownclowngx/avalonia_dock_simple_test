using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MyAvaloniaManagement.Views.Tools;

internal sealed partial class PluginStatusView : UserControl
{
    public PluginStatusView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
