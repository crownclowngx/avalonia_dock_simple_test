namespace MyAvaloniaManagement.Business.Constants;

/// <summary>
/// 宿主内建工作区项的规范身份。
/// </summary>
/// <remarks>
/// static readonly 取代 const string，使调用方无法把 Document、Tool 与 Plugin 标识互换。
/// Host Document/Tool ID 会进入布局或菜单引用，因此字符串保持稳定；Host 不再拥有 PluginId，
/// 也不会作为伪插件进入 Registry 或 Availability。
/// </remarks>
internal static class HostExtensionIds
{
    internal static readonly MyAvaloniaManagement.PluginSdk.DocumentTypeId WelcomeDocument =
        new("myavalonia.host.document.welcome");
    internal static readonly MyAvaloniaManagement.PluginSdk.ToolTypeId FileSystemTree =
        new("myavalonia.host.tool.file-system-tree");
    internal static readonly MyAvaloniaManagement.PluginSdk.ToolTypeId PluginMenu =
        new("myavalonia.host.tool.plugin-menu");
    internal static readonly MyAvaloniaManagement.PluginSdk.ToolTypeId PluginStatus =
        new("myavalonia.host.tool.plugin-status");
    internal static readonly MyAvaloniaManagement.PluginSdk.ToolTypeId ToolManagement =
        new("myavalonia.host.tool.management");

}
