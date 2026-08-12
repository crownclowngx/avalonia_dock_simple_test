using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Business.Constants;

/// <summary>
/// 宿主内建扩展的规范身份。
/// </summary>
/// <remarks>
/// static readonly 取代 const string，使调用方无法把 Document、Tool 与 Plugin 标识互换。
/// 历史值只在各自元数据的 LegacyIds 中出现，禁止继续作为新运行时身份使用。
/// </remarks>
internal static class HostExtensionIds
{
    internal static readonly PluginId Owner = new("myavalonia.host");
    internal static readonly DocumentTypeId WelcomeDocument = new("myavalonia.host.document.welcome");
    internal static readonly ToolTypeId FileSystemTree = new("myavalonia.host.tool.file-system-tree");
    internal static readonly ToolTypeId PluginMenu = new("myavalonia.host.tool.plugin-menu");
    internal static readonly ToolTypeId PluginStatus = new("myavalonia.host.tool.plugin-status");
    internal static readonly ToolTypeId ToolManagement = new("myavalonia.host.tool.management");
}
