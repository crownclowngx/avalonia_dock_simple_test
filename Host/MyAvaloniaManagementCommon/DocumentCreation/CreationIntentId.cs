using MyAvaloniaManagementCommon.Identity;

namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 同一 Document 类型内部的创建入口标识。
/// </summary>
/// <remarks>
/// 创建意图只在所属 Document 类型内唯一，因此采用简短 kebab-case；没有显式意图时使用 null，
/// 不再用空字符串同时表达“默认值”和“无效值”。
/// </remarks>
public sealed record CreationIntentId
{
    public CreationIntentId(string value) =>
        Value = StableIdentifierRules.Validate(value, nameof(value));

    public string Value { get; }

    public bool IsCanonical =>
        !Value.Contains('.') && StableIdentifierRules.IsCanonical(Value);

    public static CreationIntentId Parse(string value) => new(value);

    public static bool TryParse(string? value, out CreationIntentId? intentId)
    {
        intentId = StableIdentifierRules.TryValidate(value, out var validated)
            ? new CreationIntentId(validated)
            : null;
        return intentId is not null;
    }

    public override string ToString() => Value;
}
