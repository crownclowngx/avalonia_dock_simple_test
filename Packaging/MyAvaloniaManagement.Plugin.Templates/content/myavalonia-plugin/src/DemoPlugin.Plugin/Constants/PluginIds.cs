using MyAvaloniaManagement.PluginSdk;

namespace DemoPlugin.Constants;

public static class PluginIds
{
    public static readonly PluginId Plugin = new("myavalonia.plugin.demo");

    public static readonly DocumentTypeId MainDocument =
        new("myavalonia.plugin.demo.document.main");
}
