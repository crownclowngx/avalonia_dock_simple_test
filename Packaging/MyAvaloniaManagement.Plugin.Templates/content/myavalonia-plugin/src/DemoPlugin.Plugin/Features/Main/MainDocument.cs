using CommunityToolkit.Mvvm.ComponentModel;
using MyAvaloniaManagement.PluginSdk;

namespace DemoPlugin.Features.Main;

public sealed partial class MainDocument : ObservableObject, IPluginDocument
{
    private DocumentPresentationState _presentation = new("示例文档");

    [ObservableProperty]
    private string _message = "Hello from DemoPlugin";

    public DocumentPresentationState Presentation => _presentation;

    public event EventHandler? PresentationChanged;

    public ValueTask InitializeAsync(
        DocumentActivation activation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(activation.Title))
        {
            _presentation = new DocumentPresentationState(activation.Title);
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }

        return ValueTask.CompletedTask;
    }
}
