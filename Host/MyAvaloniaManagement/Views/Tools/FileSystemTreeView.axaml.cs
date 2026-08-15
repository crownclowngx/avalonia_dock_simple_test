using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MyAvaloniaManagement.Views.Tools;

internal sealed partial class FileSystemTreeView : UserControl
{
    public FileSystemTreeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
