using Avalonia.Controls;
using DemoPlugin.Features.Main;
using MyAvaloniaManagement.PluginSdk;

namespace DemoPlugin.Standalone;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var document = new MainDocument();
        document.InitializeAsync(
            new NewDocumentActivation("DemoPlugin Standalone"),
            CancellationToken.None).GetAwaiter().GetResult();
        DataContext = document;
    }
}
