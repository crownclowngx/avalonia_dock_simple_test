using System.Diagnostics.CodeAnalysis;
using Avalonia.Input;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginSdk.UI;

/// <summary>表示同一插件内一条工作台命令展示声明的稳定身份。</summary>
/// <remarks>
/// Placement 身份与 Command 身份分离，使同一命令可以安全地拥有菜单和快捷键等多个投影，
/// 同时让 Host 能分别治理展示排序与资源冲突。所有权由 Host 在注册 Seal 时验证。
/// </remarks>
public sealed record CommandPlacementId
{
    /// <summary>使用经过稳定标识规则校验的字符串创建展示身份。</summary>
    /// <param name="value">长度为 1–128 的小写点分/kebab-case 字符串。</param>
    public CommandPlacementId(string value) =>
        Value = CommandId.Parse(value).Value;

    /// <summary>获取注册表使用的规范字符串。</summary>
    public string Value { get; }

    /// <summary>解析展示身份；非法输入通过异常明确拒绝。</summary>
    /// <param name="value">待解析的规范字符串。</param>
    /// <returns>具有值相等语义的展示身份。</returns>
    public static CommandPlacementId Parse(string value) => new(value);

    /// <summary>尝试解析展示身份。</summary>
    /// <param name="value">待解析字符串；可以为 null。</param>
    /// <param name="placementId">成功时为解析后的身份，失败时为 null。</param>
    /// <returns>输入满足稳定标识规则时为 true，否则为 false。</returns>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out CommandPlacementId? placementId)
    {
        placementId = CommandId.TryParse(value, out var validated)
            ? new CommandPlacementId(validated.Value)
            : null;
        return placementId is not null;
    }

    /// <summary>返回注册表使用的规范字符串。</summary>
    /// <returns>与 <see cref="Value"/> 相同的字符串。</returns>
    public override string ToString() => Value;
}

/// <summary>表示由 Host 拥有并向插件开放的稳定菜单位置。</summary>
/// <remarks>
/// 位置不是本地化菜单路径，也不允许插件借此创建嵌套容器。Host 只接受
/// <see cref="WorkbenchMenuLocations"/> 中明确开放的共享末端位置。
/// </remarks>
public sealed record MenuLocationId
{
    /// <summary>使用经过稳定标识规则校验的字符串创建菜单位置身份。</summary>
    /// <param name="value">长度为 1–128 的小写点分/kebab-case 字符串。</param>
    public MenuLocationId(string value) =>
        Value = CommandId.Parse(value).Value;

    /// <summary>获取注册表使用的规范字符串。</summary>
    public string Value { get; }

    /// <summary>解析菜单位置身份；非法输入通过异常明确拒绝。</summary>
    /// <param name="value">待解析的规范字符串。</param>
    /// <returns>具有值相等语义的菜单位置身份。</returns>
    public static MenuLocationId Parse(string value) => new(value);

    /// <summary>尝试解析菜单位置身份。</summary>
    /// <param name="value">待解析字符串；可以为 null。</param>
    /// <param name="locationId">成功时为解析后的身份，失败时为 null。</param>
    /// <returns>输入满足稳定标识规则时为 true，否则为 false。</returns>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out MenuLocationId? locationId)
    {
        locationId = CommandId.TryParse(value, out var validated)
            ? new MenuLocationId(validated.Value)
            : null;
        return locationId is not null;
    }

    /// <summary>返回 Host 菜单注册表使用的规范字符串。</summary>
    /// <returns>与 <see cref="Value"/> 相同的字符串。</returns>
    public override string ToString() => Value;
}

/// <summary>提供 Workbench Command v1 唯一开放的四个 Host 菜单共享末端位置。</summary>
public static class WorkbenchMenuLocations
{
    /// <summary>获取 File 顶级菜单中 Host 内建项之后的共享位置。</summary>
    public static MenuLocationId FileShared { get; } =
        new("myavalonia.host.menu.file.shared");

    /// <summary>获取 View 顶级菜单中 Host 内建项之后的共享位置。</summary>
    public static MenuLocationId ViewShared { get; } =
        new("myavalonia.host.menu.view.shared");

    /// <summary>获取 Tools 顶级菜单中 Host 内建项之后的共享位置。</summary>
    public static MenuLocationId ToolsShared { get; } =
        new("myavalonia.host.menu.tools.shared");

    /// <summary>获取 Help 顶级菜单中 Host 内建项之后的共享位置。</summary>
    public static MenuLocationId HelpShared { get; } =
        new("myavalonia.host.menu.help.shared");
}

/// <summary>定义目标 Document 不成立时菜单项采用的展示政策。</summary>
public enum MenuCommandTargetUnavailableBehavior
{
    /// <summary>目标不成立时不展示菜单项。</summary>
    Hide,

    /// <summary>目标不成立时保留菜单项但禁用。</summary>
    Disable,
}

/// <summary>描述一条不可变工作台命令的稳定身份与规范展示文本。</summary>
/// <remarks>本对象不保存 Target、Handler、回调或运行状态，读取元数据不会执行插件代码。</remarks>
public sealed class CommandDescriptor
{
    /// <summary>创建一条工作台命令描述符。</summary>
    /// <param name="commandId">由 Host 在注册表中验证所有权并判重的稳定身份。</param>
    /// <param name="displayName">面向用户的非空白规范名称。</param>
    /// <param name="description">功能说明；允许空字符串但不能为 null。</param>
    /// <param name="iconPath">可选语义图标路径；默认空字符串。</param>
    public CommandDescriptor(
        CommandId commandId,
        string displayName,
        string description,
        string iconPath = "")
    {
        CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
        DisplayName = DescriptorValidation.RequireDisplayText(displayName, nameof(displayName));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        IconPath = iconPath ?? throw new ArgumentNullException(nameof(iconPath));
    }

    /// <summary>获取工作台命令的稳定身份。</summary>
    public CommandId CommandId { get; }

    /// <summary>获取面向用户的规范名称。</summary>
    public string DisplayName { get; }

    /// <summary>获取功能说明。</summary>
    public string Description { get; }

    /// <summary>获取可选语义图标路径。</summary>
    public string IconPath { get; }
}

/// <summary>描述一条 Command 在 Host 共享菜单末端位置中的不可变投影声明。</summary>
public sealed class MenuCommandContributionDescriptor
{
    /// <summary>创建菜单命令贡献。</summary>
    /// <param name="placementId">当前插件内唯一的展示声明身份。</param>
    /// <param name="commandId">要投影的当前插件命令身份。</param>
    /// <param name="locationId">Host 明确开放的共享菜单位置。</param>
    /// <param name="group">稳定排序和分隔使用的分组；允许空字符串但不能为 null。</param>
    /// <param name="order">组内排序值；相同时最终按 PlacementId 排序。</param>
    /// <param name="targetUnavailableBehavior">目标 Document 不成立时隐藏或禁用菜单项。</param>
    public MenuCommandContributionDescriptor(
        CommandPlacementId placementId,
        CommandId commandId,
        MenuLocationId locationId,
        string group,
        int order,
        MenuCommandTargetUnavailableBehavior targetUnavailableBehavior =
            MenuCommandTargetUnavailableBehavior.Hide)
    {
        PlacementId = placementId ?? throw new ArgumentNullException(nameof(placementId));
        CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
        LocationId = locationId ?? throw new ArgumentNullException(nameof(locationId));
        Group = group ?? throw new ArgumentNullException(nameof(group));
        if (!Enum.IsDefined(targetUnavailableBehavior))
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetUnavailableBehavior),
                targetUnavailableBehavior,
                "菜单命令目标不可用展示政策无效。");
        }

        Order = order;
        TargetUnavailableBehavior = targetUnavailableBehavior;
    }

    /// <summary>获取当前插件内唯一的展示声明身份。</summary>
    public CommandPlacementId PlacementId { get; }

    /// <summary>获取要投影的命令身份。</summary>
    public CommandId CommandId { get; }

    /// <summary>获取 Host 共享菜单位置。</summary>
    public MenuLocationId LocationId { get; }

    /// <summary>获取确定性排序和分隔使用的分组。</summary>
    public string Group { get; }

    /// <summary>获取组内排序值。</summary>
    public int Order { get; }

    /// <summary>获取目标 Document 不成立时的菜单展示政策。</summary>
    public MenuCommandTargetUnavailableBehavior TargetUnavailableBehavior { get; }
}

/// <summary>描述一条 Command 的不可变 Avalonia 键盘绑定声明。</summary>
/// <remarks>
/// 本对象只保存 Avalonia 的键和修饰键枚举，不创建 <c>KeyBinding</c>、不解析字符串 Gesture，
/// 也不保存执行回调。Host 会在后续展示阶段统一处理保留项与跨插件冲突。
/// </remarks>
public sealed class KeyBindingContributionDescriptor
{
    private static readonly KeyModifiers AllowedModifiers =
        Enum.GetValues<KeyModifiers>().Aggregate(
            KeyModifiers.None,
            static (current, value) => current | value);

    /// <summary>创建键盘绑定贡献。</summary>
    /// <param name="placementId">当前插件内唯一的展示声明身份。</param>
    /// <param name="commandId">要触发的当前插件命令身份。</param>
    /// <param name="key">非 None 且由当前 Avalonia 版本定义的键。</param>
    /// <param name="modifiers">由当前 Avalonia 版本定义的修饰键组合；允许 None。</param>
    public KeyBindingContributionDescriptor(
        CommandPlacementId placementId,
        CommandId commandId,
        Key key,
        KeyModifiers modifiers)
    {
        PlacementId = placementId ?? throw new ArgumentNullException(nameof(placementId));
        CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
        if (key == Key.None || !Enum.IsDefined(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "快捷键主键无效。");
        }
        if ((modifiers & ~AllowedModifiers) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modifiers), modifiers, "快捷键修饰键组合无效。");
        }

        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>获取当前插件内唯一的展示声明身份。</summary>
    public CommandPlacementId PlacementId { get; }

    /// <summary>获取要触发的命令身份。</summary>
    public CommandId CommandId { get; }

    /// <summary>获取 Avalonia 主键。</summary>
    public Key Key { get; }

    /// <summary>获取 Avalonia 修饰键组合。</summary>
    public KeyModifiers Modifiers { get; }
}
