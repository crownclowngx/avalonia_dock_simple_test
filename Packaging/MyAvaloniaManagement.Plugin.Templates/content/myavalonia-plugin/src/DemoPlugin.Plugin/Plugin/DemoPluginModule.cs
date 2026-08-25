using MyAvaloniaManagement.PluginSdk.UI;
using DemoPlugin.Constants;
using DemoPlugin.Features.Main;

namespace DemoPlugin.Plugin;

public sealed class TemplateProjectIdentifierModule : IPluginModule
{
    public void Configure(IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        registration.Services.AddTemplateProjectIdentifierServices();
        registration.AddDocument<MainDocument, MainView>(
            new DocumentDescriptor(
                PluginIds.MainDocument,
                "示例文档",
                "由独立预览程序和真实 Host 共用的示例功能",
                "DemoPlugin"));
    }
}
