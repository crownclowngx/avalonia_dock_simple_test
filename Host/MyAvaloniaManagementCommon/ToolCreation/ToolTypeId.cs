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
    public ToolTypeId(string value) =>
        Value = StableIdentifierRules.Validate(value, nameof(value));

    public string Value { get; }

    public bool IsCanonical => StableIdentifierRules.IsCanonical(Value);

    public static ToolTypeId Parse(string value) => new(value);

    public static bool TryParse(string? value, out ToolTypeId? toolTypeId)
    {
        toolTypeId = StableIdentifierRules.TryValidate(value, out var validated)
            ? new ToolTypeId(validated)
            : null;
        return toolTypeId is not null;
    }

    public override string ToString() => Value;
}

/// <summary>
/// 将 Tool 强类型身份适配为布局 JSON 中原有的字符串标量。
/// </summary>
public sealed class ToolTypeIdSystemTextJsonConverter : JsonConverter<ToolTypeId>
{
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

    public override void Write(
        Utf8JsonWriter writer,
        ToolTypeId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
