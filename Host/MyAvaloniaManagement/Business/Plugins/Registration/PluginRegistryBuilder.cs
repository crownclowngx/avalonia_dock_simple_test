using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>
/// 在组合阶段收集声明式贡献，并在完整校验后一次性发布不可变 Registry。
/// </summary>
/// <remarks>
/// 每个插件先写入独立的临时 Builder。模块配置、插件内校验或 Provider 构建失败时直接丢弃该
/// Builder；只有候选成功后才导入全局 Builder。最终构建再以简单分组判重找出跨插件冲突，
/// 整体排除冲突所有者，不使用可变 Registry、回滚事务或通用规则引擎。
/// </remarks>
internal sealed class PluginRegistryBuilder
{
    private readonly List<DocumentDeclaration> _documents = [];
    private readonly List<ToolDeclaration> _tools = [];
    private readonly List<LifecycleDeclaration> _lifecycles = [];
    private readonly List<WorkflowActionDeclaration> _workflowActions = [];
    private readonly HashSet<PluginId> _workflowConsumers = [];
    private bool _built;

    internal void AddDocument(
        PluginId ownerId,
        DocumentDescriptor descriptor,
        Type modelType,
        Type viewType,
        Func<Control> viewFactory,
        bool isPersistable)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(viewType);
        ArgumentNullException.ThrowIfNull(viewFactory);
        _documents.Add(new DocumentDeclaration(
            ownerId, descriptor, modelType, viewType, viewFactory, isPersistable));
    }

    internal void AddTool(
        PluginId ownerId,
        ToolDescriptor descriptor,
        Type modelType,
        Type viewType,
        Func<Control> viewFactory)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentNullException.ThrowIfNull(viewType);
        ArgumentNullException.ThrowIfNull(viewFactory);
        _tools.Add(new ToolDeclaration(ownerId, descriptor, modelType, viewType, viewFactory));
    }

    internal void AddLifecycle(PluginId ownerId, Type implementationType)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(implementationType);
        _lifecycles.Add(new LifecycleDeclaration(ownerId, implementationType));
    }

    /// <summary>收集一个尚未提交的 Workflow Action 声明。</summary>
    internal void AddWorkflowAction(
        PluginId ownerId,
        WorkflowActionDescriptor descriptor,
        Type handlerType)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handlerType);
        WorkflowActionSchemaValidator.ValidateDescriptor(descriptor);
        _workflowActions.Add(new WorkflowActionDeclaration(ownerId, descriptor, handlerType));
    }

    /// <summary>记录当前插件显式请求 caller-bound Gateway。</summary>
    internal void AddWorkflowActionConsumer(PluginId ownerId)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        if (!_workflowConsumers.Add(ownerId))
        {
            throw new HostCompositionException([
                new HostCompositionDiagnostic(
                    "WORKFLOW_ACTION_CONSUMER_DUPLICATE",
                    ownerId.Value,
                    [])]);
        }
    }

    /// <summary>把一个已经通过插件内校验和 Provider 构建的候选原子导入全局集合。</summary>
    internal void Import(PluginRegistryBuilder source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureWritable();
        source.ValidateSingleOwner();
        _documents.AddRange(source._documents);
        _tools.AddRange(source._tools);
        _lifecycles.AddRange(source._lifecycles);
        _workflowActions.AddRange(source._workflowActions);
        _workflowConsumers.UnionWith(source._workflowConsumers);
    }

    /// <summary>返回需要在候选发布前验证构造的生命周期 singleton 类型。</summary>
    internal IReadOnlyList<Type> GetLifecycleTypes() =>
        _lifecycles.Select(item => item.ImplementationType).Distinct().ToArray();

    /// <summary>返回需要在候选发布前由独立 Scope 验证解析的 Handler 类型。</summary>
    internal IReadOnlyList<Type> GetWorkflowActionHandlerTypes() =>
        _workflowActions.Select(item => item.HandlerType).Distinct().ToArray();

    internal bool IsWorkflowActionConsumer(PluginId ownerId) =>
        _workflowConsumers.Contains(ownerId);

    /// <summary>
    /// 校验当前临时 Builder 只表示一个完整且自洽的所有者。
    /// </summary>
    /// <exception cref="HostCompositionException">存在重复、所有者不匹配或生命周期冲突。</exception>
    internal void ValidateSingleOwner(PluginId? expectedOwner = null)
    {
        EnsureWritable();
        var diagnostics = new List<HostCompositionDiagnostic>();
        var owners = _documents.Select(item => item.OwnerId)
            .Concat(_tools.Select(item => item.OwnerId))
            .Concat(_lifecycles.Select(item => item.OwnerId))
            .Concat(_workflowActions.Select(item => item.OwnerId))
            .Concat(_workflowConsumers)
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
            foreach (var document in _documents.Where(item =>
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

            foreach (var tool in _tools.Where(item =>
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

            foreach (var action in _workflowActions.Where(item =>
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
        }

        AddDuplicateDiagnostics(
            _documents, item => item.Descriptor.DocumentTypeId,
            item => item.ModelType, "DOCUMENT_ID_DUPLICATE", diagnostics);
        AddDuplicateDiagnostics(
            _tools, item => item.Descriptor.ToolTypeId,
            item => item.ModelType, "TOOL_ID_DUPLICATE", diagnostics);
        AddDuplicateDiagnostics(
            _documents, item => item.ModelType,
            item => item.ModelType, "DOCUMENT_CONTRIBUTION_TYPE_DUPLICATE", diagnostics);
        AddDuplicateDiagnostics(
            _tools, item => item.ModelType,
            item => item.ModelType, "TOOL_CONTRIBUTION_TYPE_DUPLICATE", diagnostics);
        AddDuplicateDiagnostics(
            _workflowActions, item => item.Descriptor.Id,
            item => item.HandlerType, "WORKFLOW_ACTION_ID_DUPLICATE", diagnostics);
        AddDuplicateDiagnostics(
            _workflowActions, item => item.HandlerType,
            item => item.HandlerType, "WORKFLOW_ACTION_HANDLER_TYPE_DUPLICATE", diagnostics);

        if (_workflowActions.Count > 0 && _workflowConsumers.Count > 0)
        {
            diagnostics.Add(new HostCompositionDiagnostic(
                "WORKFLOW_ACTION_PROVIDER_CONSUMER_CONFLICT",
                expectedOwner?.Value ?? owners.SingleOrDefault()?.Value,
                _workflowActions.Select(item => ToContributor(item.HandlerType))
                    .Distinct().ToArray()));
        }

        var localViews = _documents.Select(item => new ViewDeclaration(
                item.OwnerId, item.ModelType, item.ViewType, item.ViewFactory))
            .Concat(_tools.Select(item => new ViewDeclaration(
                item.OwnerId, item.ModelType, item.ViewType, item.ViewFactory)));
        AddDuplicateDiagnostics(
            localViews,
            item => item.ModelType,
            item => item.ViewType,
            "VIEW_MODEL_REGISTRATION_DUPLICATE",
            diagnostics);

        foreach (var type in _documents.Select(item => item.ModelType)
                     .Intersect(_tools.Select(item => item.ModelType)))
        {
            diagnostics.Add(Diagnostic(
                "CONTRIBUTION_MODEL_TYPE_CONFLICT",
                type.FullName,
                type));
        }

        if (_lifecycles.Count > 1)
        {
            diagnostics.Add(new HostCompositionDiagnostic(
                "LIFECYCLE_PLUGIN_ID_DUPLICATE",
                owners.SingleOrDefault()?.Value,
                _lifecycles.Select(item => ToContributor(item.ImplementationType))
                    .Distinct().ToArray()));
        }

        if (diagnostics.Count > 0)
        {
            throw new HostCompositionException(diagnostics);
        }
    }

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

    /// <summary>
    /// 完成跨所有者冲突隔离并发布本次 Runtime 唯一的不可变 Registry。
    /// </summary>
    internal PluginRegistry Build(
        PluginModuleCatalog? catalog,
        IHostDiagnosticSink? diagnosticSink = null,
        PluginProviderOwner? pluginProviders = null)
    {
        EnsureWritable();
        _built = true;

        var rejectedOwners = new HashSet<PluginId>();
        RejectGlobalConflicts(
            _documents,
            item => item.Descriptor.DocumentTypeId,
            item => item.OwnerId,
            item => item.ModelType,
            "DOCUMENT_ID_DUPLICATE",
            rejectedOwners,
            diagnosticSink);
        RejectGlobalConflicts(
            _tools,
            item => item.Descriptor.ToolTypeId,
            item => item.OwnerId,
            item => item.ModelType,
            "TOOL_ID_DUPLICATE",
            rejectedOwners,
            diagnosticSink);
        RejectGlobalConflicts(
            _workflowActions,
            item => item.Descriptor.Id,
            item => item.OwnerId,
            item => item.HandlerType,
            "WORKFLOW_ACTION_ID_DUPLICATE",
            rejectedOwners,
            diagnosticSink);

        var views = _documents.Select(item => new ViewDeclaration(
                item.OwnerId, item.ModelType, item.ViewType, item.ViewFactory))
            .Concat(_tools.Select(item => new ViewDeclaration(
                item.OwnerId, item.ModelType, item.ViewType, item.ViewFactory)))
            .ToArray();
        RejectGlobalConflicts(
            views,
            item => item.ModelType,
            item => item.OwnerId,
            item => item.ViewType,
            "VIEW_MODEL_REGISTRATION_DUPLICATE",
            rejectedOwners,
            diagnosticSink);

        var acceptedDocuments = _documents
            .Where(item => !rejectedOwners.Contains(item.OwnerId))
            .Select(item => new PluginDocumentRegistration(
                item.OwnerId,
                item.Descriptor,
                item.ModelType,
                item.ViewType,
                item.ViewFactory,
                item.IsPersistable))
            .ToArray();
        var acceptedTools = _tools
            .Where(item => !rejectedOwners.Contains(item.OwnerId))
            .Select(item => new PluginToolRegistration(
                item.OwnerId,
                item.Descriptor,
                item.ModelType,
                item.ViewType,
                item.ViewFactory))
            .ToArray();
        var acceptedLifecycles = _lifecycles
            .Where(item => !rejectedOwners.Contains(item.OwnerId))
            .Select(item => new PluginLifecycleDeclaration(
                item.OwnerId, item.ImplementationType))
            .ToArray();
        var acceptedActions = _workflowActions
            .Where(item => !rejectedOwners.Contains(item.OwnerId))
            .Select(item => new PluginWorkflowActionRegistration(
                item.OwnerId,
                item.Descriptor,
                item.HandlerType))
            .ToArray();
        var acceptedConsumers = _workflowConsumers
            .Where(owner => !rejectedOwners.Contains(owner))
            .ToHashSet();

        var acceptedPluginIds = pluginProviders?.AvailablePluginIds
            .Where(owner => !rejectedOwners.Contains(owner))
            .ToHashSet();

        // 先完成所有只读事实的构造，再提交 Provider 与 Scope 租约。这样即使快照复制或索引
        // 构造意外失败，也没有任何运行期所有权已经对外可见，失败原子性不依赖事后回滚。
        var registry = new PluginRegistry(
            catalog?.CreatePluginSnapshots(
                acceptedDocuments,
                acceptedTools,
                acceptedLifecycles,
                acceptedPluginIds) ?? [],
            acceptedDocuments,
            acceptedTools,
            acceptedLifecycles,
            acceptedActions,
            acceptedConsumers);
        pluginProviders?.CommitRegistryResult(rejectedOwners);
        return registry;
    }

    private static void RejectGlobalConflicts<TItem, TKey>(
        IEnumerable<TItem> source,
        Func<TItem, TKey> keySelector,
        Func<TItem, PluginId> ownerSelector,
        Func<TItem, Type> contributorSelector,
        string code,
        ISet<PluginId> rejectedOwners,
        IHostDiagnosticSink? sink)
        where TKey : notnull
    {
        foreach (var group in source.GroupBy(keySelector).Where(group =>
                     group.Select(ownerSelector).Distinct().Count() > 1))
        {
            var entries = group.ToArray();
            foreach (var owner in entries.Select(ownerSelector).Distinct())
            {
                var contributor = contributorSelector(
                    entries.First(item => ownerSelector(item) == owner));
                rejectedOwners.Add(owner);
                sink?.Report(new HostDiagnosticDraft(
                    code,
                    HostDiagnosticPhase.ExtensionDiscovery)
                {
                    PluginId = owner,
                    StableId = group.Key.ToString(),
                    AssemblyName = contributor.Assembly.GetName(),
                });
            }
        }
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

    private static HostCompositionDiagnostic Diagnostic(
        string code,
        string? stableId,
        Type type) => new(code, stableId, [ToContributor(type)]);

    private static HostCompositionContributor ToContributor(Type type) =>
        new(type.FullName ?? type.Name, type.Assembly.GetName().Name ?? "Unknown");

    private void EnsureWritable()
    {
        if (_built)
        {
            throw new InvalidOperationException("Plugin Registry 已经构建，不能再次修改或发布。");
        }
    }

    /// <summary>插件局部 Builder 中尚未提交的 Document 候选事实。</summary>
    internal sealed record DocumentDeclaration(
        PluginId OwnerId,
        DocumentDescriptor Descriptor,
        Type ModelType,
        Type ViewType,
        Func<Control> ViewFactory,
        bool IsPersistable);

    /// <summary>插件局部 Builder 中尚未提交的 Tool 候选事实。</summary>
    internal sealed record ToolDeclaration(
        PluginId OwnerId,
        ToolDescriptor Descriptor,
        Type ModelType,
        Type ViewType,
        Func<Control> ViewFactory);

    /// <summary>插件局部 Builder 中尚未提交的生命周期候选事实。</summary>
    internal sealed record LifecycleDeclaration(PluginId OwnerId, Type ImplementationType);

    /// <summary>插件局部 Builder 中尚未提交的 Workflow Action 候选事实。</summary>
    internal sealed record WorkflowActionDeclaration(
        PluginId OwnerId,
        WorkflowActionDescriptor Descriptor,
        Type HandlerType);

    /// <summary>全局模型映射冲突检查使用的最小 View 候选投影。</summary>
    private sealed record ViewDeclaration(
        PluginId OwnerId,
        Type ModelType,
        Type ViewType,
        Func<Control> ViewFactory);
}
