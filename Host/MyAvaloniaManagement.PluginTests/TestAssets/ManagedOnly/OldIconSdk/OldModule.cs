using Avalonia.Controls;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace OldIconSdk;

/// <summary>只使用 3.3.0 契约和旧 builtin 引用的真实二进制消费夹具。</summary>
public sealed class OldModule : IPluginModule
{
    public void Configure(IPluginRegistration registration)
    {
        registration.AddDocument<OldDocument, UserControl>(new DocumentDescriptor(
            new("myavalonia.plugin.old-icon-sdk.document.main"), "旧 SDK 功能", "兼容性夹具", "旧版", "builtin:table"));
        registration.AddTool<OldTool, UserControl>(new ToolDescriptor(
            new("myavalonia.plugin.old-icon-sdk.tool.prevent"), "旧 SDK Prevent 工具", "V7 隐藏兼容夹具",
            ToolDockSide.Right, ToolCloseBehavior.Prevent));
    }
}

public sealed class OldTool { public int Counter { get; set; } = 42; }

public sealed class OldDocument : IPluginDocument
{
    public DocumentPresentationState Presentation { get; } = new("旧 SDK 功能");
    public event EventHandler? PresentationChanged { add { } remove { } }
    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
