using MyAvaloniaManagementCommon.Identity;

namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>
/// 插件在宿主进程内的稳定身份。
/// </summary>
/// <remarks>
/// 使用独立引用型值对象而不是 <see cref="string"/> 或值类型，可以同时阻止
/// Plugin、Document、Tool 三类标识在编译期误传，并避免值类型的 default 状态绕过校验。
/// </remarks>
public sealed record PluginId
{
    /// <summary>使用经过规范校验的字符串创建插件身份。</summary>
    /// <param name="value">跨清单、注册表和诊断保持不变的标识。</param>
    /// <exception cref="ArgumentException">值不满足稳定标识规则。</exception>
    public PluginId(string value) =>
        Value = StableIdentifierRules.Validate(value, nameof(value));

    /// <summary>获取 manifest、注册表和诊断共享的稳定字符串。</summary>
    public string Value { get; }

    /// <summary>获取该值是否满足当前稳定标识格式。</summary>
    public bool IsCanonical => StableIdentifierRules.IsCanonical(Value);

    /// <summary>解析插件身份。</summary>
    /// <param name="value">待解析字符串。</param>
    /// <returns>有效插件身份。</returns>
    public static PluginId Parse(string value) => new(value);

    /// <summary>尝试解析插件身份。</summary>
    /// <param name="value">待解析字符串。</param>
    /// <param name="pluginId">成功时为有效身份，否则为 <see langword="null"/>。</param>
    /// <returns>解析是否成功。</returns>
    public static bool TryParse(string? value, out PluginId? pluginId)
    {
        pluginId = StableIdentifierRules.TryValidate(value, out var validated)
            ? new PluginId(validated)
            : null;
        return pluginId is not null;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
