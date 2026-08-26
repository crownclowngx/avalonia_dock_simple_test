using System;
using System.Linq;
using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.Workflow;

namespace MyAvaloniaManagement.Business.WorkflowActions;

/// <summary>把共享 Workflow Schema 结果适配为 Host 既有的异常边界。</summary>
/// <remarks>
/// Schema 语义全部属于 PluginSdk.Workflow；Host 只负责在注册阶段拒绝非法 Descriptor，
/// 并在调用阶段让 Runtime 把异常映射为既有稳定失败码，避免再次维护一套校验算法。
/// </remarks>
internal static class WorkflowActionSchemaValidator
{
    internal const int MaximumSchemaBytes = WorkflowSchemaProfile.MaximumSchemaBytes;
    internal const int MaximumInputBytes = WorkflowSchemaProfile.MaximumInputBytes;
    internal const int MaximumOutputBytes = WorkflowSchemaProfile.MaximumOutputBytes;
    internal const int MaximumDepth = WorkflowSchemaProfile.MaximumDepth;
    internal const int MaximumProperties = WorkflowSchemaProfile.MaximumProperties;
    internal const int MaximumArrayItems = WorkflowSchemaProfile.MaximumArrayItems;
    internal const int MaximumStringBytes = WorkflowSchemaProfile.MaximumStringBytes;

    private static readonly WorkflowSchemaValidator Shared = new();

    internal static void ValidateDescriptor(WorkflowActionDescriptor descriptor) =>
        ThrowIfInvalid(Shared.ValidateDescriptor(descriptor), nameof(descriptor));

    internal static void ValidateSchema(JsonElement schema) =>
        ThrowIfInvalid(Shared.ValidateSchema(schema), nameof(schema));

    internal static void ValidateInstance(JsonElement schema, JsonElement instance, int maximumBytes) =>
        ThrowIfInvalid(Shared.ValidateInstance(schema, instance, maximumBytes), nameof(instance));

    private static void ThrowIfInvalid(WorkflowSchemaValidationResult result, string parameterName)
    {
        if (result.IsValid)
        {
            return;
        }
        var first = result.Issues.First();
        throw new ArgumentException($"{first.Code}（{first.Path}）：{first.Message}", parameterName);
    }
}
