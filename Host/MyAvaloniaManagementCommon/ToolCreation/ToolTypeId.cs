using MyAvaloniaManagementCommon.Identity;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAvaloniaManagementCommon.ToolCreation;

/// <summary>
/// 标识宿主工作区中的一种单例 Tool。
/// </summary>
[JsonConverter(typeof(ToolTypeIdSystemTextJsonConverter))]
public sealed record ToolTypeId
{
    /// <summary>使用经过规范校验的字符串创建 Tool 类型身份。</summary>
    /// <param name="value">插件命名空间内唯一的持久化标识。</param>
    /// <exception cref="ArgumentException">值不满足稳定标识规则。</exception>
    public ToolTypeId(string value) =>
        Value = StableIdentifierRules.Validate(value, nameof(value));

    /// <summary>获取布局、注册表和诊断使用的稳定字符串。</summary>
    public string Value { get; }

    /// <summary>获取该值是否满足当前规范格式。</summary>
    public bool IsCanonical => StableIdentifierRules.IsCanonical(Value);

    /// <summary>解析 Tool 类型身份。</summary>
    /// <param name="value">待解析字符串。</param>
    /// <returns>有效 Tool 身份。</returns>
    public static ToolTypeId Parse(string value) => new(value);

    /// <summary>尝试解析 Tool 类型身份。</summary>
    /// <param name="value">待解析字符串。</param>
    /// <param name="toolTypeId">成功时为有效身份，否则为 <see langword="null"/>。</param>
    /// <returns>解析是否成功。</returns>
    public static bool TryParse(string? value, out ToolTypeId? toolTypeId)
    {
        toolTypeId = StableIdentifierRules.TryValidate(value, out var validated)
            ? new ToolTypeId(validated)
            : null;
        return toolTypeId is not null;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// 将 Tool 强类型身份适配为布局 JSON 中原有的字符串标量。
/// </summary>
public sealed class ToolTypeIdSystemTextJsonConverter : JsonConverter<ToolTypeId>
{
    /// <inheritdoc />
    public override ToolTypeId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { } value)
        {
            throw new JsonException("ToolTypeId 必须是非空字符串。");
        }

        try
        {
            return ToolTypeId.Parse(value);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("ToolTypeId 格式非法。", exception);
        }
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        ToolTypeId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
