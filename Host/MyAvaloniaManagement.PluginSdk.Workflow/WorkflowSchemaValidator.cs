using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginSdk.Workflow;

/// <summary>校验冻结 Workflow Schema、Action Descriptor 与 JSON 实例。</summary>
/// <remarks>
/// 所有入口都返回结构化问题，不把 JsonException、OverflowException 或输入正文传播到调用层。
/// Host 可以把问题映射为稳定失败码，Studio 则可以直接把字段路径展示给用户。
/// </remarks>
public sealed partial class WorkflowSchemaValidator
{
    private static readonly IReadOnlySet<string> CommonKeywords = new HashSet<string>(
        ["type", "description", "enum"], StringComparer.Ordinal);

    /// <summary>校验 Descriptor 的输入输出 Schema、敏感指针与确认策略。</summary>
    /// <param name="descriptor">插件注册的 Action Descriptor。</param>
    /// <returns>不包含原始异常或输入正文的结构化结果。</returns>
    public WorkflowSchemaValidationResult ValidateDescriptor(WorkflowActionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var issues = new List<WorkflowSchemaIssue>();
        Append(issues, ValidateSchema(descriptor.InputSchema), "$.inputSchema");
        Append(issues, ValidateSchema(descriptor.OutputSchema), "$.outputSchema");
        ValidateSensitivePointers(descriptor.InputSchema, descriptor.SensitiveInputPointers, issues);
        ValidateConfirmation(descriptor.Risks, descriptor.ConfirmationPolicy, issues);
        if (descriptor.DisplayName.Length > 128 || descriptor.Description.Length > 512)
        {
            issues.Add(new("descriptor.presentation", "$", "Action 名称或说明超过目录预算。"));
        }
        return new WorkflowSchemaValidationResult(issues);
    }

    /// <summary>校验一个 Schema 是否属于冻结的 Workflow Schema Profile。</summary>
    /// <param name="schema">待检查的 JSON Schema。</param>
    /// <returns>包含稳定代码和 JSON 路径的问题快照。</returns>
    public WorkflowSchemaValidationResult ValidateSchema(JsonElement schema)
    {
        var issues = new List<WorkflowSchemaIssue>();
        try
        {
            if (Encoding.UTF8.GetByteCount(schema.GetRawText()) > WorkflowSchemaProfile.MaximumSchemaBytes)
            {
                issues.Add(new("schema.budget", "$", "Schema 超过 64 KiB。"));
                return new(issues);
            }
            var propertyCount = 0;
            ValidateSchemaNode(schema, "$", 1, true, ref propertyCount, issues);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            issues.Add(new("schema.invalid", "$", "Schema 不符合冻结 Profile。"));
        }
        return new(issues);
    }

    /// <summary>以共享数值、Rune 与资源预算语义校验 JSON 实例。</summary>
    /// <param name="schema">已通过冻结 Profile 校验的 Schema。</param>
    /// <param name="instance">待校验的运行时或常量 JSON 值。</param>
    /// <param name="maximumBytes">本调用允许的实例 UTF-8 总字节数。</param>
    /// <param name="path">问题路径的起始位置。</param>
    /// <returns>不抛出数据相关异常的结构化校验结果。</returns>
    public WorkflowSchemaValidationResult ValidateInstance(
        JsonElement schema,
        JsonElement instance,
        int maximumBytes,
        string path = "$")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
        var issues = new List<WorkflowSchemaIssue>();
        try
        {
            if (Encoding.UTF8.GetByteCount(instance.GetRawText()) > maximumBytes)
            {
                issues.Add(new("instance.budget", path, "JSON 实例超过允许的总字节预算。"));
                return new(issues);
            }
            ValidateInstanceNode(schema, instance, path, 1, issues);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            issues.Add(new("instance.invalid", path, "JSON 实例无法按冻结 Schema 验证。"));
        }
        return new(issues);
    }

    private static void ValidateSchemaNode(
        JsonElement node,
        string path,
        int depth,
        bool isRoot,
        ref int propertyCount,
        IList<WorkflowSchemaIssue> issues)
    {
        if (depth > WorkflowSchemaProfile.MaximumDepth || node.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new("schema.node", path, "Schema 节点不是对象或超过最大深度。"));
            return;
        }
        if (!node.TryGetProperty("type", out var typeNode) || typeNode.ValueKind != JsonValueKind.String)
        {
            issues.Add(new("schema.type", path, "每个 Schema 节点必须包含单一字符串 type。"));
            return;
        }

        var type = typeNode.GetString()!;
        if (isRoot && type != "object")
        {
            issues.Add(new("schema.root", path, "根 Schema 必须为 object。"));
        }
        var allowed = new HashSet<string>(CommonKeywords, StringComparer.Ordinal);
        switch (type)
        {
            case "object":
                allowed.UnionWith(["properties", "required", "additionalProperties"]);
                ValidateObjectSchema(node, path, depth, ref propertyCount, issues);
                break;
            case "array":
                allowed.UnionWith(["items", "minItems", "maxItems"]);
                ValidateArraySchema(node, path, depth, ref propertyCount, issues);
                break;
            case "string":
                allowed.UnionWith(["minLength", "maxLength"]);
                ValidateIntegerBounds(node, path, "minLength", "maxLength", issues);
                break;
            case "integer":
            case "number":
                allowed.UnionWith(["minimum", "maximum"]);
                ValidateNumberBounds(node, path, issues);
                break;
            case "boolean":
            case "null":
                break;
            default:
                issues.Add(new("schema.type", path + ".type", "Schema type 不在冻结 Profile 中。"));
                break;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                issues.Add(new("schema.duplicate", path + "." + property.Name, "Schema 包含重复关键字。"));
            }
            else if (!allowed.Contains(property.Name))
            {
                issues.Add(new("schema.keyword", path + "." + property.Name, "Schema 包含未知关键字。"));
            }
        }
        if (node.TryGetProperty("description", out var description) && description.ValueKind != JsonValueKind.String)
        {
            issues.Add(new("schema.description", path + ".description", "description 必须为字符串。"));
        }
        ValidateEnum(node, type, path, issues);
    }

    private static void ValidateObjectSchema(
        JsonElement node,
        string path,
        int depth,
        ref int propertyCount,
        IList<WorkflowSchemaIssue> issues)
    {
        if (!node.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False)
        {
            issues.Add(new("schema.object", path, "object 必须声明 properties 且 additionalProperties 必须为 false。"));
            return;
        }
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties.EnumerateObject())
        {
            if (!declared.Add(property.Name))
            {
                issues.Add(new("schema.property.duplicate", path + ".properties." + property.Name, "属性名称重复。"));
                continue;
            }
            if (++propertyCount > WorkflowSchemaProfile.MaximumProperties)
            {
                issues.Add(new("schema.properties.budget", path, "Schema 累计属性超过 128。"));
                return;
            }
            ValidateSchemaNode(property.Value, path + ".properties." + property.Name,
                depth + 1, false, ref propertyCount, issues);
        }
        if (!node.TryGetProperty("required", out var required))
        {
            return;
        }
        if (required.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new("schema.required", path + ".required", "required 必须为数组。"));
            return;
        }
        var requiredNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in required.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !requiredNames.Add(item.GetString()!) ||
                !declared.Contains(item.GetString()!))
            {
                issues.Add(new("schema.required", path + ".required", "required 必须唯一引用已声明属性。"));
            }
        }
    }

    private static void ValidateArraySchema(
        JsonElement node,
        string path,
        int depth,
        ref int propertyCount,
        IList<WorkflowSchemaIssue> issues)
    {
        if (!node.TryGetProperty("items", out var items) ||
            !node.TryGetProperty("maxItems", out var maximum) ||
            !maximum.TryGetInt32(out var maximumValue) ||
            maximumValue is < 0 or > WorkflowSchemaProfile.MaximumArrayItems)
        {
            issues.Add(new("schema.array", path, "array 必须声明 items 和不超过 1024 的 maxItems。"));
            return;
        }
        if (node.TryGetProperty("minItems", out var minimum) &&
            (!minimum.TryGetInt32(out var minimumValue) || minimumValue < 0 || minimumValue > maximumValue))
        {
            issues.Add(new("schema.array.bounds", path, "minItems 必须是 0 到 maxItems 之间的整数。"));
        }
        ValidateSchemaNode(items, path + ".items", depth + 1, false, ref propertyCount, issues);
    }

    private static void ValidateEnum(
        JsonElement node,
        string type,
        string path,
        IList<WorkflowSchemaIssue> issues)
    {
        if (!node.TryGetProperty("enum", out var values))
        {
            return;
        }
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0 || type is "object" or "array")
        {
            issues.Add(new("schema.enum", path + ".enum", "enum 只允许用于非空标量集合。"));
            return;
        }
        var serialized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.EnumerateArray())
        {
            if (!MatchesType(type, value) || !serialized.Add(value.GetRawText()))
            {
                issues.Add(new("schema.enum", path + ".enum", "enum 必须包含唯一且与 type 匹配的标量。"));
            }
        }
    }

    private static void ValidateIntegerBounds(
        JsonElement node,
        string path,
        string minimumName,
        string maximumName,
        IList<WorkflowSchemaIssue> issues)
    {
        var hasMinimum = node.TryGetProperty(minimumName, out var minimum);
        var hasMaximum = node.TryGetProperty(maximumName, out var maximum);
        if (hasMinimum && (!minimum.TryGetInt32(out var minimumValue) || minimumValue < 0) ||
            hasMaximum && (!maximum.TryGetInt32(out var maximumValue) || maximumValue < 0) ||
            hasMinimum && hasMaximum && minimum.GetInt32() > maximum.GetInt32())
        {
            issues.Add(new("schema.bounds", path, "字符串长度边界必须是有序非负整数。"));
        }
    }

    private static void ValidateNumberBounds(
        JsonElement node,
        string path,
        IList<WorkflowSchemaIssue> issues)
    {
        var hasMinimum = node.TryGetProperty("minimum", out var minimum);
        var hasMaximum = node.TryGetProperty("maximum", out var maximum);
        decimal minimumValue = 0;
        decimal maximumValue = 0;
        var validMinimum = !hasMinimum || minimum.TryGetDecimal(out minimumValue);
        var validMaximum = !hasMaximum || maximum.TryGetDecimal(out maximumValue);
        if (!validMinimum || !validMaximum ||
            hasMinimum && hasMaximum && minimumValue > maximumValue)
        {
            issues.Add(new("schema.number.bounds", path, "数值上下界必须是可表示为 decimal 的有序数字。"));
        }
    }

    private static void ValidateSensitivePointers(
        JsonElement inputSchema,
        IEnumerable<string> pointers,
        IList<WorkflowSchemaIssue> issues)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pointer in pointers)
        {
            if (!CanonicalPointer().IsMatch(pointer) || !unique.Add(pointer) ||
                !PointerTargetsDeclaredProperty(inputSchema, pointer))
            {
                issues.Add(new("descriptor.sensitive", "$.sensitiveInputPointers",
                    "敏感字段必须是唯一、规范且指向输入 Schema 已声明属性的绝对 JSON Pointer。"));
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
        WorkflowActionConfirmationPolicy policy,
        IList<WorkflowSchemaIssue> issues)
    {
        const WorkflowActionRiskFlags all = WorkflowActionRiskFlags.UsesNetwork |
                                            WorkflowActionRiskFlags.ReadsLocalFiles |
                                            WorkflowActionRiskFlags.WritesLocalFiles |
                                            WorkflowActionRiskFlags.DeletesLocalFiles |
                                            WorkflowActionRiskFlags.HandlesSecret |
                                            WorkflowActionRiskFlags.LongRunning;
        if ((risks & ~all) != 0 || !Enum.IsDefined(policy) ||
            risks != WorkflowActionRiskFlags.None && policy < WorkflowActionConfirmationPolicy.OncePerRun ||
            risks.HasFlag(WorkflowActionRiskFlags.DeletesLocalFiles) &&
            policy != WorkflowActionConfirmationPolicy.EveryInvocation)
        {
            issues.Add(new("descriptor.confirmation", "$.confirmationPolicy", "风险与最低确认频率不匹配。"));
        }
    }

    private static void ValidateInstanceNode(
        JsonElement schema,
        JsonElement instance,
        string path,
        int depth,
        IList<WorkflowSchemaIssue> issues)
    {
        if (depth > WorkflowSchemaProfile.MaximumDepth)
        {
            issues.Add(new("instance.depth", path, "JSON 实例超过最大深度。"));
            return;
        }
        var type = schema.GetProperty("type").GetString()!;
        if (!MatchesType(type, instance))
        {
            issues.Add(new("instance.type", path, $"JSON 实例类型不符合 Schema：{type}。"));
            return;
        }
        if (schema.TryGetProperty("enum", out var enumValues) &&
            !enumValues.EnumerateArray().Any(value => JsonElement.DeepEquals(value, instance)))
        {
            issues.Add(new("instance.enum", path, "JSON 实例不在 Schema enum 中。"));
        }
        switch (type)
        {
            case "object": ValidateObjectInstance(schema, instance, path, depth, issues); break;
            case "array": ValidateArrayInstance(schema, instance, path, depth, issues); break;
            case "string": ValidateStringInstance(schema, instance, path, issues); break;
            case "integer":
            case "number": ValidateNumberInstance(schema, instance, path, issues); break;
        }
    }

    private static void ValidateObjectInstance(
        JsonElement schema,
        JsonElement instance,
        string path,
        int depth,
        IList<WorkflowSchemaIssue> issues)
    {
        var properties = schema.GetProperty("properties");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in instance.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                issues.Add(new("instance.duplicate", path + "." + property.Name, "JSON 对象包含重复属性。"));
            }
            else if (!properties.TryGetProperty(property.Name, out var childSchema))
            {
                issues.Add(new("instance.additional", path + "." + property.Name, "JSON 对象包含未声明属性。"));
            }
            else
            {
                ValidateInstanceNode(childSchema, property.Value, path + "." + property.Name, depth + 1, issues);
            }
        }
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var item in required.EnumerateArray())
            {
                if (!seen.Contains(item.GetString()!))
                {
                    issues.Add(new("instance.required", path + "." + item.GetString(), "JSON 对象缺少 required 属性。"));
                }
            }
        }
    }

    private static void ValidateArrayInstance(
        JsonElement schema,
        JsonElement instance,
        string path,
        int depth,
        IList<WorkflowSchemaIssue> issues)
    {
        var length = instance.GetArrayLength();
        var maximum = schema.GetProperty("maxItems").GetInt32();
        var minimum = schema.TryGetProperty("minItems", out var min) ? min.GetInt32() : 0;
        if (length < minimum || length > maximum || length > WorkflowSchemaProfile.MaximumArrayItems)
        {
            issues.Add(new("instance.array.bounds", path, "JSON 数组项数不符合 Schema。"));
        }
        var itemSchema = schema.GetProperty("items");
        var index = 0;
        foreach (var item in instance.EnumerateArray())
        {
            ValidateInstanceNode(itemSchema, item, $"{path}[{index++}]", depth + 1, issues);
        }
    }

    private static void ValidateStringInstance(
        JsonElement schema,
        JsonElement instance,
        string path,
        IList<WorkflowSchemaIssue> issues)
    {
        var text = instance.GetString()!;
        if (Encoding.UTF8.GetByteCount(text) > WorkflowSchemaProfile.MaximumStringBytes)
        {
            issues.Add(new("instance.string.budget", path, "单字符串超过 64 KiB。"));
        }
        var length = text.EnumerateRunes().Count();
        if (schema.TryGetProperty("minLength", out var min) && length < min.GetInt32() ||
            schema.TryGetProperty("maxLength", out var max) && length > max.GetInt32())
        {
            issues.Add(new("instance.string.bounds", path, "字符串长度不符合 Schema。"));
        }
    }

    private static void ValidateNumberInstance(
        JsonElement schema,
        JsonElement instance,
        string path,
        IList<WorkflowSchemaIssue> issues)
    {
        if (!instance.TryGetDecimal(out var value))
        {
            issues.Add(new("instance.number.range", path, "数字不能表示为冻结 Profile 的 decimal。"));
            return;
        }
        if (schema.TryGetProperty("minimum", out var minimum) && value < minimum.GetDecimal() ||
            schema.TryGetProperty("maximum", out var maximum) && value > maximum.GetDecimal())
        {
            issues.Add(new("instance.number.bounds", path, "数字不符合 Schema 边界。"));
        }
    }

    internal static bool MatchesType(string type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false,
    };

    private static void Append(
        ICollection<WorkflowSchemaIssue> destination,
        WorkflowSchemaValidationResult source,
        string prefix)
    {
        foreach (var issue in source.Issues)
        {
            destination.Add(issue with { Path = prefix + issue.Path[1..] });
        }
    }

    [GeneratedRegex("^(?:/(?:[^~/]|~0|~1)*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalPointer();
}
