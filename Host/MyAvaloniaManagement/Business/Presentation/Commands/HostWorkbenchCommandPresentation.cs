using System.Collections.Generic;
using Avalonia.Input;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

/// <summary>冻结 Host 自己拥有的菜单与快捷键声明。</summary>
/// <remarks>
/// Host 声明与插件 Registry 保持分离，避免为了复用投影算法伪造一个 Host PluginId。
/// Descriptor 只保存稳定身份和键枚举；真正的 MenuItem、Separator 与 KeyBinding 始终由 Host View 创建。
/// </remarks>
internal sealed class HostWorkbenchCommandProjectionCatalog
{
    internal HostWorkbenchCommandProjectionCatalog()
    {
        MenuContributions =
        [
            new MenuCommandContributionDescriptor(
                new CommandPlacementId(
                    "myavalonia.host.command-placement.menu.file.open-document"),
                HostWorkbenchCommandIds.OpenDocument,
                WorkbenchMenuLocations.FileShared,
                group: string.Empty,
                order: 0,
                MenuCommandTargetUnavailableBehavior.Disable),
            new MenuCommandContributionDescriptor(
                new CommandPlacementId(
                    "myavalonia.host.command-placement.menu.file.save-document"),
                HostWorkbenchCommandIds.SaveDocument,
                WorkbenchMenuLocations.FileShared,
                group: string.Empty,
                order: 10,
                MenuCommandTargetUnavailableBehavior.Disable),
        ];
        KeyBindingContributions =
        [
            new KeyBindingContributionDescriptor(
                new CommandPlacementId(
                    "myavalonia.host.command-placement.key-binding.save-document"),
                HostWorkbenchCommandIds.SaveDocument,
                Key.S,
                KeyModifiers.Control),
        ];
    }

    /// <summary>获取 Host 保留菜单项的不可变声明快照。</summary>
    internal IReadOnlyList<MenuCommandContributionDescriptor> MenuContributions { get; }

    /// <summary>获取 Host 保留快捷键的不可变声明快照。</summary>
    internal IReadOnlyList<KeyBindingContributionDescriptor> KeyBindingContributions { get; }
}
