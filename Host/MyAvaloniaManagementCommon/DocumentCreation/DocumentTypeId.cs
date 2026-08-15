using MyAvaloniaManagementCommon.Identity;
using Newtonsoft.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAvaloniaManagementCommon.DocumentCreation;

/// <summary>
/// 标识一种可由宿主创建、保存和恢复的 Document 类型。
/// </summary>
/// <remarks>
/// JSON 转换器把值对象继续写成单个字符串，确保强类型改造不改变历史文档信封的线格式。
/// </remarks>
[Newtonsoft.Json.JsonConverter(typeof(DocumentTypeIdNewtonsoftJsonConverter))]
[System.Text.Json.Serialization.JsonConverter(typeof(DocumentTypeIdSystemTextJsonConverter))]
public sealed record DocumentTypeId
{
    /// <summary>使用经过规范校验的字符串创建 Document 类型身份。</summary>
    /// <param name="value">插件命名空间内唯一的持久化标识。</param>
    /// <exception cref="ArgumentException">值不满足稳定标识规则。</exception>
    public DocumentTypeId(string value) =>
        Value = StableIdentifierRules.Validate(value, nameof(value));

    /// <summary>获取布局、文档信封和诊断使用的稳定字符串。</summary>
    public string Value { get; }

    /// <summary>获取该值是否满足当前规范格式。</summary>
    public bool IsCanonical => StableIdentifierRules.IsCanonical(Value);

    /// <summary>解析 Document 类型身份。</summary>
    /// <param name="value">待解析字符串。</param>
    /// <returns>有效身份。</returns>
    public static DocumentTypeId Parse(string value) => new(value);

    /// <summary>尝试解析 Document 类型身份。</summary>
    /// <param name="value">待解析字符串。</param>
    /// <param name="documentTypeId">成功时为有效身份，否则为 <see langword="null"/>。</param>
    /// <returns>解析是否成功。</returns>
    public static bool TryParse(string? value, out DocumentTypeId? documentTypeId)
    {
        documentTypeId = StableIdentifierRules.TryValidate(value, out var validated)
            ? new DocumentTypeId(validated)
            : null;
        return documentTypeId is not null;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// System.Text.Json 边界使用的字符串标量适配器。
/// </summary>
/// <remarks>
/// 设计意图：布局等宿主持久化设施采用 System.Text.Json；显式转换可防止值对象被默认写成
/// <c>{ "Value": "..." }</c>，从而让磁盘协议与 Newtonsoft.Json 文档信封保持一致。
/// </remarks>
public sealed class DocumentTypeIdSystemTextJsonConverter : System.Text.Json.Serialization.JsonConverter<DocumentTypeId>
{
    /// <inheritdoc />
    public override DocumentTypeId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { } value)
        {
            throw new System.Text.Json.JsonException("DocumentTypeId 必须是非空字符串。");
        }

        try
        {
            return DocumentTypeId.Parse(value);
        }
        catch (ArgumentException exception)
        {
            throw new System.Text.Json.JsonException("DocumentTypeId 格式非法。", exception);
        }
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        DocumentTypeId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>
/// Newtonsoft.Json 使用的字符串标量适配器。
/// </summary>
public sealed class DocumentTypeIdNewtonsoftJsonConverter : Newtonsoft.Json.JsonConverter<DocumentTypeId>
{
    /// <inheritdoc />
    public override void WriteJson(
        JsonWriter writer,
        DocumentTypeId? value,
        Newtonsoft.Json.JsonSerializer serializer) =>
        writer.WriteValue(value?.Value);

    /// <inheritdoc />
    public override DocumentTypeId? ReadJson(
        JsonReader reader,
        Type objectType,
        DocumentTypeId? existingValue,
        bool hasExistingValue,
        Newtonsoft.Json.JsonSerializer serializer)
    {
        if (reader.TokenType != JsonToken.String || reader.Value is not string value)
        {
            throw new JsonSerializationException("DocumentTypeId 必须是非空字符串。");
        }

        try
        {
            return DocumentTypeId.Parse(value);
        }
        catch (ArgumentException exception)
        {
            throw new JsonSerializationException("DocumentTypeId 格式非法。", exception);
        }
    }
}
