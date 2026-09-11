using Avalonia.Controls;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace OldIconSdk;

/// <summary>只使用 3.3.0 契约和旧 builtin 引用的真实二进制消费夹具。</summary>
public sealed class OldModule : IPluginModule
{
    public void Configure(IPluginRegistration registration) =>
        registration.AddDocument<OldDocument, UserControl>(new DocumentDescriptor(
            new("myavalonia.plugin.old-icon-sdk.document.main"), "旧 SDK 功能", "兼容性夹具", "旧版", "builtin:table"));
}

public sealed class OldDocument : IPluginDocument
{
    public DocumentPresentationState Presentation { get; } = new("旧 SDK 功能");
    public event EventHandler? PresentationChanged { add { } remove { } }
    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
