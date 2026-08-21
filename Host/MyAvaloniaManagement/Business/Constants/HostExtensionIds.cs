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

    // 以下 Legacy 强类型值只供 G7/G8 前的 Document v1、layout v1 与尚未迁移插件测试使用。
    // 它们不得写入声明式 Descriptor，也不得重新成为 Registry 的身份事实。
    internal static readonly PluginId Owner = new("myavalonia.host");
    internal static readonly DocumentTypeId WelcomeDocument = new("myavalonia.host.document.welcome");
    internal static readonly ToolTypeId FileSystemTree = new("myavalonia.host.tool.file-system-tree");
    internal static readonly ToolTypeId PluginMenu = new("myavalonia.host.tool.plugin-menu");
    internal static readonly ToolTypeId PluginStatus = new("myavalonia.host.tool.plugin-status");
    internal static readonly ToolTypeId ToolManagement = new("myavalonia.host.tool.management");
}
