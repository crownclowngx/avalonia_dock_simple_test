using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Commands.Catalog;

/// <summary>表示合并目录中一条只读命令事实。</summary>
internal abstract record WorkbenchCommandCatalogEntry(CommandDescriptor Descriptor);

/// <summary>表示由 Host 拥有并通过显式 Handler 执行的目录事实。</summary>
internal sealed record HostWorkbenchCommandCatalogEntry(
    CommandDescriptor Descriptor,
    IHostWorkbenchCommandHandler Handler)
    : WorkbenchCommandCatalogEntry(Descriptor);

/// <summary>表示由插件声明、将在 G3 路由到活动 Document 实例的目录事实。</summary>
internal sealed record PluginWorkbenchCommandCatalogEntry(
    PluginId OwnerId,
    CommandDescriptor Descriptor,
    DocumentTypeId TargetDocumentTypeId)
    : WorkbenchCommandCatalogEntry(Descriptor);

/// <summary>把 Host 内建目录和插件 Registry 合并为统一的无 UI 查询面。</summary>
/// <remarks>
/// 合并层保留所有已经冻结的插件声明，不按生命周期状态过滤。Executor 必须在每次调用前重新查询
/// owner availability，避免把启动时快照误当成运行期事实。目录不保存插件 Target、Provider 或 Scope。
/// </remarks>
internal sealed class WorkbenchCommandCatalog
{
    private readonly IReadOnlyDictionary<CommandId, WorkbenchCommandCatalogEntry> _entries;

    internal WorkbenchCommandCatalog(
        HostWorkbenchCommandCatalog host,
        PluginRegistry plugins)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(plugins);

        var hostEntries = host.Registrations.Select(item =>
            new HostWorkbenchCommandCatalogEntry(item.Descriptor, item.Handler));
        var pluginEntries = plugins.WorkbenchCommands.Select(item =>
            new PluginWorkbenchCommandCatalogEntry(
                item.OwnerId,
                item.Descriptor,
                item.TargetDocumentTypeId));
        var entries = hostEntries
            .Cast<WorkbenchCommandCatalogEntry>()
            .Concat(pluginEntries)
            .ToArray();

        var conflicts = entries
            .GroupBy(item => item.Descriptor.CommandId)
            .Where(group => group.Count() > 1)
            .Select(group => new HostCompositionDiagnostic(
                HostDiagnosticCodes.WorkbenchCommandIdDuplicate,
                group.Key.Value,
                [
                    new HostCompositionContributor(
                        nameof(HostWorkbenchCommandCatalog),
                        typeof(HostWorkbenchCommandCatalog).Assembly.GetName().Name!),
                    new HostCompositionContributor(
                        nameof(PluginRegistry),
                        typeof(PluginRegistry).Assembly.GetName().Name!),
                ]))
            .ToArray();
        if (conflicts.Length > 0)
        {
            // 合法 G1 插件命名空间不会与 Host ID 重叠；这里仍在最终合并边界防御，
            // 防止测试构造、未来内部迁移或损坏快照悄悄采用“最后加载者胜出”。
            throw new HostCompositionException(conflicts);
        }

        _entries = entries.ToDictionary(item => item.Descriptor.CommandId);
    }

    /// <summary>获取按 CommandId 排序的防御性快照。</summary>
    internal IReadOnlyList<WorkbenchCommandCatalogEntry> Entries =>
        _entries.Values
            .OrderBy(item => item.Descriptor.CommandId.Value, StringComparer.Ordinal)
            .ToArray();

    internal bool TryGet(
        CommandId commandId,
        out WorkbenchCommandCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        return _entries.TryGetValue(commandId, out entry!);
    }
}
