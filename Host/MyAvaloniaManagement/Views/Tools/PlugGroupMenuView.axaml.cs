using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MyAvaloniaManagement.Views.Tools;

internal sealed partial class PlugGroupMenuView : UserControl
{
    public PlugGroupMenuView()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
