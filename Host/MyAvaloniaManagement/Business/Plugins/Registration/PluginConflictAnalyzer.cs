using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using static MyAvaloniaManagement.Business.Plugins.Registration.PluginRegistryBuilder;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>一条全局冲突事实；只携带所有者、稳定诊断身份与来源，不拥有 Provider。</summary>
internal sealed record PluginContributionConflict(
    PluginId OwnerId, string Code, string? StableId, Type? Contributor = null);

/// <summary>按原发现顺序分析跨插件冲突，由 Builder 决定何时报告并提交排除。</summary>
/// <remarks>
/// 使用惰性枚举保留“发现一条、报告一条”的失败边界。诊断端口抛错后不会提前计算后续
/// 来源，也不改变 Builder 已封闭的状态；这里不解析插件服务、不执行工厂、不释放资源。
/// 局部重复和全局冲突规则不同，不能将二者抽象成统一的首次胜出或通用规则管线。
/// </remarks>
internal static class PluginConflictAnalyzer
{
    /// <summary>枚举图标、文档、工具、动作、命令、位置和模型映射的有序冲突事实。</summary>
    internal static IEnumerable<PluginContributionConflict> Analyze(PluginContributionSnapshot contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        // 图标完整引用包含 Owner。同一候选误导入两次也必须排除，因此不采用跨 Owner 判重。
        foreach (var duplicate in contributions.Icons.GroupBy(item => item.Reference).Where(group => group.Count() > 1))
        foreach (var owner in duplicate.Select(item => item.OwnerId).Distinct())
            yield return new(owner, "ICON_REFERENCE_DUPLICATE", duplicate.Key);

        foreach (var conflict in FindConflicts(contributions.Documents,
                     item => item.Descriptor.DocumentTypeId, item => item.OwnerId,
                     item => item.ModelType, "DOCUMENT_ID_DUPLICATE"))
            yield return conflict;
        foreach (var conflict in FindConflicts(contributions.Tools,
                     item => item.Descriptor.ToolTypeId, item => item.OwnerId,
                     item => item.ModelType, "TOOL_ID_DUPLICATE"))
            yield return conflict;
        foreach (var conflict in FindConflicts(contributions.WorkflowActions,
                     item => item.Descriptor.Id, item => item.OwnerId,
                     item => item.HandlerType, "WORKFLOW_ACTION_ID_DUPLICATE"))
            yield return conflict;
        foreach (var conflict in FindConflicts(contributions.WorkbenchCommands,
                     item => item.Descriptor.CommandId, item => item.OwnerId,
                     item => ResolveCommandContributor(contributions, item), HostDiagnosticCodes.WorkbenchCommandIdDuplicate))
            yield return conflict;

        var placements = contributions.MenuCommandContributions
            .Select(item => new Placement(item.OwnerId, item.Descriptor.PlacementId, item.Descriptor.CommandId))
            .Concat(contributions.KeyBindingContributions.Select(item =>
                new Placement(item.OwnerId, item.Descriptor.PlacementId, item.Descriptor.CommandId)))
            .ToArray();
        foreach (var conflict in FindConflicts(placements, item => item.PlacementId, item => item.OwnerId,
                     item => ResolvePlacementContributor(contributions, item),
                     HostDiagnosticCodes.WorkbenchCommandPlacementIdDuplicate))
            yield return conflict;

        var views = contributions.Documents.Select(item => (item.OwnerId, item.ModelType, item.ViewType))
            .Concat(contributions.Tools.Select(item => (item.OwnerId, item.ModelType, item.ViewType)))
            .ToArray();
        foreach (var conflict in FindConflicts(views, item => item.ModelType, item => item.OwnerId,
                     item => item.ViewType, "VIEW_MODEL_REGISTRATION_DUPLICATE"))
            yield return conflict;
    }

    /// <summary>只将涉及多个真实 Owner 的重复键视为全局冲突，保持组和 Owner 的发现顺序。</summary>
    private static IEnumerable<PluginContributionConflict> FindConflicts<TItem, TKey>(
        IEnumerable<TItem> source, Func<TItem, TKey> keySelector, Func<TItem, PluginId> ownerSelector,
        Func<TItem, Type> contributorSelector, string code) where TKey : notnull
    {
        foreach (var group in source.GroupBy(keySelector).Where(group =>
                     group.Select(ownerSelector).Distinct().Count() > 1))
        {
            var entries = group.ToArray();
            foreach (var owner in entries.Select(ownerSelector).Distinct())
            {
                var contributor = contributorSelector(entries.First(item => ownerSelector(item) == owner));
                yield return new(owner, code, group.Key.ToString(), contributor);
            }
        }
    }

    private static Type ResolveCommandContributor(
        PluginContributionSnapshot contributions, WorkbenchCommandDeclaration command) =>
        contributions.Documents.FirstOrDefault(document =>
            document.OwnerId == command.OwnerId &&
            document.Descriptor.DocumentTypeId == command.TargetDocumentTypeId)?.ModelType ??
        typeof(PluginRegistryBuilder);

    private static Type ResolvePlacementContributor(PluginContributionSnapshot contributions, Placement placement)
    {
        var command = contributions.WorkbenchCommands.FirstOrDefault(item =>
            item.OwnerId == placement.OwnerId && item.Descriptor.CommandId == placement.CommandId);
        // 保留缺失局部 Seal 时的原来源回退，不因提取类改名而改变诊断来源。
        return command is null ? typeof(PluginRegistryBuilder) : ResolveCommandContributor(contributions, command);
    }

    private sealed record Placement(PluginId OwnerId, CommandPlacementId PlacementId, CommandId CommandId);
}
