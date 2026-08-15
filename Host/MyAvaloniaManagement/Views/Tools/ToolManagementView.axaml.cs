using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MyAvaloniaManagement.Views.Tools;

internal sealed partial class ToolManagementView : UserControl
{
    public ToolManagementView()
    {
        InitializeComponent();
    }
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
