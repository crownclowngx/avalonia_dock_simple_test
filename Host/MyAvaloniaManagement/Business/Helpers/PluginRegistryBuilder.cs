using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Helpers;

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

    /// <summary>把一个已经通过插件内校验和 Provider 构建的候选原子导入全局集合。</summary>
    internal void Import(PluginRegistryBuilder source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureWritable();
        source.ValidateSingleOwner();
        _documents.AddRange(source._documents);
        _tools.AddRange(source._tools);
        _lifecycles.AddRange(source._lifecycles);
    }

    /// <summary>返回需要在候选发布前验证构造的生命周期 singleton 类型。</summary>
    internal IReadOnlyList<Type> GetLifecycleTypes() =>
        _lifecycles.Select(item => item.ImplementationType).Distinct().ToArray();

    /// <summary>
    /// 校验当前临时 Builder 只表示一个完整且自洽的所有者。
    /// </summary>
    /// <exception cref="HostCompositionException">存在重复、所有者不匹配或生命周期冲突。</exception>
    internal void ValidateSingleOwner()
    {
        EnsureWritable();
        var diagnostics = new List<HostCompositionDiagnostic>();
        var owners = _documents.Select(item => item.OwnerId)
            .Concat(_tools.Select(item => item.OwnerId))
            .Concat(_lifecycles.Select(item => item.OwnerId))
            .Distinct()
            .ToArray();
        if (owners.Length > 1)
        {
            diagnostics.Add(new HostCompositionDiagnostic(
                "EXTENSION_OWNER_MISMATCH",
                string.Join(",", owners.Select(item => item.Value)),
                []));
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
            acceptedLifecycles);
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
            var pluginOwners = entries.Select(ownerSelector)
                .Where(owner => owner != HostExtensionIds.V2Owner)
                .Distinct()
                .ToArray();
            foreach (var owner in pluginOwners)
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

    /// <summary>全局模型映射冲突检查使用的最小 View 候选投影。</summary>
    private sealed record ViewDeclaration(
        PluginId OwnerId,
        Type ModelType,
        Type ViewType,
        Func<Control> ViewFactory);
}
