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
        ReservedKeyGestures =
        [
            HostWorkbenchKeyGestures.CommandPalette,
        ];
    }

    /// <summary>获取 Host 保留菜单项的不可变声明快照。</summary>
    internal IReadOnlyList<MenuCommandContributionDescriptor> MenuContributions { get; }

    /// <summary>获取 Host 保留快捷键的不可变声明快照。</summary>
    internal IReadOnlyList<KeyBindingContributionDescriptor> KeyBindingContributions { get; }

    /// <summary>
    /// 获取由 Host 窗口壳层直接处理、但仍必须参与插件快捷键冲突治理的保留组合。
    /// </summary>
    /// <remarks>
    /// Command Palette 的打开行为不是工作台业务命令，因此不会伪造 CommandId 或 Handler；
    /// 这里仅冻结其键盘资源所有权，确保插件声明相同组合时继续按 Host 优先政策安全禁用。
    /// </remarks>
    internal IReadOnlyList<WorkbenchKeyGesture> ReservedKeyGestures { get; }
}

/// <summary>集中定义 Host 窗口壳层拥有的稳定快捷键。</summary>
internal static class HostWorkbenchKeyGestures
{
    /// <summary>获取打开最小 Command Palette 的 Host 保留组合。</summary>
    internal static WorkbenchKeyGesture CommandPalette { get; } =
        new(Key.P, KeyModifiers.Control | KeyModifiers.Shift);
}

/// <summary>表示已经完成枚举解析的 Host internal 键盘组合。</summary>
internal readonly record struct WorkbenchKeyGesture(Key Key, KeyModifiers Modifiers);
