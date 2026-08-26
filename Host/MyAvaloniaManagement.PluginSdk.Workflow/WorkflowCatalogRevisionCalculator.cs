using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginSdk.Workflow;

/// <summary>为 Action 目录分别计算执行契约修订和展示修订。</summary>
/// <remarks>
/// 契约哈希忽略 description，并规范化 JSON 对象以及 required/enum 的无序集合；展示哈希只跟踪
/// 名称、说明和 Schema description。这样文案变化可提示用户刷新，却不会让可执行定义失效。
/// </remarks>
public static class WorkflowCatalogRevisionCalculator
{
    /// <summary>计算给定 Action 快照的契约修订与展示修订。</summary>
    /// <param name="actions">待规范化的 Action Descriptor 快照。</param>
    /// <returns>使用小写十六进制 SHA-256 表示的两类修订。</returns>
    public static WorkflowCatalogRevisions Calculate(IReadOnlyList<WorkflowActionDescriptor> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var ordered = actions.OrderBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
        return new(Hash(BuildContract(ordered)), Hash(BuildPresentation(ordered)));
    }

    private static byte[] BuildContract(IReadOnlyList<WorkflowActionDescriptor> actions)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var action in actions)
            {
                writer.WriteStartObject();
                writer.WriteString("id", action.Id.Value);
                writer.WriteNumber("risks", (int)action.Risks);
                writer.WriteNumber("confirmationPolicy", (int)action.ConfirmationPolicy);
                writer.WritePropertyName("sensitiveInputPointers");
                writer.WriteStartArray();
                foreach (var pointer in action.SensitiveInputPointers.Order(StringComparer.Ordinal))
                {
                    writer.WriteStringValue(pointer);
                }
                writer.WriteEndArray();
                writer.WritePropertyName("inputSchema");
                WriteContractSchema(writer, action.InputSchema);
                writer.WritePropertyName("outputSchema");
                WriteContractSchema(writer, action.OutputSchema);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return stream.ToArray();
    }

    private static byte[] BuildPresentation(IReadOnlyList<WorkflowActionDescriptor> actions)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var action in actions)
            {
                writer.WriteStartObject();
                writer.WriteString("id", action.Id.Value);
                writer.WriteString("displayName", action.DisplayName);
                writer.WriteString("description", action.Description);
                writer.WritePropertyName("schemaDescriptions");
                writer.WriteStartObject();
                WriteDescriptions(writer, action.InputSchema, "input");
                WriteDescriptions(writer, action.OutputSchema, "output");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return stream.ToArray();
    }

    private static void WriteContractSchema(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                             .Where(item => item.Name != "description")
                             .OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    if (property.Name is "required" or "enum")
                    {
                        WriteSortedArray(writer, property.Value);
                    }
                    else
                    {
                        WriteContractSchema(writer, property.Value);
                    }
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteContractSchema(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetDecimal().ToString("G29", CultureInfo.InvariantCulture));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void WriteSortedArray(Utf8JsonWriter writer, JsonElement array)
    {
        var values = array.EnumerateArray()
            .Select(item => (Key: CanonicalScalar(item), Value: item.Clone()))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        writer.WriteStartArray();
        foreach (var item in values)
        {
            WriteContractSchema(writer, item.Value);
        }
        writer.WriteEndArray();
    }

    private static string CanonicalScalar(JsonElement element) => element.ValueKind == JsonValueKind.Number
        ? element.GetDecimal().ToString("G29", CultureInfo.InvariantCulture)
        : element.GetRawText();

    private static void WriteDescriptions(Utf8JsonWriter writer, JsonElement schema, string path)
    {
        if (schema.TryGetProperty("description", out var description))
        {
            writer.WriteString(path, description.GetString());
        }
        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                WriteDescriptions(writer, property.Value, path + "/" + Escape(property.Name));
            }
        }
        if (schema.TryGetProperty("items", out var items))
        {
            WriteDescriptions(writer, items, path + "/*");
        }
    }

    private static string Escape(string text) => text.Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    private static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
