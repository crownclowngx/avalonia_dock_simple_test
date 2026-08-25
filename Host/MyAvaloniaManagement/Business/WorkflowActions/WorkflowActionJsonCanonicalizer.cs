using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MyAvaloniaManagement.Business.WorkflowActions;

/// <summary>为目录 revision 和运行内参数指纹提供同一份确定性 JSON 写法。</summary>
/// <remarks>
/// 对象属性按 Ordinal 排序，数组保持业务顺序，标量交给 System.Text.Json 写入。该工具只用于内存哈希，
/// 不保存参数正文；集中实现可避免 Catalog 与 Run 对“同一 JSON”的判断逐渐产生差异。
/// </remarks>
internal static class WorkflowActionJsonCanonicalizer
{
    internal static byte[] GetUtf8Bytes(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, element);
        }
        return stream.ToArray();
    }

    internal static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        ArgumentNullException.ThrowIfNull(writer);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(
                             property => property.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
