using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>G0 测试专用的窄 Schema Profile 参考验证器。</summary>
/// <remarks>
/// 本类型不进入 Host 生产代码。它把 G0 已冻结的关键字、预算和确认规则变成可执行样例，
/// 让 G1 实现可以复用测试语料而不必复用测试算法。
/// </remarks>
internal static partial class WorkflowActionG0SchemaProfile
{
    internal const int MaximumSchemaBytes = 64 * 1024;
    internal const int MaximumInputBytes = 256 * 1024;
    internal const int MaximumOutputBytes = 1024 * 1024;
    internal const int MaximumDepth = 16;
    internal const int MaximumProperties = 128;
    internal const int MaximumArrayItems = 1024;
    internal const int MaximumStringBytes = 64 * 1024;

    private static readonly IReadOnlySet<string> CommonKeywords = new HashSet<string>(
        ["type", "description", "enum"], StringComparer.Ordinal);

    internal static void ValidateSchema(JsonElement schema)
    {
        if (Encoding.UTF8.GetByteCount(schema.GetRawText()) > MaximumSchemaBytes)
        {
            throw new ArgumentException("Schema 超过 64 KiB。", nameof(schema));
        }

        var propertyCount = 0;
        ValidateSchemaNode(schema, depth: 1, isRoot: true, ref propertyCount);
    }

    internal static void ValidateInstance(JsonElement instance, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(instance.GetRawText()) > maximumBytes)
        {
            throw new ArgumentException("JSON 实例超过冻结总字节预算。", nameof(instance));
        }

        ValidateInstanceNode(instance, depth: 1);
    }

    internal static void ValidateSensitivePointers(IEnumerable<string> pointers)
    {
        foreach (var pointer in pointers)
        {
            if (!CanonicalPointer().IsMatch(pointer))
            {
                throw new ArgumentException("敏感字段必须使用规范绝对 JSON Pointer。", nameof(pointers));
            }
        }
    }

    internal static void ValidateConfirmation(
        WorkflowActionRiskFlags risks,
        WorkflowActionConfirmationPolicy policy)
    {
        const WorkflowActionRiskFlags all = WorkflowActionRiskFlags.UsesNetwork |
            WorkflowActionRiskFlags.ReadsLocalFiles |
            WorkflowActionRiskFlags.WritesLocalFiles |
            WorkflowActionRiskFlags.DeletesLocalFiles |
            WorkflowActionRiskFlags.HandlesSecret |
            WorkflowActionRiskFlags.LongRunning;
        if ((risks & ~all) != 0)
        {
            throw new ArgumentException("风险组合包含未冻结的位。", nameof(risks));
        }

        if (!Enum.IsDefined(policy) ||
            risks != WorkflowActionRiskFlags.None &&
            policy < WorkflowActionConfirmationPolicy.OncePerRun ||
            risks.HasFlag(WorkflowActionRiskFlags.DeletesLocalFiles) &&
            policy != WorkflowActionConfirmationPolicy.EveryInvocation)
        {
            throw new ArgumentException("风险与最低确认频率不匹配。", nameof(policy));
        }
    }

    private static void ValidateSchemaNode(
        JsonElement node,
        int depth,
        bool isRoot,
        ref int propertyCount)
    {
        if (depth > MaximumDepth || node.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Schema 节点不是对象或超过最大深度。", nameof(node));
        }

        if (!node.TryGetProperty("type", out var typeNode) ||
            typeNode.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("每个 Schema 节点必须包含单一字符串 type。", nameof(node));
        }

        var type = typeNode.GetString()!;
        if (isRoot && type != "object")
        {
            throw new ArgumentException("根 Schema 必须为 object。", nameof(node));
        }

        var allowed = new HashSet<string>(CommonKeywords, StringComparer.Ordinal);
        switch (type)
        {
            case "object":
                allowed.UnionWith(["properties", "required", "additionalProperties"]);
                ValidateObjectSchema(node, depth, ref propertyCount);
                break;
            case "array":
                allowed.UnionWith(["items", "minItems", "maxItems"]);
                ValidateArraySchema(node, depth, ref propertyCount);
                break;
            case "string":
                allowed.UnionWith(["minLength", "maxLength"]);
                break;
            case "integer":
            case "number":
                allowed.UnionWith(["minimum", "maximum"]);
                break;
            case "boolean":
            case "null":
                break;
            default:
                throw new ArgumentException("Schema type 不在冻结 Profile 中。", nameof(node));
        }

        foreach (var property in node.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new ArgumentException($"Schema 包含未知关键字 {property.Name}。", nameof(node));
            }
        }

        if (node.TryGetProperty("description", out var description) &&
            description.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("description 必须为字符串。", nameof(node));
        }
        ValidateEnum(node, type);
        if (type == "string")
        {
            ValidateIntegerBounds(node, "minLength", "maxLength");
        }
        else if (type is "integer" or "number")
        {
            ValidateNumberBounds(node);
        }
    }

    private static void ValidateObjectSchema(JsonElement node, int depth, ref int propertyCount)
    {
        if (!node.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("additionalProperties", out var additional) ||
            additional.ValueKind != JsonValueKind.False)
        {
            throw new ArgumentException(
                "object 必须声明 properties 且 additionalProperties 必须为 false。",
                nameof(node));
        }

        foreach (var property in properties.EnumerateObject())
        {
            propertyCount++;
            if (propertyCount > MaximumProperties)
            {
                throw new ArgumentException("Schema 累计属性超过 128。", nameof(node));
            }
            ValidateSchemaNode(property.Value, depth + 1, isRoot: false, ref propertyCount);
        }

        if (node.TryGetProperty("required", out var required))
        {
            if (required.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("required 必须为数组。", nameof(node));
            }
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String ||
                    !properties.TryGetProperty(item.GetString()!, out _))
                {
                    throw new ArgumentException("required 必须引用已声明属性。", nameof(node));
                }
            }
        }
    }

    private static void ValidateArraySchema(JsonElement node, int depth, ref int propertyCount)
    {
        if (!node.TryGetProperty("items", out var items) ||
            !node.TryGetProperty("maxItems", out var maximum) ||
            !maximum.TryGetInt32(out var maximumValue) ||
            maximumValue is < 0 or > MaximumArrayItems)
        {
            throw new ArgumentException("array 必须声明 items 和不超过 1024 的 maxItems。", nameof(node));
        }
        if (node.TryGetProperty("minItems", out var minimum) &&
            (!minimum.TryGetInt32(out var minimumValue) ||
             minimumValue < 0 ||
             minimumValue > maximumValue))
        {
            throw new ArgumentException("minItems 必须是 0 到 maxItems 之间的整数。", nameof(node));
        }
        ValidateSchemaNode(items, depth + 1, isRoot: false, ref propertyCount);
    }

    private static void ValidateEnum(JsonElement node, string type)
    {
        if (!node.TryGetProperty("enum", out var values))
        {
            return;
        }
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0)
        {
            throw new ArgumentException("enum 必须是非空标量数组。", nameof(node));
        }
        if (type is "object" or "array")
        {
            throw new ArgumentException("enum 只允许用于标量 type。", nameof(node));
        }

        foreach (var value in values.EnumerateArray())
        {
            var matchesType = type switch
            {
                "string" => value.ValueKind == JsonValueKind.String,
                "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
                "number" => value.ValueKind == JsonValueKind.Number,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "null" => value.ValueKind == JsonValueKind.Null,
                _ => value.ValueKind is JsonValueKind.String or JsonValueKind.Number or
                    JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null,
            };
            if (!matchesType)
            {
                throw new ArgumentException("enum 只能包含与 type 匹配的标量。", nameof(node));
            }
        }
    }

    private static void ValidateIntegerBounds(
        JsonElement node,
        string minimumName,
        string maximumName)
    {
        var hasMinimum = node.TryGetProperty(minimumName, out var minimum);
        var hasMaximum = node.TryGetProperty(maximumName, out var maximum);
        if (hasMinimum && (!minimum.TryGetInt32(out var minimumValue) || minimumValue < 0) ||
            hasMaximum && (!maximum.TryGetInt32(out var maximumValue) || maximumValue < 0) ||
            hasMinimum && hasMaximum && minimum.GetInt32() > maximum.GetInt32())
        {
            throw new ArgumentException("字符串长度边界必须是有序非负整数。", nameof(node));
        }
    }

    private static void ValidateNumberBounds(JsonElement node)
    {
        var hasMinimum = node.TryGetProperty("minimum", out var minimum);
        var hasMaximum = node.TryGetProperty("maximum", out var maximum);
        if (hasMinimum && minimum.ValueKind != JsonValueKind.Number ||
            hasMaximum && maximum.ValueKind != JsonValueKind.Number ||
            hasMinimum && hasMaximum && minimum.GetDouble() > maximum.GetDouble())
        {
            throw new ArgumentException("数值上下界必须是有序数字。", nameof(node));
        }
    }

    private static void ValidateInstanceNode(JsonElement node, int depth)
    {
        if (depth > MaximumDepth)
        {
            throw new ArgumentException("JSON 实例超过最大深度。", nameof(node));
        }

        if (node.ValueKind == JsonValueKind.String &&
            Encoding.UTF8.GetByteCount(node.GetString()!) > MaximumStringBytes)
        {
            throw new ArgumentException("单字符串超过 64 KiB。", nameof(node));
        }

        if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > MaximumArrayItems)
        {
            throw new ArgumentException("数组超过 1024 项。", nameof(node));
        }

        foreach (var child in node.ValueKind switch
                 {
                     JsonValueKind.Object => node.EnumerateObject().Select(item => item.Value),
                     JsonValueKind.Array => node.EnumerateArray(),
                     _ => [],
                 })
        {
            ValidateInstanceNode(child, depth + 1);
        }
    }

    [GeneratedRegex("^(?:/(?:[^~/]|~0|~1)*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalPointer();
}
