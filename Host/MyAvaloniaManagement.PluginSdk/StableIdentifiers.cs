using System.Diagnostics.CodeAnalysis;

namespace MyAvaloniaManagement.PluginSdk;

/// <summary>
/// 为当前 V3 插件、Document、Tool 和创建意图提供统一且严格的稳定标识校验。
/// </summary>
/// <remarks>
/// 本类型保持 internal，避免把一套可被绕过的“通用字符串校验器”扩张为 public API。
/// 值对象只负责词法正确性；标识是否属于某个插件，由 Host 在汇总贡献时统一验证。
/// </remarks>
internal static class StableIdentifierRules
{
    internal const int MaximumLength = 128;

    internal static string Validate(string value, string parameterName, bool allowDots)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!IsValid(value, allowDots))
        {
            throw new ArgumentException(
                allowDots
                    ? "稳定标识必须为 1-128 个字符，并由小写 ASCII 字母、数字和短横线组成的非空点分段构成。"
                    : "创建意图必须为 1-128 个字符的小写 kebab-case，且不能包含点号。",
                parameterName);
        }

        return value;
    }

    internal static bool TryValidate(
        string? value,
        bool allowDots,
        [NotNullWhen(true)] out string? validated)
    {
        validated = IsValid(value, allowDots) ? value : null;
        return validated is not null;
    }

    private static bool IsValid(string? value, bool allowDots)
    {
        if (value is null || value.Length is 0 or > MaximumLength)
        {
            return false;
        }

        var segments = allowDots ? value.Split('.') : [value];
        return segments.All(segment =>
            segment.Length > 0 &&
            char.IsAsciiLetterOrDigit(segment[0]) &&
            char.IsAsciiLetterOrDigit(segment[^1]) &&
            segment.All(character =>
                char.IsAsciiLetterLower(character) ||
                char.IsAsciiDigit(character) ||
                character == '-'));
    }
}

/// <summary>表示 manifest、注册表和诊断共同使用的当前插件身份。</summary>
public sealed record PluginId
{
    /// <summary>使用经过当前规范校验的稳定字符串创建插件身份。</summary>
    /// <param name="value">小写点分/kebab-case 插件身份。</param>
    /// <exception cref="ArgumentException">值不满足当前稳定标识规则。</exception>
    public PluginId(string value) => Value = StableIdentifierRules.Validate(value, nameof(value), true);

    /// <summary>获取不经本地化、可跨 manifest 和注册表持久化的字符串。</summary>
    public string Value { get; }

    /// <summary>解析插件身份；非法输入通过异常明确拒绝。</summary>
    /// <param name="value">待解析的小写点分/kebab-case 字符串。</param>
    /// <returns>具有值相等语义的插件身份。</returns>
    /// <exception cref="ArgumentNullException">输入为 null。</exception>
    /// <exception cref="ArgumentException">输入不满足当前词法规则。</exception>
    public static PluginId Parse(string value) => new(value);

    /// <summary>尝试解析插件身份，不把预期输入错误转换为异常。</summary>
    /// <param name="value">待解析字符串；可以为 null。</param>
    /// <param name="pluginId">成功时为解析后的身份，失败时为 null。</param>
    /// <returns>输入满足当前词法规则时为 true，否则为 false。</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out PluginId? pluginId)
    {
        pluginId = StableIdentifierRules.TryValidate(value, true, out var validated)
            ? new PluginId(validated)
            : null;
        return pluginId is not null;
    }

    /// <summary>返回可直接写入 manifest、日志白名单或注册表的规范字符串。</summary>
    /// <returns>与 <see cref="Value"/> 相同的稳定字符串。</returns>
    public override string ToString() => Value;
}

/// <summary>表示一种由插件贡献、由 Host 创建和持久化的 Document 类型。</summary>
public sealed record DocumentTypeId
{
    /// <summary>使用经过当前规范校验的稳定字符串创建 Document 类型身份。</summary>
    /// <param name="value">长度为 1–128 的小写点分/kebab-case 字符串。</param>
    /// <exception cref="ArgumentNullException">输入为 null。</exception>
    /// <exception cref="ArgumentException">输入不满足当前词法规则。</exception>
    public DocumentTypeId(string value) => Value = StableIdentifierRules.Validate(value, nameof(value), true);

    /// <summary>获取注册表和 Document 信封使用的稳定字符串。</summary>
    public string Value { get; }

    /// <summary>解析 Document 类型身份。</summary>
    /// <param name="value">待解析的规范字符串。</param>
    /// <returns>具有值相等语义的 Document 类型身份。</returns>
    /// <exception cref="ArgumentNullException">输入为 null。</exception>
    /// <exception cref="ArgumentException">输入不满足当前词法规则。</exception>
    public static DocumentTypeId Parse(string value) => new(value);

    /// <summary>尝试解析 Document 类型身份。</summary>
    /// <param name="value">待解析字符串；可以为 null。</param>
    /// <param name="documentTypeId">成功时为解析后的身份，失败时为 null。</param>
    /// <returns>输入满足当前词法规则时为 true，否则为 false。</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out DocumentTypeId? documentTypeId)
    {
        documentTypeId = StableIdentifierRules.TryValidate(value, true, out var validated)
            ? new DocumentTypeId(validated)
            : null;
        return documentTypeId is not null;
    }

    /// <summary>返回注册表与 Document 信封使用的规范字符串。</summary>
    /// <returns>与 <see cref="Value"/> 相同的稳定字符串。</returns>
    public override string ToString() => Value;
}

/// <summary>表示一种由插件贡献、由 Host 适配到 Dock 的单例 Tool 类型。</summary>
public sealed record ToolTypeId
{
    /// <summary>使用经过当前规范校验的稳定字符串创建 Tool 类型身份。</summary>
    /// <param name="value">长度为 1–128 的小写点分/kebab-case 字符串。</param>
    /// <exception cref="ArgumentNullException">输入为 null。</exception>
    /// <exception cref="ArgumentException">输入不满足当前词法规则。</exception>
    public ToolTypeId(string value) => Value = StableIdentifierRules.Validate(value, nameof(value), true);

    /// <summary>获取注册表和布局快照使用的稳定字符串。</summary>
    public string Value { get; }

    /// <summary>解析 Tool 类型身份。</summary>
    /// <param name="value">待解析的规范字符串。</param>
    /// <returns>具有值相等语义的 Tool 类型身份。</returns>
    /// <exception cref="ArgumentNullException">输入为 null。</exception>
    /// <exception cref="ArgumentException">输入不满足当前词法规则。</exception>
    public static ToolTypeId Parse(string value) => new(value);

    /// <summary>尝试解析 Tool 类型身份。</summary>
    /// <param name="value">待解析字符串；可以为 null。</param>
    /// <param name="toolTypeId">成功时为解析后的身份，失败时为 null。</param>
    /// <returns>输入满足当前词法规则时为 true，否则为 false。</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out ToolTypeId? toolTypeId)
    {
        toolTypeId = StableIdentifierRules.TryValidate(value, true, out var validated)
            ? new ToolTypeId(validated)
            : null;
        return toolTypeId is not null;
    }

    /// <summary>返回布局与注册表使用的规范字符串。</summary>
    /// <returns>与 <see cref="Value"/> 相同的稳定字符串。</returns>
    public override string ToString() => Value;
}

/// <summary>表示同一 Document 类型内部的一个创建入口。</summary>
/// <remarks>创建意图只在所属 Document 类型内唯一，因此只能使用单段 kebab-case。</remarks>
public sealed record CreationIntentId
{
    /// <summary>使用经过当前规范校验的字符串创建入口身份。</summary>
    /// <param name="value">长度为 1–128 的单段小写 kebab-case 字符串。</param>
    /// <exception cref="ArgumentNullException">输入为 null。</exception>
    /// <exception cref="ArgumentException">输入包含点号、大小写或其他非法形式。</exception>
    public CreationIntentId(string value) => Value = StableIdentifierRules.Validate(value, nameof(value), false);

    /// <summary>获取所属 Document 类型内稳定的入口字符串。</summary>
    public string Value { get; }

    /// <summary>解析创建意图身份。</summary>
    /// <param name="value">待解析的单段规范字符串。</param>
    /// <returns>具有值相等语义的创建意图身份。</returns>
    /// <exception cref="ArgumentNullException">输入为 null。</exception>
    /// <exception cref="ArgumentException">输入不满足当前创建意图规则。</exception>
    public static CreationIntentId Parse(string value) => new(value);

    /// <summary>尝试解析创建意图身份。</summary>
    /// <param name="value">待解析字符串；可以为 null。</param>
    /// <param name="creationIntentId">成功时为解析后的身份，失败时为 null。</param>
    /// <returns>输入是规范单段 kebab-case 时为 true，否则为 false。</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out CreationIntentId? creationIntentId)
    {
        creationIntentId = StableIdentifierRules.TryValidate(value, false, out var validated)
            ? new CreationIntentId(validated)
            : null;
        return creationIntentId is not null;
    }

    /// <summary>返回 Document 创建入口使用的规范字符串。</summary>
    /// <returns>与 <see cref="Value"/> 相同的稳定字符串。</returns>
    public override string ToString() => Value;
}
