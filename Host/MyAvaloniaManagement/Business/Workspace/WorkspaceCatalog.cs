using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>
/// 把 Host 内建目录与当前可用插件 Registry 合并为 Workspace 的只读查询面。
/// </summary>
/// <remarks>
/// 合并只发生在描述、菜单和 View 映射层，不合并 Provider 所有权。Host 项始终可见；插件项
/// 每次查询都经过只读可用性投影。模型创建仍分别交给 HostWorkspaceActivator 与
/// PluginContributionActivator，避免目录演变为服务定位器。
/// </remarks>
internal sealed class WorkspaceCatalog
{
    private readonly HostWorkspaceCatalog _host;
    private readonly PluginRegistry _plugins;
    private readonly PluginAvailabilityReadModel _availability;

    internal WorkspaceCatalog(
        HostWorkspaceCatalog host,
        PluginRegistry plugins,
        PluginAvailabilityReadModel availability)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        ValidateMergedFacts();
    }

    internal IEnumerable<DocumentCreationMenuEntry> GetCreationEntries() =>
        _host.Documents.Cast<IWorkspaceDocumentRegistration>()
            .Concat(_plugins.Documents.Where(IsAvailable))
            .OrderBy(item => item.Descriptor.MenuCategory, StringComparer.Ordinal)
            .ThenBy(item => item.Descriptor.DisplayName, StringComparer.Ordinal)
            .ThenBy(item => item.Descriptor.DocumentTypeId.Value, StringComparer.Ordinal)
            .SelectMany(ToCreationEntries);

    internal bool TryGetDocument(
        DocumentTypeId id,
        out IWorkspaceDocumentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_host.TryGetDocument(id, out var host))
        {
            registration = host;
            return true;
        }
        if (_plugins.TryGetDocumentRegistration(id, out var plugin) && IsAvailable(plugin))
        {
            registration = plugin;
            return true;
        }
        registration = null!;
        return false;
    }

    internal bool TryGetHostDocument(
        DocumentTypeId id,
        out HostWorkspaceDocumentRegistration registration) =>
        _host.TryGetDocument(id, out registration!);

    internal bool TryGetAvailablePluginDocument(
        DocumentTypeId id,
        out PluginDocumentRegistration registration)
    {
        if (_plugins.TryGetDocumentRegistration(id, out registration) && IsAvailable(registration))
        {
            return true;
        }
        registration = null!;
        return false;
    }

    internal bool TryGetPersistablePluginDocument(
        DocumentTypeId id,
        out PluginDocumentRegistration registration) =>
        TryGetAvailablePluginDocument(id, out registration) && registration.IsPersistable;

    internal bool IsRegisteredTool(string toolId) =>
        ToolTypeId.TryParse(toolId, out var id) &&
        id is not null &&
        (_host.TryGetTool(id, out _) || _plugins.TryGetToolRegistration(id, out _));

    internal bool IsToolAvailable(string toolId) =>
        ToolTypeId.TryParse(toolId, out var id) &&
        id is not null &&
        TryGetTool(id, out _);

    internal bool TryGetTool(ToolTypeId id, out IWorkspaceToolRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_host.TryGetTool(id, out var host))
        {
            registration = host;
            return true;
        }
        if (_plugins.TryGetToolRegistration(id, out var plugin) && IsAvailable(plugin))
        {
            registration = plugin;
            return true;
        }
        registration = null!;
        return false;
    }

    internal bool TryGetHostTool(
        ToolTypeId id,
        out HostWorkspaceToolRegistration registration) =>
        _host.TryGetTool(id, out registration!);

    internal bool TryGetAvailablePluginTool(
        ToolTypeId id,
        out PluginToolRegistration registration)
    {
        if (_plugins.TryGetToolRegistration(id, out registration) && IsAvailable(registration))
        {
            return true;
        }
        registration = null!;
        return false;
    }

    internal IReadOnlyDictionary<ToolTypeId, ToolDescriptor> GetAvailableToolDescriptors() =>
        _host.Tools.Cast<IWorkspaceToolRegistration>()
            .Concat(_plugins.Tools.Where(IsAvailable))
            .ToDictionary(item => item.Descriptor.ToolTypeId, item => item.Descriptor);

    internal bool TryResolveToolTypeId(string value, out ToolTypeId? toolTypeId)
    {
        if (!ToolTypeId.TryParse(value, out var parsed) || parsed is null || !TryGetTool(parsed, out _))
        {
            toolTypeId = null;
            return false;
        }
        toolTypeId = parsed;
        return true;
    }

    internal bool TryGetView(Type modelType, out IWorkspaceViewRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        if (_host.TryGetView(modelType, out var host))
        {
            registration = host;
            return true;
        }
        if (_plugins.TryGetView(modelType, out var plugin) &&
            _availability.IsAvailable(plugin.OwnerId))
        {
            registration = new PluginWorkspaceViewRegistration(
                plugin.OwnerId, plugin.ModelType, plugin.ViewType, plugin.Factory);
            return true;
        }
        registration = null!;
        return false;
    }

    private bool IsAvailable(PluginDocumentRegistration registration) =>
        _availability.IsAvailable(registration.OwnerId);

    private bool IsAvailable(PluginToolRegistration registration) =>
        _availability.IsAvailable(registration.OwnerId);

    private static IEnumerable<DocumentCreationMenuEntry> ToCreationEntries(
        IWorkspaceDocumentRegistration registration)
    {
        var descriptor = registration.Descriptor;
        if (descriptor.CreationIntents.Count == 0)
        {
            yield return new DocumentCreationMenuEntry(
                descriptor.DocumentTypeId,
                null,
                descriptor.DisplayName,
                descriptor.Description,
                descriptor.IconPath,
                descriptor.MenuCategory);
            yield break;
        }
        foreach (var intent in descriptor.CreationIntents)
        {
            yield return new DocumentCreationMenuEntry(
                descriptor.DocumentTypeId,
                intent.IntentId,
                intent.DisplayName,
                string.IsNullOrWhiteSpace(intent.Description)
                    ? descriptor.Description
                    : intent.Description,
                string.IsNullOrWhiteSpace(intent.IconPath)
                    ? descriptor.IconPath
                    : intent.IconPath,
                descriptor.MenuCategory);
        }
    }

    private void ValidateMergedFacts()
    {
        // HostCatalog 与 PluginRegistry 分别在自身构造/发布阶段保证内部唯一；合并层只负责检查
        // 两个来源之间的碰撞，不重复实现插件 Builder 的诊断和隔离规则。
        var hostModels = _host.Documents.Select(item => item.ModelType)
            .Concat(_host.Tools.Select(item => item.ModelType))
            .ToHashSet();
        var pluginModels = _plugins.Documents.Select(item => item.ModelType)
            .Concat(_plugins.Tools.Select(item => item.ModelType));
        if (_host.Documents.Select(item => item.Descriptor.DocumentTypeId)
                .Intersect(_plugins.Documents.Select(item => item.Descriptor.DocumentTypeId)).Any() ||
            _host.Tools.Select(item => item.Descriptor.ToolTypeId)
                .Intersect(_plugins.Tools.Select(item => item.Descriptor.ToolTypeId)).Any() ||
            pluginModels.Any(hostModels.Contains))
        {
            throw new InvalidOperationException("Host Catalog 与 Plugin Registry 的只读合并事实存在冲突。");
        }
    }
}
