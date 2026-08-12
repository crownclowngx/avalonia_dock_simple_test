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
    public PluginId(string value) =>
        Value = StableIdentifierRules.Validate(value, nameof(value));

    public string Value { get; }

    public bool IsCanonical => StableIdentifierRules.IsCanonical(Value);

    public static PluginId Parse(string value) => new(value);

    public static bool TryParse(string? value, out PluginId? pluginId)
    {
        pluginId = StableIdentifierRules.TryValidate(value, out var validated)
            ? new PluginId(validated)
            : null;
        return pluginId is not null;
    }

    public override string ToString() => Value;
}
