using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginSdk.UI;

/// <summary>表示 Tool 在 Host 工作区中的默认停靠方向。</summary>
public enum ToolDockSide
{
    /// <summary>停靠在左侧。</summary>
    Left,
    /// <summary>停靠在右侧。</summary>
    Right,
    /// <summary>停靠在顶部。</summary>
    Top,
    /// <summary>停靠在底部。</summary>
    Bottom,
}

/// <summary>定义用户执行关闭操作时 Host 对 Tool 的处理方式。</summary>
public enum ToolCloseBehavior
{
    /// <summary>关闭操作只隐藏 Tool，后续可由 Tool 管理入口恢复。</summary>
    Hide,
    /// <summary>禁止用户关闭或隐藏该 Tool。</summary>
    Prevent,
}

/// <summary>描述同一 Document 类型内部的一个不可变创建入口。</summary>
/// <remarks>本对象只保存可验证数据，不包含回调或工厂，避免读取菜单元数据时执行插件代码。</remarks>
public sealed class DocumentCreationIntentDescriptor
{
    /// <summary>创建一个入口描述。</summary>
    /// <param name="intentId">在所属 Document 类型内唯一的稳定身份。</param>
    /// <param name="displayName">面向用户的非空白名称。</param>
    /// <param name="description">可选说明；默认空字符串。</param>
    /// <param name="iconPath">可选语义图标路径；默认空字符串。</param>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    /// <exception cref="ArgumentException">显示名称为空字符串或纯空白。</exception>
    public DocumentCreationIntentDescriptor(
        CreationIntentId intentId,
        string displayName,
        string description = "",
        string iconPath = "")
    {
        IntentId = intentId ?? throw new ArgumentNullException(nameof(intentId));
        DisplayName = DescriptorValidation.RequireDisplayText(displayName, nameof(displayName));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        IconPath = iconPath ?? throw new ArgumentNullException(nameof(iconPath));
    }

    /// <summary>获取所属 Document 类型内唯一的创建意图。</summary>
    public CreationIntentId IntentId { get; }
    /// <summary>获取展示给用户的入口名称。</summary>
    public string DisplayName { get; }
    /// <summary>获取入口用途说明；空字符串表示不展示说明。</summary>
    public string Description { get; }
    /// <summary>获取可选语义图标路径；空字符串表示使用 Host 默认图标。</summary>
    public string IconPath { get; }
}

/// <summary>一次性描述一种 Document 的身份、菜单和 ViewModel 贡献。</summary>
/// <remarks>构造时复制创建意图集合，插件不能在注册后改变 Host 已验证的元数据。</remarks>
public sealed class DocumentDescriptor
{
    /// <summary>创建不可变 Document 描述符。</summary>
    /// <param name="documentTypeId">由 Host 在全局贡献表中判重的稳定身份。</param>
    /// <param name="displayName">面向用户的非空白名称。</param>
    /// <param name="description">功能说明；允许空字符串但不能为 null。</param>
    /// <param name="menuCategory">创建菜单中的非空白分组名称。</param>
    /// <param name="iconPath">可选语义图标路径；默认空字符串。</param>
    /// <param name="creationIntents">可选入口集合；构造时按输入顺序复制并冻结。</param>
    /// <exception cref="ArgumentNullException">身份、显示名称、说明、菜单分组或图标路径为 null。</exception>
    /// <exception cref="ArgumentException">显示字段为空白，或入口包含 null/重复身份。</exception>
    public DocumentDescriptor(
        DocumentTypeId documentTypeId,
        string displayName,
        string description,
        string menuCategory,
        string iconPath = "",
        IEnumerable<DocumentCreationIntentDescriptor>? creationIntents = null)
    {
        DocumentTypeId = documentTypeId ?? throw new ArgumentNullException(nameof(documentTypeId));
        DisplayName = DescriptorValidation.RequireDisplayText(displayName, nameof(displayName));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        MenuCategory = DescriptorValidation.RequireDisplayText(menuCategory, nameof(menuCategory));
        IconPath = iconPath ?? throw new ArgumentNullException(nameof(iconPath));

        var intents = (creationIntents ?? []).ToArray();
        if (intents.Any(intent => intent is null))
        {
            throw new ArgumentException("创建意图集合不能包含 null。", nameof(creationIntents));
        }

        var duplicate = intents.GroupBy(intent => intent.IntentId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"创建意图 {duplicate.Key.Value} 重复。", nameof(creationIntents));
        }

        CreationIntents = Array.AsReadOnly(intents);
    }

    /// <summary>获取全局稳定的 Document 类型身份。</summary>
    public DocumentTypeId DocumentTypeId { get; }
    /// <summary>获取展示名称。</summary>
    public string DisplayName { get; }
    /// <summary>获取功能说明。</summary>
    public string Description { get; }
    /// <summary>获取 Host 创建菜单的分组名称。</summary>
    public string MenuCategory { get; }
    /// <summary>获取可选语义图标路径。</summary>
    public string IconPath { get; }
    /// <summary>获取稳定顺序、不可修改的创建入口。</summary>
    public IReadOnlyList<DocumentCreationIntentDescriptor> CreationIntents { get; }
}

/// <summary>一次性描述一种 Tool 的身份、默认位置、关闭行为和 ViewModel 贡献。</summary>
/// <remarks>描述符不认识 Dock 类型；Host Adapter 负责把方向和关闭策略投影到实际工作区。</remarks>
public sealed class ToolDescriptor
{
    /// <summary>创建不可变 Tool 描述符。</summary>
    /// <param name="toolTypeId">由 Host 在全局贡献表中判重的稳定身份。</param>
    /// <param name="displayName">面向用户的非空白名称。</param>
    /// <param name="description">功能说明；允许空字符串但不能为 null。</param>
    /// <param name="dockSide">Host 首次创建 Tool 时使用的四向位置。</param>
    /// <param name="closeBehavior">用户请求关闭时采用的隐藏或禁止策略。</param>
    /// <param name="iconPath">可选语义图标路径；默认空字符串。</param>
    /// <exception cref="ArgumentNullException">身份或字符串参数为 null。</exception>
    /// <exception cref="ArgumentException">显示名称为空字符串或纯空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException">方向或关闭策略不是已定义枚举值。</exception>
    public ToolDescriptor(
        ToolTypeId toolTypeId,
        string displayName,
        string description,
        ToolDockSide dockSide,
        ToolCloseBehavior closeBehavior,
        string iconPath = "")
    {
        ToolTypeId = toolTypeId ?? throw new ArgumentNullException(nameof(toolTypeId));
        DisplayName = DescriptorValidation.RequireDisplayText(displayName, nameof(displayName));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        if (!Enum.IsDefined(dockSide))
        {
            throw new ArgumentOutOfRangeException(nameof(dockSide), dockSide, "Tool 默认停靠方向无效。");
        }
        if (!Enum.IsDefined(closeBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(closeBehavior), closeBehavior, "Tool 关闭行为无效。");
        }
        IconPath = iconPath ?? throw new ArgumentNullException(nameof(iconPath));
        DockSide = dockSide;
        CloseBehavior = closeBehavior;
    }

    /// <summary>获取全局稳定的 Tool 类型身份。</summary>
    public ToolTypeId ToolTypeId { get; }
    /// <summary>获取展示名称。</summary>
    public string DisplayName { get; }
    /// <summary>获取功能说明。</summary>
    public string Description { get; }
    /// <summary>获取可选语义图标路径。</summary>
    public string IconPath { get; }
    /// <summary>获取 Host 初次创建 Tool 时使用的停靠方向。</summary>
    public ToolDockSide DockSide { get; }
    /// <summary>获取用户关闭 Tool 时的处理方式。</summary>
    public ToolCloseBehavior CloseBehavior { get; }
}

/// <summary>集中处理 Descriptor 自身负责的轻量输入不变量。</summary>
internal static class DescriptorValidation
{
    internal static string RequireDisplayText(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("展示文本不能是空字符串或纯空白。", parameterName);
        }
        return value;
    }
}
