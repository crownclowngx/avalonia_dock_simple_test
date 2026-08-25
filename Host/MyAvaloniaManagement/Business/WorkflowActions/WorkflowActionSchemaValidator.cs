using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.WorkflowActions;

/// <summary>实现 Workflow Action G0 冻结的窄 JSON Schema Profile 与资源预算。</summary>
/// <remarks>
/// 本类型不是完整 JSON Schema 引擎，也不提供扩展关键字。注册阶段先验证 Schema 自身，
/// 调用阶段再使用同一份冻结 Schema 验证输入或输出实例；这种显式白名单比引入通用引擎更容易
/// 审阅，也保证插件不能借远程引用、组合 Schema 或未预算的递归结构扩大宿主攻击面。
/// </remarks>
internal static partial class WorkflowActionSchemaValidator
{
    internal const int MaximumSchemaBytes = 64 * 1024;
    internal const int MaximumInputBytes = 256 * 1024;
    internal const int MaximumOutputBytes = 1024 * 1024;
    internal const int MaximumDepth = 16;
    internal const int MaximumProperties = 128;
    internal const int MaximumArrayItems = 1024;
    internal const int MaximumStringBytes = 64 * 1024;

    private const WorkflowActionRiskFlags AllRisks =
        WorkflowActionRiskFlags.UsesNetwork |
        WorkflowActionRiskFlags.ReadsLocalFiles |
        WorkflowActionRiskFlags.WritesLocalFiles |
        WorkflowActionRiskFlags.DeletesLocalFiles |
        WorkflowActionRiskFlags.HandlesSecret |
        WorkflowActionRiskFlags.LongRunning;

    private static readonly IReadOnlySet<string> CommonKeywords = new HashSet<string>(
        ["type", "description", "enum"], StringComparer.Ordinal);

    /// <summary>完整校验一个插件声明的描述符，但不解析或创建 Handler。</summary>
    internal static void ValidateDescriptor(WorkflowActionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateSchema(descriptor.InputSchema);
        ValidateSchema(descriptor.OutputSchema);
        ValidateSensitivePointers(descriptor.InputSchema, descriptor.SensitiveInputPointers);
        ValidateConfirmation(descriptor.Risks, descriptor.ConfirmationPolicy);
        if (descriptor.DisplayName.Length > 128 || descriptor.Description.Length > 512)
        {
            throw new ArgumentException("Action 名称或说明超过 Host 目录预算。", nameof(descriptor));
        }
    }

    internal static void ValidateSchema(JsonElement schema)
    {
        if (Encoding.UTF8.GetByteCount(schema.GetRawText()) > MaximumSchemaBytes)
        {
            throw new ArgumentException("Schema 超过 64 KiB。", nameof(schema));
        }

        var propertyCount = 0;
        ValidateSchemaNode(schema, depth: 1, isRoot: true, ref propertyCount);
    }

    /// <summary>按冻结 Schema 和独立总字节预算验证一个 JSON 实例。</summary>
    internal static void ValidateInstance(
        JsonElement schema,
        JsonElement instance,
        int maximumBytes)
    {
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
        if (Encoding.UTF8.GetByteCount(instance.GetRawText()) > maximumBytes)
        {
            throw new ArgumentException("JSON 实例超过冻结总字节预算。", nameof(instance));
        }
        ValidateInstanceNode(schema, instance, depth: 1);
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
                ValidateIntegerBounds(node, "minLength", "maxLength");
                break;
            case "integer":
            case "number":
                allowed.UnionWith(["minimum", "maximum"]);
                ValidateNumberBounds(node);
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
            if (++propertyCount > MaximumProperties)
            {
                throw new ArgumentException("Schema 累计属性超过 128。", nameof(node));
            }
            ValidateSchemaNode(property.Value, depth + 1, isRoot: false, ref propertyCount);
        }

        if (!node.TryGetProperty("required", out var required))
        {
            return;
        }
        if (required.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("required 必须为数组。", nameof(node));
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in required.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                !names.Add(item.GetString()!) ||
                !properties.TryGetProperty(item.GetString()!, out _))
            {
                throw new ArgumentException("required 必须唯一引用已声明属性。", nameof(node));
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
             minimumValue < 0 || minimumValue > maximumValue))
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
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0 ||
            type is "object" or "array")
        {
            throw new ArgumentException("enum 只允许用于非空标量集合。", nameof(node));
        }
        var serialized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.EnumerateArray())
        {
            if (!MatchesType(type, value) || !serialized.Add(value.GetRawText()))
            {
                throw new ArgumentException("enum 必须包含唯一且与 type 匹配的标量。", nameof(node));
            }
        }
    }

    private static void ValidateIntegerBounds(JsonElement node, string minimumName, string maximumName)
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

    private static void ValidateSensitivePointers(
        JsonElement inputSchema,
        IEnumerable<string> pointers)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pointer in pointers)
        {
            if (!CanonicalPointer().IsMatch(pointer) || !unique.Add(pointer) ||
                !PointerTargetsDeclaredProperty(inputSchema, pointer))
            {
                throw new ArgumentException(
                    "敏感字段必须是唯一、规范且指向输入 Schema 已声明属性的绝对 JSON Pointer。",
                    nameof(pointers));
            }
        }
    }

    private static bool PointerTargetsDeclaredProperty(JsonElement schema, string pointer)
    {
        var current = schema;
        foreach (var encoded in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = encoded.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (!current.TryGetProperty("type", out var type) || type.GetString() != "object" ||
                !current.GetProperty("properties").TryGetProperty(name, out current))
            {
                return false;
            }
        }
        return true;
    }

    private static void ValidateConfirmation(
        WorkflowActionRiskFlags risks,
        WorkflowActionConfirmationPolicy policy)
    {
        if ((risks & ~AllRisks) != 0 || !Enum.IsDefined(policy) ||
            risks != WorkflowActionRiskFlags.None && policy < WorkflowActionConfirmationPolicy.OncePerRun ||
            risks.HasFlag(WorkflowActionRiskFlags.DeletesLocalFiles) &&
            policy != WorkflowActionConfirmationPolicy.EveryInvocation)
        {
            throw new ArgumentException("风险与最低确认频率不匹配。", nameof(policy));
        }
    }

    private static void ValidateInstanceNode(JsonElement schema, JsonElement instance, int depth)
    {
        if (depth > MaximumDepth)
        {
            throw new ArgumentException("JSON 实例超过最大深度。", nameof(instance));
        }
        var type = schema.GetProperty("type").GetString()!;
        if (!MatchesType(type, instance))
        {
            throw new ArgumentException($"JSON 实例类型不符合 Schema：{type}。", nameof(instance));
        }
        if (schema.TryGetProperty("enum", out var enumValues) &&
            !enumValues.EnumerateArray().Any(value => JsonElement.DeepEquals(value, instance)))
        {
            throw new ArgumentException("JSON 实例不在 Schema enum 中。", nameof(instance));
        }

        switch (type)
        {
            case "object":
                ValidateObjectInstance(schema, instance, depth);
                break;
            case "array":
                ValidateArrayInstance(schema, instance, depth);
                break;
            case "string":
                ValidateStringInstance(schema, instance);
                break;
            case "integer":
            case "number":
                ValidateNumberInstance(schema, instance);
                break;
        }
    }

    private static void ValidateObjectInstance(JsonElement schema, JsonElement instance, int depth)
    {
        var properties = schema.GetProperty("properties");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in instance.EnumerateObject())
        {
            if (!seen.Add(property.Name) || !properties.TryGetProperty(property.Name, out var childSchema))
            {
                throw new ArgumentException("JSON 对象包含重复或未声明属性。", nameof(instance));
            }
            ValidateInstanceNode(childSchema, property.Value, depth + 1);
        }
        if (schema.TryGetProperty("required", out var required) &&
            required.EnumerateArray().Any(item => !seen.Contains(item.GetString()!)))
        {
            throw new ArgumentException("JSON 对象缺少 required 属性。", nameof(instance));
        }
    }

    private static void ValidateArrayInstance(JsonElement schema, JsonElement instance, int depth)
    {
        var length = instance.GetArrayLength();
        var maximum = schema.GetProperty("maxItems").GetInt32();
        var minimum = schema.TryGetProperty("minItems", out var min) ? min.GetInt32() : 0;
        if (length < minimum || length > maximum || length > MaximumArrayItems)
        {
            throw new ArgumentException("JSON 数组项数不符合 Schema。", nameof(instance));
        }
        var itemSchema = schema.GetProperty("items");
        foreach (var item in instance.EnumerateArray())
        {
            ValidateInstanceNode(itemSchema, item, depth + 1);
        }
    }

    private static void ValidateStringInstance(JsonElement schema, JsonElement instance)
    {
        var text = instance.GetString()!;
        if (Encoding.UTF8.GetByteCount(text) > MaximumStringBytes)
        {
            throw new ArgumentException("单字符串超过 64 KiB。", nameof(instance));
        }
        var length = text.EnumerateRunes().Count();
        if (schema.TryGetProperty("minLength", out var min) && length < min.GetInt32() ||
            schema.TryGetProperty("maxLength", out var max) && length > max.GetInt32())
        {
            throw new ArgumentException("字符串长度不符合 Schema。", nameof(instance));
        }
    }

    private static void ValidateNumberInstance(JsonElement schema, JsonElement instance)
    {
        var value = instance.GetDouble();
        if (schema.TryGetProperty("minimum", out var minimum) && value < minimum.GetDouble() ||
            schema.TryGetProperty("maximum", out var maximum) && value > maximum.GetDouble())
        {
            throw new ArgumentException("数字不符合 Schema 边界。", nameof(instance));
        }
    }

    private static bool MatchesType(string type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false,
    };

    [GeneratedRegex("^(?:/(?:[^~/]|~0|~1)*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalPointer();
}
