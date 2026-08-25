using MyAvaloniaManagement.PluginSdk.UI;

namespace WorkflowActionG1.Consumer;

/// <summary>只声明 caller-bound Gateway 的 G1 Consumer。</summary>
public sealed class ConsumerModule : IPluginModule
{
    /// <inheritdoc />
    public void Configure(IPluginRegistration registration) =>
        registration.UseWorkflowActionGateway();
}
