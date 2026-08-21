namespace MyAvaloniaManagement.Business.Constants;

/// <summary>
/// 宿主内建扩展的规范身份。
/// </summary>
/// <remarks>
/// static readonly 取代 const string，使调用方无法把 Document、Tool 与 Plugin 标识互换。
/// G13 后只保留 V2 主身份；历史值仅存在于版本历史文档，不能进入运行时目录。
/// </remarks>
internal static class HostExtensionIds
{
    /// <summary>G5 声明式 Registry 使用的最终 SDK 宿主身份。</summary>
    internal static readonly MyAvaloniaManagement.PluginSdk.PluginId V2Owner =
        new("myavalonia.host");
    internal static readonly MyAvaloniaManagement.PluginSdk.DocumentTypeId V2WelcomeDocument =
        new("myavalonia.host.document.welcome");
    internal static readonly MyAvaloniaManagement.PluginSdk.ToolTypeId V2FileSystemTree =
        new("myavalonia.host.tool.file-system-tree");
    internal static readonly MyAvaloniaManagement.PluginSdk.ToolTypeId V2PluginMenu =
        new("myavalonia.host.tool.plugin-menu");
    internal static readonly MyAvaloniaManagement.PluginSdk.ToolTypeId V2PluginStatus =
        new("myavalonia.host.tool.plugin-status");
    internal static readonly MyAvaloniaManagement.PluginSdk.ToolTypeId V2ToolManagement =
        new("myavalonia.host.tool.management");

}
