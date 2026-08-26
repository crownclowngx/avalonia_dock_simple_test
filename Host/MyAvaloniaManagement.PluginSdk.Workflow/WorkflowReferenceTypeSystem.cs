using System.Text.Json;

namespace MyAvaloniaManagement.PluginSdk.Workflow;

/// <summary>判断一个引用来源 Schema 的全部合法值能否安全赋给目标参数 Schema。</summary>
/// <remarks>
/// 这里采用保守的值域包含关系，而不是只比较 type 字符串。无法证明安全时返回结构化问题，
/// 从而把 enum、required、范围或数组边界冲突留在运行前，而不是依赖 Host 最终拒绝。
/// </remarks>
public sealed class WorkflowReferenceTypeSystem
{
    private readonly WorkflowSchemaValidator _validator = new();

    /// <summary>验证来源 Schema 的全部合法值是否都属于目标 Schema 的值域。</summary>
    /// <param name="sourceSchema">引用解析后得到的来源 Schema。</param>
    /// <param name="targetSchema">参数位置要求的目标 Schema。</param>
    /// <param name="path">写入问题结果的逻辑参数路径。</param>
    /// <returns>不可证明安全时包含保守拒绝原因的只读结果。</returns>
    public WorkflowSchemaValidationResult ValidateAssignable(
        JsonElement sourceSchema,
        JsonElement targetSchema,
        string path = "$")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var issues = new List<WorkflowSchemaIssue>();
        ValidateNode(sourceSchema, targetSchema, path, issues);
        return new(issues);
    }

    private void ValidateNode(
        JsonElement source,
        JsonElement target,
        string path,
        IList<WorkflowSchemaIssue> issues)
    {
        if (source.TryGetProperty("enum", out var sourceEnum))
        {
            foreach (var value in sourceEnum.EnumerateArray())
            {
                var result = _validator.ValidateInstance(
                    target, value, WorkflowSchemaProfile.MaximumSchemaBytes, path);
                if (!result.IsValid)
                {
                    issues.Add(new("reference.enum", path, "来源 enum 包含目标 Schema 不接受的值。"));
                    return;
                }
            }
            return;
        }

        var sourceType = source.GetProperty("type").GetString()!;
        var targetType = target.GetProperty("type").GetString()!;
        if (sourceType != targetType && !(sourceType == "integer" && targetType == "number"))
        {
            issues.Add(new("reference.type", path, $"来源类型 {sourceType} 不能赋给目标类型 {targetType}。"));
            return;
        }
        if (target.TryGetProperty("enum", out _))
        {
            issues.Add(new("reference.enum", path, "无限来源值域不能保证落在目标 enum 中。"));
            return;
        }

        switch (sourceType)
        {
            case "object": ValidateObject(source, target, path, issues); break;
            case "array": ValidateArray(source, target, path, issues); break;
            case "string": ValidateString(source, target, path, issues); break;
            case "integer":
            case "number": ValidateNumber(source, target, path, issues); break;
        }
    }

    private void ValidateObject(
        JsonElement source,
        JsonElement target,
        string path,
        IList<WorkflowSchemaIssue> issues)
    {
        if (target.GetProperty("type").GetString() != "object")
        {
            issues.Add(new("reference.type", path, "对象来源不能赋给非对象目标。"));
            return;
        }
        var sourceProperties = source.GetProperty("properties");
        var targetProperties = target.GetProperty("properties");
        var sourceRequired = Required(source);
        var targetRequired = Required(target);
        foreach (var required in targetRequired)
        {
            if (!sourceRequired.Contains(required) || !sourceProperties.TryGetProperty(required, out _))
            {
                issues.Add(new("reference.required", path + "." + required,
                    "目标 required 字段未由来源 Schema 保证存在。"));
            }
        }
        foreach (var property in sourceProperties.EnumerateObject())
        {
            if (!targetProperties.TryGetProperty(property.Name, out var targetProperty))
            {
                issues.Add(new("reference.additional", path + "." + property.Name,
                    "来源对象可能产生目标 Schema 不允许的字段。"));
                continue;
            }
            ValidateNode(property.Value, targetProperty, path + "." + property.Name, issues);
        }
    }

    private void ValidateArray(
        JsonElement source,
        JsonElement target,
        string path,
        IList<WorkflowSchemaIssue> issues)
    {
        if (target.GetProperty("type").GetString() != "array")
        {
            issues.Add(new("reference.type", path, "数组来源不能赋给非数组目标。"));
            return;
        }
        var sourceMinimum = source.TryGetProperty("minItems", out var sourceMin) ? sourceMin.GetInt32() : 0;
        var targetMinimum = target.TryGetProperty("minItems", out var targetMin) ? targetMin.GetInt32() : 0;
        var sourceMaximum = source.GetProperty("maxItems").GetInt32();
        var targetMaximum = target.GetProperty("maxItems").GetInt32();
        if (sourceMinimum < targetMinimum || sourceMaximum > targetMaximum)
        {
            issues.Add(new("reference.array.bounds", path, "来源数组长度值域超出目标 Schema。"));
        }
        ValidateNode(source.GetProperty("items"), target.GetProperty("items"), path + "[]", issues);
    }

    private static void ValidateString(
        JsonElement source,
        JsonElement target,
        string path,
        IList<WorkflowSchemaIssue> issues)
    {
        var sourceMinimum = source.TryGetProperty("minLength", out var sourceMin) ? sourceMin.GetInt32() : 0;
        var targetMinimum = target.TryGetProperty("minLength", out var targetMin) ? targetMin.GetInt32() : 0;
        var sourceMaximum = source.TryGetProperty("maxLength", out var sourceMax) ? sourceMax.GetInt32() : (int?)null;
        var targetMaximum = target.TryGetProperty("maxLength", out var targetMax) ? targetMax.GetInt32() : (int?)null;
        if (sourceMinimum < targetMinimum ||
            targetMaximum is not null && (sourceMaximum is null || sourceMaximum > targetMaximum))
        {
            issues.Add(new("reference.string.bounds", path, "来源字符串长度值域超出目标 Schema。"));
        }
    }

    private static void ValidateNumber(
        JsonElement source,
        JsonElement target,
        string path,
        IList<WorkflowSchemaIssue> issues)
    {
        var sourceMinimum = source.TryGetProperty("minimum", out var sourceMin)
            ? sourceMin.GetDecimal()
            : (decimal?)null;
        var targetMinimum = target.TryGetProperty("minimum", out var targetMin)
            ? targetMin.GetDecimal()
            : (decimal?)null;
        var sourceMaximum = source.TryGetProperty("maximum", out var sourceMax)
            ? sourceMax.GetDecimal()
            : (decimal?)null;
        var targetMaximum = target.TryGetProperty("maximum", out var targetMax)
            ? targetMax.GetDecimal()
            : (decimal?)null;
        if (targetMinimum is not null && (sourceMinimum is null || sourceMinimum < targetMinimum) ||
            targetMaximum is not null && (sourceMaximum is null || sourceMaximum > targetMaximum))
        {
            issues.Add(new("reference.number.bounds", path, "来源数值范围超出目标 Schema。"));
        }
    }

    private static HashSet<string> Required(JsonElement schema) =>
        schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];
}
