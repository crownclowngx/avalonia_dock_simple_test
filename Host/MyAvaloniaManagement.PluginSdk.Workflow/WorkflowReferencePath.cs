using System.Globalization;
using System.Text.Json;

namespace MyAvaloniaManagement.PluginSdk.Workflow;

/// <summary>以完全相同的 segment 规则解析 Schema 路径和运行时 JSON 路径。</summary>
/// <remarks>
/// 对象属性的“存在”不足以证明运行时一定可取：静态路径还要求属性出现在 required 中。
/// 数组路径只接受非负十进制索引；静态解析进一步要求 minItems 足以保证该索引存在。
/// </remarks>
public static class WorkflowReferencePath
{
    /// <summary>按 required 与 minItems 证明规则解析静态 Schema 路径。</summary>
    /// <param name="schema">已通过冻结 Profile 校验的根 Schema。</param>
    /// <param name="segments">未转义的对象属性名或数组索引段。</param>
    /// <returns>成功时包含目标子 Schema；失败时包含稳定原因与段位置。</returns>
    public static WorkflowReferencePathResult ResolveGuaranteedSchemaPath(
        JsonElement schema,
        IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var current = schema;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var type = current.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (type == "object")
            {
                if (!current.TryGetProperty("properties", out var properties) ||
                    !properties.TryGetProperty(segment, out var propertySchema))
                {
                    return Failure(WorkflowReferencePathFailure.MissingProperty, index);
                }
                if (!IsRequired(current, segment))
                {
                    return Failure(WorkflowReferencePathFailure.OptionalProperty, index);
                }
                current = propertySchema;
                continue;
            }
            if (type == "array")
            {
                if (!TryParseIndex(segment, out var arrayIndex))
                {
                    return Failure(WorkflowReferencePathFailure.InvalidArrayIndex, index);
                }
                var minimum = current.TryGetProperty("minItems", out var min) ? min.GetInt32() : 0;
                if (arrayIndex >= minimum)
                {
                    return Failure(WorkflowReferencePathFailure.ArrayIndexNotGuaranteed, index);
                }
                if (!current.TryGetProperty("items", out current))
                {
                    return Failure(WorkflowReferencePathFailure.NonContainer, index);
                }
                continue;
            }
            return Failure(WorkflowReferencePathFailure.NonContainer, index);
        }
        return new WorkflowReferencePathResult(current, WorkflowReferencePathFailure.None, -1);
    }

    /// <summary>按静态解析器相同的对象与数组 segment 语法解析运行时 JSON。</summary>
    /// <param name="value">运行时根 JSON 值。</param>
    /// <param name="segments">未转义的对象属性名或数组索引段。</param>
    /// <returns>成功时包含目标值快照；失败时不泄漏输入正文。</returns>
    public static WorkflowReferencePathResult ResolveValuePath(
        JsonElement value,
        IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var current = value;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(segment, out current))
                {
                    return Failure(WorkflowReferencePathFailure.MissingProperty, index);
                }
                continue;
            }
            if (current.ValueKind == JsonValueKind.Array)
            {
                if (!TryParseIndex(segment, out var arrayIndex))
                {
                    return Failure(WorkflowReferencePathFailure.InvalidArrayIndex, index);
                }
                if (arrayIndex >= current.GetArrayLength())
                {
                    return Failure(WorkflowReferencePathFailure.ArrayIndexOutOfRange, index);
                }
                current = current[arrayIndex];
                continue;
            }
            return Failure(WorkflowReferencePathFailure.NonContainer, index);
        }
        return new WorkflowReferencePathResult(current, WorkflowReferencePathFailure.None, -1);
    }

    private static bool IsRequired(JsonElement schema, string name) =>
        schema.TryGetProperty("required", out var required) &&
        required.EnumerateArray().Any(item =>
            string.Equals(item.GetString(), name, StringComparison.Ordinal));

    private static bool TryParseIndex(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;

    private static WorkflowReferencePathResult Failure(
        WorkflowReferencePathFailure failure,
        int index) => new(null, failure, index);
}
