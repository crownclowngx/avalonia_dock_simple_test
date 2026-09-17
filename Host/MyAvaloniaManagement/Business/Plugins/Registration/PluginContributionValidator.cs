using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using static MyAvaloniaManagement.Business.Plugins.Registration.PluginRegistryBuilder;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>只校验单个插件候选中的冻结声明，返回有序诊断，不解析或释放任何服务。</summary>
/// <remarks>
/// 设计思路：把身份、重复和声明关系作为数据规则集中维护。可写状态和抛出组合异常的时点
/// 仍归 Builder；本类不保存候选、不调用 View 工厂、不向日志提交诊断。
/// 规则顺序与原 Seal 保持一致，最终异常的稳定排序仍由 HostCompositionException 完成。
/// </remarks>
internal static class PluginContributionValidator
{
    /// <summary>校验本次声明快照；expectedOwner 为空时仍执行导入所需的局部完整性防线。</summary>
    internal static IReadOnlyList<HostCompositionDiagnostic> Validate(
        PluginContributionSnapshot contributions, PluginId? expectedOwner = null)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        var diagnostics = new List<HostCompositionDiagnostic>();
        var owners = contributions.Documents.Select(item => item.OwnerId)
            .Concat(contributions.Tools.Select(item => item.OwnerId))
            .Concat(contributions.Lifecycles.Select(item => item.OwnerId))
            .Concat(contributions.WorkflowActions.Select(item => item.OwnerId))
            .Concat(contributions.WorkflowConsumers)
            .Concat(contributions.WorkbenchCommands.Select(item => item.OwnerId))
            .Concat(contributions.MenuCommandContributions.Select(item => item.OwnerId))
            .Concat(contributions.KeyBindingContributions.Select(item => item.OwnerId))
            .Concat(contributions.Icons.Select(item => item.OwnerId))
            .Distinct()
            .ToArray();
        if (owners.Length > 1)
        {
            diagnostics.Add(new HostCompositionDiagnostic(
                "EXTENSION_OWNER_MISMATCH",
                string.Join(",", owners.Select(item => item.Value)),
                []));
        }

        if (expectedOwner is not null)
        {
            foreach (var icon in contributions.Icons.Where(item => item.OwnerId != expectedOwner))
            {
                diagnostics.Add(IdentityDiagnostic("ICON_OWNER_MISMATCH", icon.Reference));
            }
            foreach (var document in contributions.Documents.Where(item =>
                         !BelongsToOwner(
                             item.Descriptor.DocumentTypeId.Value,
                             expectedOwner,
                             "document")))
            {
                diagnostics.Add(Diagnostic(
                    HostDiagnosticCodes.DocumentIdOwnerMismatch,
                    document.Descriptor.DocumentTypeId.Value,
                    document.ModelType));
            }

            foreach (var tool in contributions.Tools.Where(item =>
                         !BelongsToOwner(
                             item.Descriptor.ToolTypeId.Value,
                             expectedOwner,
                             "tool")))
            {
                diagnostics.Add(Diagnostic(
                    HostDiagnosticCodes.ToolIdOwnerMismatch,
                    tool.Descriptor.ToolTypeId.Value,
                    tool.ModelType));
            }

            foreach (var action in contributions.WorkflowActions.Where(item =>
                         !BelongsToOwner(
                             item.Descriptor.Id.Value,
                             expectedOwner,
                             "workflow")))
            {
                diagnostics.Add(Diagnostic(
                    "WORKFLOW_ACTION_ID_OWNER_MISMATCH",
                    action.Descriptor.Id.Value,
                    action.HandlerType));
            }

            ValidateWorkbenchCommandOwnership(contributions, expectedOwner, diagnostics);
        }

        AddDuplicateDiagnostics(
            contributions.Documents, item => item.Descriptor.DocumentTypeId,
            item => item.ModelType, "DOCUMENT_ID_DUPLICATE", diagnostics);
        AddDuplicateDiagnostics(
            contributions.Tools, item => item.Descriptor.ToolTypeId,
            item => item.ModelType, "TOOL_ID_DUPLICATE", diagnostics);
        AddDuplicateDiagnostics(
            contributions.Documents, item => item.ModelType,
            item => item.ModelType, "DOCUMENT_CONTRIBUTION_TYPE_DUPLICATE", diagnostics);
        AddDuplicateDiagnostics(
            contributions.Tools, item => item.ModelType,
            item => item.ModelType, "TOOL_CONTRIBUTION_TYPE_DUPLICATE", diagnostics);
        AddDuplicateDiagnostics(
            contributions.WorkflowActions, item => item.Descriptor.Id,
            item => item.HandlerType, "WORKFLOW_ACTION_ID_DUPLICATE", diagnostics);
        AddDuplicateDiagnostics(
            contributions.WorkflowActions, item => item.HandlerType,
            item => item.HandlerType, "WORKFLOW_ACTION_HANDLER_TYPE_DUPLICATE", diagnostics);
        AddIdentityDuplicateDiagnostics(
            contributions.WorkbenchCommands,
            item => item.Descriptor.CommandId,
            HostDiagnosticCodes.WorkbenchCommandIdDuplicate,
            diagnostics);

        AddIdentityDuplicateDiagnostics(contributions.Icons, item => item.Reference,
            "ICON_REFERENCE_DUPLICATE", diagnostics);

        var placements = contributions.MenuCommandContributions
            .Select(item => new WorkbenchPlacementDeclaration(
                item.OwnerId,
                item.Descriptor.PlacementId,
                item.Descriptor.CommandId))
            .Concat(contributions.KeyBindingContributions.Select(item => new WorkbenchPlacementDeclaration(
                item.OwnerId,
                item.Descriptor.PlacementId,
                item.Descriptor.CommandId)))
            .ToArray();
        AddIdentityDuplicateDiagnostics(
            placements,
            item => item.PlacementId,
            HostDiagnosticCodes.WorkbenchCommandPlacementIdDuplicate,
            diagnostics);
        AddIdentityDuplicateDiagnostics(
            contributions.KeyBindingContributions,
            item => (item.Descriptor.Key, item.Descriptor.Modifiers),
            HostDiagnosticCodes.WorkbenchKeyGestureDuplicate,
            diagnostics);

        var localViews = contributions.Documents.Select(item => new ViewDeclaration(
                item.OwnerId, item.ModelType, item.ViewType, item.ViewFactory))
            .Concat(contributions.Tools.Select(item => new ViewDeclaration(
                item.OwnerId, item.ModelType, item.ViewType, item.ViewFactory)));
        AddDuplicateDiagnostics(
            localViews,
            item => item.ModelType,
            item => item.ViewType,
            "VIEW_MODEL_REGISTRATION_DUPLICATE",
            diagnostics);

        foreach (var type in contributions.Documents.Select(item => item.ModelType)
                     .Intersect(contributions.Tools.Select(item => item.ModelType)))
        {
            diagnostics.Add(Diagnostic(
                "CONTRIBUTION_MODEL_TYPE_CONFLICT",
                type.FullName,
                type));
        }

        if (contributions.Lifecycles.Count > 1)
        {
            // 保留原局部校验的失败边界：内部畸形输入若同时包含多 Owner 与多 Lifecycle，
            // SingleOrDefault 会直接抛出 InvalidOperationException；本次等价拆分不改为新的诊断政策。
            diagnostics.Add(new HostCompositionDiagnostic(
                "LIFECYCLE_PLUGIN_ID_DUPLICATE",
                owners.SingleOrDefault()?.Value,
                contributions.Lifecycles.Select(item => ToContributor(item.ImplementationType))
                    .Distinct().ToArray()));
        }

        return diagnostics.AsReadOnly();
    }

    /// <summary>校验 Command 声明与当前插件的 Document/Placement 关系。</summary>
    /// <remarks>
    /// 关系校验统一在 Seal 时执行，因此插件可以按适合自身代码组织的顺序声明 Document、Command
    /// 和 Placement。这里仅比较冻结身份，不解析模型或调用插件代码。
    /// </remarks>
    private static void ValidateWorkbenchCommandOwnership(
        PluginContributionSnapshot contributions,
        PluginId expectedOwner,
        ICollection<HostCompositionDiagnostic> diagnostics)
    {
        foreach (var command in contributions.WorkbenchCommands)
        {
            if (!BelongsToOwner(command.Descriptor.CommandId.Value, expectedOwner, "command"))
            {
                diagnostics.Add(IdentityDiagnostic(
                    HostDiagnosticCodes.WorkbenchCommandIdOwnerMismatch,
                    command.Descriptor.CommandId.Value));
            }

            if (!BelongsToOwner(command.TargetDocumentTypeId.Value, expectedOwner, "document"))
            {
                diagnostics.Add(IdentityDiagnostic(
                    HostDiagnosticCodes.WorkbenchCommandTargetDocumentOwnerMismatch,
                    command.TargetDocumentTypeId.Value));
            }
            else if (!contributions.Documents.Any(document =>
                         document.OwnerId == expectedOwner &&
                         document.Descriptor.DocumentTypeId == command.TargetDocumentTypeId))
            {
                diagnostics.Add(IdentityDiagnostic(
                    HostDiagnosticCodes.WorkbenchCommandTargetDocumentNotRegistered,
                    command.TargetDocumentTypeId.Value));
            }
        }

        var commandIds = contributions.WorkbenchCommands
            .Where(command => command.OwnerId == expectedOwner)
            .Select(command => command.Descriptor.CommandId)
            .ToHashSet();
        foreach (var placement in contributions.MenuCommandContributions
                     .Select(item => new WorkbenchPlacementDeclaration(
                         item.OwnerId,
                         item.Descriptor.PlacementId,
                         item.Descriptor.CommandId))
                     .Concat(contributions.KeyBindingContributions.Select(item =>
                         new WorkbenchPlacementDeclaration(
                             item.OwnerId,
                             item.Descriptor.PlacementId,
                             item.Descriptor.CommandId))))
        {
            ValidatePlacement(expectedOwner, placement, commandIds, diagnostics);
        }

        foreach (var menu in contributions.MenuCommandContributions.Where(item =>
                     !IsSupportedMenuLocation(item.Descriptor.LocationId)))
        {
            diagnostics.Add(IdentityDiagnostic(
                HostDiagnosticCodes.WorkbenchMenuLocationUnsupported,
                menu.Descriptor.LocationId.Value));
        }
    }

    private static void ValidatePlacement(
        PluginId expectedOwner,
        WorkbenchPlacementDeclaration placement,
        IReadOnlySet<CommandId> commandIds,
        ICollection<HostCompositionDiagnostic> diagnostics)
    {
        if (!BelongsToOwner(placement.PlacementId.Value, expectedOwner, "command-placement"))
        {
            diagnostics.Add(IdentityDiagnostic(
                HostDiagnosticCodes.WorkbenchCommandPlacementIdOwnerMismatch,
                placement.PlacementId.Value));
        }

        if (!BelongsToOwner(placement.CommandId.Value, expectedOwner, "command"))
        {
            diagnostics.Add(IdentityDiagnostic(
                HostDiagnosticCodes.WorkbenchCommandPlacementCommandOwnerMismatch,
                placement.CommandId.Value));
        }
        else if (!commandIds.Contains(placement.CommandId))
        {
            diagnostics.Add(IdentityDiagnostic(
                HostDiagnosticCodes.WorkbenchCommandPlacementCommandNotRegistered,
                placement.CommandId.Value));
        }
    }

    private static bool IsSupportedMenuLocation(MenuLocationId locationId) =>
        locationId == WorkbenchMenuLocations.FileShared ||
        locationId == WorkbenchMenuLocations.ViewShared ||
        locationId == WorkbenchMenuLocations.ToolsShared ||
        locationId == WorkbenchMenuLocations.HelpShared;

    /// <summary>判断贡献 ID 是否位于指定所有者和贡献种类的精确点分命名空间。</summary>
    /// <remarks>
    /// 前缀末尾显式包含点号，避免所有者 <c>a.b</c> 错误接纳 <c>a.bc</c>；同时要求前缀后
    /// 至少还有一个字符，拒绝只有 <c>{PluginId}.document.</c> 或 <c>.tool.</c> 的空后缀。
    /// 稳定 ID 的词法合法性仍由 SDK 值对象负责，这里只判断跨对象所有权。
    /// </remarks>
    private static bool BelongsToOwner(string stableId, PluginId owner, string contributionKind)
    {
        var prefix = $"{owner.Value}.{contributionKind}.";
        return stableId.Length > prefix.Length &&
               stableId.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static void AddDuplicateDiagnostics<TItem, TKey>(
        IEnumerable<TItem> source,
        Func<TItem, TKey> keySelector,
        Func<TItem, Type> contributorSelector,
        string code,
        ICollection<HostCompositionDiagnostic> diagnostics)
        where TKey : notnull
    {
        foreach (var group in source.GroupBy(keySelector).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new HostCompositionDiagnostic(
                code,
                group.Key.ToString(),
                group.Select(item => ToContributor(contributorSelector(item)))
                    .Distinct().ToArray()));
        }
    }

    private static void AddIdentityDuplicateDiagnostics<TItem, TKey>(
        IEnumerable<TItem> source,
        Func<TItem, TKey> keySelector,
        string code,
        ICollection<HostCompositionDiagnostic> diagnostics)
        where TKey : notnull
    {
        foreach (var group in source.GroupBy(keySelector).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new HostCompositionDiagnostic(code, group.Key.ToString(), []));
        }
    }

    private static HostCompositionDiagnostic Diagnostic(
        string code,
        string? stableId,
        Type type) => new(code, stableId, [ToContributor(type)]);

    private static HostCompositionDiagnostic IdentityDiagnostic(
        string code,
        string stableId) => new(code, stableId, []);

    private static HostCompositionContributor ToContributor(Type type) =>
        new(type.FullName ?? type.Name, type.Assembly.GetName().Name ?? "Unknown");

    private sealed record WorkbenchPlacementDeclaration(
        PluginId OwnerId, CommandPlacementId PlacementId, CommandId CommandId);

    private sealed record ViewDeclaration(
        PluginId OwnerId, Type ModelType, Type ViewType, Func<Control> ViewFactory);
}
