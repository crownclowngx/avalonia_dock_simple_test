using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.WorkflowActions;

/// <summary>解决 Host Provider 先构建、Plugin Registry 后发布的单次目录提交时序。</summary>
/// <remarks>
/// Store 在组合期可以先被 caller-bound Gateway 引用，但在 Registry 冲突隔离完成后才提交真实目录。
/// 提交只允许一次；之后只读查询，不提供追加、删除或刷新 API。目录项仍只保存元数据和 Handler Type，
/// 不保存 Provider、Scope 或 Handler 实例。
/// </remarks>
internal sealed class WorkflowActionCatalogStore
{
    private readonly object _gate = new();
    private IReadOnlyDictionary<WorkflowActionId, PluginWorkflowActionRegistration>? _entries;
    private PluginAvailabilityReadModel? _availability;

    internal string Revision { get; private set; } = string.Empty;

    internal bool IsCommitted
    {
        get
        {
            lock (_gate)
            {
                return _entries is not null;
            }
        }
    }

    /// <summary>一次性提交最终 Registry 动作和只读可用性投影。</summary>
    internal void Commit(
        PluginRegistry registry,
        PluginAvailabilityReadModel availability)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(availability);
        var entries = registry.WorkflowActions.ToDictionary(item => item.Descriptor.Id);
        var revision = ComputeRevision(entries.Values);
        lock (_gate)
        {
            if (_entries is not null)
            {
                throw new InvalidOperationException("Workflow Action 目录已经提交，不能二次发布。");
            }
            _entries = entries;
            _availability = availability;
            Revision = revision;
        }
    }

    internal IReadOnlyList<WorkflowActionDescriptor> GetAvailableDescriptors()
    {
        lock (_gate)
        {
            EnsureCommitted();
            return _entries!.Values
                .Where(item => _availability!.IsAvailable(item.OwnerId))
                .OrderBy(item => item.Descriptor.Id.Value, StringComparer.Ordinal)
                .Select(item => item.Descriptor)
                .ToArray();
        }
    }

    internal bool TryGet(
        WorkflowActionId actionId,
        out PluginWorkflowActionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        lock (_gate)
        {
            EnsureCommitted();
            return _entries!.TryGetValue(actionId, out registration!);
        }
    }

    internal bool IsOwnerAvailable(PluginId ownerId)
    {
        lock (_gate)
        {
            EnsureCommitted();
            return _availability!.IsAvailable(ownerId);
        }
    }

    private void EnsureCommitted()
    {
        if (_entries is null)
        {
            throw new InvalidOperationException("Workflow Action 目录尚未提交。");
        }
    }

    private static string ComputeRevision(
        IEnumerable<PluginWorkflowActionRegistration> registrations)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in registrations.OrderBy(
                         item => item.Descriptor.Id.Value,
                         StringComparer.Ordinal))
            {
                var descriptor = item.Descriptor;
                writer.WriteStartObject();
                writer.WriteString("owner", item.OwnerId.Value);
                writer.WriteString("id", descriptor.Id.Value);
                writer.WriteString("displayName", descriptor.DisplayName);
                writer.WriteString("description", descriptor.Description);
                writer.WriteNumber("risks", (int)descriptor.Risks);
                writer.WriteNumber("confirmation", (int)descriptor.ConfirmationPolicy);
                writer.WritePropertyName("sensitive");
                writer.WriteStartArray();
                foreach (var pointer in descriptor.SensitiveInputPointers.Order(StringComparer.Ordinal))
                {
                    writer.WriteStringValue(pointer);
                }
                writer.WriteEndArray();
                writer.WritePropertyName("input");
                WorkflowActionJsonCanonicalizer.Write(writer, descriptor.InputSchema);
                writer.WritePropertyName("output");
                WorkflowActionJsonCanonicalizer.Write(writer, descriptor.OutputSchema);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

}
