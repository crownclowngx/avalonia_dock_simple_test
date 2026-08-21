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
    /// <summary>使用经过规范校验的字符串创建入口标识。</summary>
    /// <param name="value">所属 Document 类型内唯一的 kebab-case 标识。</param>
    /// <exception cref="ArgumentException">值为空、包含空白或不满足稳定标识规则。</exception>
    public CreationIntentId(string value) =>
        Value = StableIdentifierRules.Validate(value, nameof(value));

    /// <summary>获取不经本地化、可持久化的稳定字符串。</summary>
    public string Value { get; }

    /// <summary>获取该值是否为不带命名空间的规范创建意图。</summary>
    public bool IsCanonical =>
        !Value.Contains('.') && StableIdentifierRules.IsCanonical(Value);

    /// <summary>解析创建意图；无效输入通过异常明确拒绝。</summary>
    /// <param name="value">待解析值。</param>
    /// <returns>有效的创建意图标识。</returns>
    public static CreationIntentId Parse(string value) => new(value);

    /// <summary>尝试解析创建意图，不把预期的用户输入错误转换成异常。</summary>
    /// <param name="value">待解析值。</param>
    /// <param name="intentId">成功时为标识；失败时为 <see langword="null"/>。</param>
    /// <returns>输入满足稳定标识规则时为 <see langword="true"/>。</returns>
    public static bool TryParse(string? value, out CreationIntentId? intentId)
    {
        intentId = StableIdentifierRules.TryValidate(value, out var validated)
            ? new CreationIntentId(validated)
            : null;
        return intentId is not null;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
