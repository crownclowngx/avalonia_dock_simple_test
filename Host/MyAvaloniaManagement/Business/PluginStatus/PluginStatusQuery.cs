using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Models.Plugins;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.PluginStatus;

/// <summary>窗口只依赖查询契约，既不能修改生命周期，也不能解析插件服务。</summary>
internal interface IPluginStatusQuery
{
    IReadOnlyList<PluginStatusItem> Capture();
}

/// <summary>组合已提交的插件声明、生命周期事实和本次会话诊断，生成无副作用的状态快照。</summary>
/// <remarks>
/// 设计思路：Registry 决定声明，Availability 决定贡献是否开放，诊断补足加载失败的候选。
/// 三者仍由原组件拥有；这里不建立第二套运行状态，不扫描目录，也不激活插件。
/// 诊断按稳定身份合并，避免同一插件因多个失败阶段重复占行。
/// </remarks>
internal sealed class PluginStatusQuery(
    PluginRegistry registry,
    PluginAvailabilityReadModel availability,
    HostDiagnosticSession? diagnostics = null) : IPluginStatusQuery
{
    public IReadOnlyList<PluginStatusItem> Capture()
    {
        var records = diagnostics?.Snapshot ?? [];
        // 早期诊断可能只有目录，后续诊断才取得 PluginId。只有目录明确对应唯一身份时才关联，
        // 防止把两个不同插件的故障混在一起；大小写策略与原目录发现机制保持一致。
        var directoryOwners = records.Where(item => item.PluginDirectory is not null && item.PluginId is not null)
            .GroupBy(item => item.PluginDirectory!, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Directory: group.Key, Ids: group.Select(item => item.PluginId!).Distinct(StringComparer.Ordinal).ToArray()))
            .Where(group => group.Ids.Length == 1)
            .ToDictionary(group => group.Directory, group => group.Ids[0], StringComparer.OrdinalIgnoreCase);
        string? Identity(HostDiagnosticRecord record)
        {
            if (record.PluginId is { } id) return "plugin:" + id;
            if (record.PluginDirectory is not { } directory) return null;
            return directoryOwners.TryGetValue(directory, out var owner)
                ? "plugin:" + owner : "directory:" + directory.ToUpperInvariant();
        }
        var groups = records.Where(record => Identity(record) is not null)
            .GroupBy(record => Identity(record)!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Sequence).ToArray(), StringComparer.Ordinal);
        var result = new List<PluginStatusItem>();
        foreach (var plugin in registry.Plugins)
        {
            var id = plugin.Manifest.PluginId;
            var key = "plugin:" + id.Value;
            var pluginRecords = groups.GetValueOrDefault(key) ?? [];
            groups.Remove(key);
            var state = availability.GetLifecycleState(id);
            var isAvailable = availability.IsAvailable(id);
            var item = PluginStatusPresentation.ForPlugin(plugin, registry.Lifecycles.Any(item => item.OwnerId == id), state);
            // 没有生命周期的插件也可能被宿主统一撤回；可用性必须读取真实门控，不能由状态文案推断。
            if (!isAvailable && item.AvailabilityText == "可用") item = item with { AvailabilityText = "当前不可用" };
            result.Add(item with
            {
                Key = key,
                IsAvailable = isAvailable,
                HasProblem = IsFailure(state?.Status) || pluginRecords.Any(IsProblem),
                Contributions = Contributions(id, isAvailable),
                Diagnostics = ToDiagnostics(pluginRecords)
            });
        }
        foreach (var group in groups)
        {
            // 插件专属的后续命令诊断不代表发现了一个新插件。只有加载/注册阶段的事实才能补候选。
            if (!group.Value.Any(record => IsDiscoveryPhase(record.Phase))) continue;
            result.Add(PluginStatusPresentation.ForRejectedCandidate(group.Value) with
            {
                Key = group.Key,
                HasProblem = true,
                Diagnostics = ToDiagnostics(group.Value)
            });
        }
        return result.OrderByDescending(item => item.HasProblem)
            .ThenBy(item => item.PluginId, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyList<PluginContributionItem> Contributions(PluginId owner, bool available)
    {
        var state = available ? "已声明 · 插件可用" : "已声明 · 插件当前不可用";
        // 描述符是不可变数据。这里刻意不访问 ModelType、ViewFactory 或任何命令执行入口。
        return registry.Documents.Where(item => item.OwnerId == owner)
            .Select(item => new PluginContributionItem("文档", item.Descriptor.DisplayName, item.Descriptor.DocumentTypeId.Value, state))
            .Concat(registry.Tools.Where(item => item.OwnerId == owner)
                .Select(item => new PluginContributionItem("工具", item.Descriptor.DisplayName, item.Descriptor.ToolTypeId.Value, state)))
            .Concat(registry.WorkbenchCommands.Where(item => item.OwnerId == owner)
                .Select(item => new PluginContributionItem("命令", item.Descriptor.DisplayName, item.Descriptor.CommandId.Value, state)))
            .Concat(registry.WorkflowActions.Where(item => item.OwnerId == owner)
                .Select(item => new PluginContributionItem("工作流动作", item.Descriptor.DisplayName, item.Descriptor.Id.Value, state)))
            .OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    private static bool IsDiscoveryPhase(HostDiagnosticPhase phase) => phase is
        HostDiagnosticPhase.PluginRootDiscovery or HostDiagnosticPhase.PluginManifestPreflight or
        HostDiagnosticPhase.PluginAssemblyLoad or HostDiagnosticPhase.PluginTypePreflight or
        HostDiagnosticPhase.PluginModuleDiscovery or HostDiagnosticPhase.PluginServiceRegistration or
        HostDiagnosticPhase.ExtensionDiscovery or HostDiagnosticPhase.HostContainerBuild;

    private static bool IsFailure(PluginLifecycleStatus? status) => status is
        PluginLifecycleStatus.InitializationFailed or PluginLifecycleStatus.InitializationTimedOut or
        PluginLifecycleStatus.HostCancelled or PluginLifecycleStatus.ShutdownFailed or PluginLifecycleStatus.ShutdownTimedOut;

    private static bool IsProblem(HostDiagnosticRecord record) => record.Severity >= HostDiagnosticSeverity.Warning;

    private static IReadOnlyList<PluginDiagnosticItem> ToDiagnostics(IEnumerable<HostDiagnosticRecord> records) => records
        .Select(record => new PluginDiagnosticItem(record.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"),
            record.Severity switch
            {
                HostDiagnosticSeverity.Information => "信息", HostDiagnosticSeverity.Warning => "警告",
                HostDiagnosticSeverity.Error => "错误", HostDiagnosticSeverity.Fatal => "严重错误", _ => "未知"
            }, PluginStatusPresentation.PhaseText(record.Phase), record.Code, record.UserMessage,
            record.TechnicalDetail ?? string.Empty)).ToArray();
}
