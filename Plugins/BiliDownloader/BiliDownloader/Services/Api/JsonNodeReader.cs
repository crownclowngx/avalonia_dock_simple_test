using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BiliDownloader.Services.Api;

/// <summary>
/// Bilibili 远端响应的最小 System.Text.Json 读取帮助类。
/// </summary>
/// <remarks>
/// 站点接口存在“数字有时以字符串返回”的历史差异，因此这里集中保留原有宽容读取语义；
/// 业务适配器仍需显式选择字段和默认值，不把动态 JSON 对象泄漏到领域模型。
/// </remarks>
internal static class JsonNodeReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 64,
    };

    internal static JsonObject ParseObject(string json)
    {
        return JsonNode.Parse(
                   json,
                   nodeOptions: null,
                   documentOptions: new JsonDocumentOptions { MaxDepth = 64 })
               as JsonObject
               ?? throw new JsonException("远端响应根节点不是 JSON 对象。");
    }

    internal static T? Value<T>(this JsonNode? node)
    {
        if (node is null) return default;
        try
        {
            return node.Deserialize<T>(SerializerOptions);
        }
        catch (JsonException) when (node is JsonValue)
        {
            // 旧解析器对数字字符串较宽容；这些分支保持既有远端兼容行为。
            var text = node.ToString();
            object? converted = typeof(T) switch
            {
                var type when type == typeof(int) || type == typeof(int?) =>
                    int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null,
                var type when type == typeof(long) || type == typeof(long?) =>
                    long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null,
                var type when type == typeof(double) || type == typeof(double?) =>
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null,
                var type when type == typeof(bool) || type == typeof(bool?) =>
                    bool.TryParse(text, out var value) ? value : null,
                var type when type == typeof(string) => text,
                _ => null,
            };
            return converted is null ? default : (T)converted;
        }
    }
}
