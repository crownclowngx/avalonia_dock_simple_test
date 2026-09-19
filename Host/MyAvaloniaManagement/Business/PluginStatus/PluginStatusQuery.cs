using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Enablement;
using System.IO;
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
/// 发现快照补足未加载身份，设置服务提供下次意图。各事实仍由原组件拥有；
/// 这里不建立第二套运行状态，不扫描目录，也不激活插件。
/// 诊断按稳定身份合并，避免同一插件因多个失败阶段重复占行。
/// </remarks>
internal sealed class PluginStatusQuery(
    PluginRegistry registry,
    PluginAvailabilityReadModel availability,
    HostDiagnosticSession? diagnostics = null,
    PluginDiscoverySnapshot? discovery = null,
    IPluginEnablementState? enablement = null) : IPluginStatusQuery
{
    public IReadOnlyList<PluginStatusItem> Capture()
    {
        var records = diagnostics?.Snapshot ?? [];
        var candidates = discovery?.Candidates ?? [];
        var identities = candidates.ToLookup(candidate => candidate.Manifest.PluginId.Value, StringComparer.Ordinal);
        var settings = enablement?.Current;
        // 早期诊断可能只有目录，后续诊断才取得 PluginId。只有目录明确对应唯一身份时才关联，
        // 防止把两个不同插件的故障混在一起；大小写策略与原目录发现机制保持一致。
        var directoryOwners = records.Where(item => item.PluginDirectory is not null && item.PluginId is not null)
            .Select(item => (Directory: item.PluginDirectory!, Id: item.PluginId!))
            .Concat(candidates.Select(item => (Directory: Path.GetFileName(item.DirectoryPath), Id: item.Manifest.PluginId.Value)))
            .GroupBy(item => item.Directory, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Directory: group.Key, Ids: group.Select(item => item.Id).Distinct(StringComparer.Ordinal).ToArray()))
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
        // Registry 只含成功提交的插件。候选快照补足禁用和加载前失败，不依靠伪造错误来保留行。
        var registeredKeys = result.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var candidate in candidates.DistinctBy(item => item.Manifest.PluginId))
        {
            var manifest = candidate.Manifest;
            var key = "plugin:" + manifest.PluginId.Value;
            if (registeredKeys.Contains(key)) continue;
            var candidateRecords = groups.GetValueOrDefault(key) ?? [];
            groups.Remove(key);
            var enabled = discovery!.StartupSettings.Settings?.IsEnabled(manifest.PluginId);
            var item = enabled == true && candidateRecords.Length > 0
                ? PluginStatusPresentation.ForRejectedCandidate(candidateRecords)
                : new PluginStatusItem(manifest.PluginId.Value, Path.GetFileNameWithoutExtension(manifest.EntryPoint.Assembly),
                    enabled == false ? "已禁用 · 未加载" : enabled is null ? "配置不可用 · 本次未加载" : "未完成加载",
                    "—", "不可用", enabled == false ? "本次启动按用户设置跳过插件；重新启用将在重启 Host 后尝试加载。" :
                    enabled is null ? discovery.StartupSettings.Message : "插件未进入已提交的注册表，请查看本次诊断。");
            result.Add(item with
            {
                Key = key, PluginId = manifest.PluginId.Value,
                VersionText = PluginVersionText.Format(manifest.PluginVersion), CompatibilityText = $"Plugin SDK {manifest.Sdk}",
                HasProblem = enabled != false || candidateRecords.Any(IsProblem), Diagnostics = ToDiagnostics(candidateRecords),
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
        return result.Select(item =>
            {
                var matches = identities[item.PluginId];
                if (matches.Count() != 1) return item;
                var id = matches.First().Manifest.PluginId;
                return item with
                {
                    EnabledAtStartup = discovery!.StartupSettings.Settings?.IsEnabled(id),
                    NextStartupEnabled = settings?.Settings?.IsEnabled(id),
                    CanSetEnablement = settings?.CanWrite == true,
                };
            }).OrderByDescending(item => item.HasProblem)
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
