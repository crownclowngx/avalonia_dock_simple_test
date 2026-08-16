using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 经完整校验后一次性发布的宿主扩展组合快照。
/// </summary>
/// <remarks>
/// Registry 没有写入 API，也不保存生命周期运行状态或诊断日志。它只表示本次 HostRuntime 已经
/// 接受的清单和贡献，使菜单、Dock、View、生命周期与状态页读取同一份不可变事实。
/// </remarks>
internal sealed class PluginRegistry
{
    private readonly IReadOnlyDictionary<DocumentTypeId, PluginDocumentRegistration> _documents;
    private readonly IReadOnlyDictionary<ToolTypeId, PluginToolRegistration> _tools;
    private readonly IReadOnlyDictionary<Type, PluginViewRegistration> _views;
    private readonly IReadOnlyDictionary<DocumentTypeId, DocumentTypeId> _documentAliases;
    private readonly IReadOnlyDictionary<ToolTypeId, ToolTypeId> _toolAliases;

    internal PluginRegistry(
        IReadOnlyList<PluginRegistryPlugin> plugins,
        IReadOnlyList<PluginDocumentRegistration> documents,
        IReadOnlyList<PluginToolRegistration> tools,
        IReadOnlyList<PluginViewRegistration> views,
        IReadOnlyList<PluginLifecycleRegistration> lifecycles)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(lifecycles);

        var diagnostics = Validate(documents, tools, views);
        if (diagnostics.Count > 0)
        {
            throw new HostCompositionException(diagnostics);
        }

        Plugins = plugins.ToArray();
        Lifecycles = lifecycles.ToArray();
        _documents = documents.ToDictionary(item => item.Metadata.DocumentTypeId);
        _tools = tools.ToDictionary(item => item.Metadata.ToolTypeId);
        _views = views.ToDictionary(item => item.ViewModelType);
        _documentAliases = documents
            .SelectMany(item => item.Metadata.LegacyIds.Select(alias =>
                (Alias: alias, Canonical: item.Metadata.DocumentTypeId)))
            .ToDictionary(item => item.Alias, item => item.Canonical);
        _toolAliases = tools
            .SelectMany(item => item.Metadata.LegacyIds.Select(alias =>
                (Alias: alias, Canonical: item.Metadata.ToolTypeId)))
            .ToDictionary(item => item.Alias, item => item.Canonical);
    }

    /// <summary>测试使用的最小显式组合入口；所有测试贡献视为宿主所有。</summary>
    internal PluginRegistry(
        IEnumerable<IDocumentCreationStrategy> documentStrategies,
        IEnumerable<IToolCreationStrategy> toolStrategies)
        : this(
            [],
            documentStrategies.Select(strategy => new PluginDocumentRegistration(
                HostExtensionIds.Owner,
                strategy,
                strategy.GetMetadata(),
                strategy is IDocumentCreationIntentProvider provider
                    ? (provider.GetCreationIntents() ?? []).ToArray()
                    : [],
                strategy.GetType())).ToArray(),
            toolStrategies.Select(strategy => new PluginToolRegistration(
                HostExtensionIds.Owner,
                strategy,
                strategy.GetMetadata(),
                strategy.GetType())).ToArray(),
            [],
            [])
    {
    }

    internal IReadOnlyList<PluginRegistryPlugin> Plugins { get; }

    internal IReadOnlyList<PluginLifecycleRegistration> Lifecycles { get; }

    internal IReadOnlyDictionary<DocumentTypeId, DocumentMetadata> DocumentMetadata =>
        _documents.ToDictionary(item => item.Key, item => item.Value.Metadata);

    internal IReadOnlyDictionary<ToolTypeId, ToolMetadata> ToolMetadata =>
        _tools.ToDictionary(item => item.Key, item => item.Value.Metadata);

    internal bool TryGetView(Type viewModelType, out PluginViewRegistration registration) =>
        _views.TryGetValue(viewModelType, out registration!);

    internal bool TryGetToolStrategy(ToolTypeId toolTypeId, out IToolCreationStrategy strategy)
    {
        var canonical = ResolveToolTypeId(toolTypeId);
        if (_tools.TryGetValue(canonical, out var registration))
        {
            strategy = registration.Strategy;
            return true;
        }

        strategy = null!;
        return false;
    }

    internal DocumentTypeId ResolveDocumentTypeId(DocumentTypeId documentTypeId) =>
        _documentAliases.GetValueOrDefault(documentTypeId, documentTypeId);

    internal ToolTypeId ResolveToolTypeId(ToolTypeId toolTypeId) =>
        _toolAliases.GetValueOrDefault(toolTypeId, toolTypeId);

    internal bool TryResolveToolTypeId(string value, out ToolTypeId? toolTypeId)
    {
        if (!ToolTypeId.TryParse(value, out var parsed))
        {
            toolTypeId = null;
            return false;
        }

        var canonical = ResolveToolTypeId(parsed!);
        if (!_tools.ContainsKey(canonical))
        {
            toolTypeId = null;
            return false;
        }

        toolTypeId = canonical;
        return true;
    }

    internal Document CreateDocument(DocumentCreationParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var canonical = ResolveDocumentTypeId(parameters.DocumentTypeId);
        if (!_documents.TryGetValue(canonical, out var registration))
        {
            throw new NotSupportedException($"不支持的 Document 类型: {parameters.DocumentTypeId}");
        }

        var normalized = canonical == parameters.DocumentTypeId
            ? parameters
            : new DocumentCreationParams(canonical)
            {
                InitializationData = parameters.InitializationData,
                Title = parameters.Title,
                AdditionalData = parameters.AdditionalData,
                CreationIntentId = parameters.CreationIntentId,
            };
        return registration.Strategy.CreateDocument(normalized);
    }

    internal IEnumerable<DocumentCreationMenuEntry> GetCreationEntries()
    {
        foreach (var registration in _documents.Values)
        {
            var metadata = registration.Metadata;
            if (!metadata.ShowInMenu) continue;
            if (registration.Intents.Count == 0)
            {
                yield return ToMenuEntry(
                    metadata, null, metadata.DisplayName, metadata.Description, metadata.IconPath);
                continue;
            }

            foreach (var intent in registration.Intents)
            {
                yield return ToMenuEntry(
                    metadata,
                    intent.IntentId,
                    intent.DisplayName,
                    string.IsNullOrWhiteSpace(intent.Description)
                        ? metadata.Description
                        : intent.Description,
                    string.IsNullOrWhiteSpace(intent.IconPath)
                        ? metadata.IconPath
                        : intent.IconPath);
            }
        }
    }

    private static IReadOnlyList<HostCompositionDiagnostic> Validate(
        IReadOnlyList<PluginDocumentRegistration> documents,
        IReadOnlyList<PluginToolRegistration> tools,
        IReadOnlyList<PluginViewRegistration> views)
    {
        var diagnostics = new List<HostCompositionDiagnostic>();
        foreach (var document in documents)
        {
            var metadata = document.Metadata;
            if (!metadata.DocumentTypeId.IsCanonical ||
                !IsOwnedCanonical(metadata.DocumentTypeId.Value, document.OwnerId, "document"))
            {
                diagnostics.Add(Diagnostic(
                    "EXTENSION_OWNER_MISMATCH",
                    metadata.DocumentTypeId.Value,
                    document.StrategyType));
            }

            if (string.IsNullOrWhiteSpace(metadata.DisplayName) ||
                (metadata.ShowInMenu && string.IsNullOrWhiteSpace(metadata.MenuCategory)) ||
                metadata.LegacyIds.Any(alias => alias == metadata.DocumentTypeId) ||
                document.Intents.Any(intent =>
                    !intent.IntentId.IsCanonical || string.IsNullOrWhiteSpace(intent.DisplayName)))
            {
                diagnostics.Add(Diagnostic(
                    "EXTENSION_METADATA_INVALID",
                    metadata.DocumentTypeId.Value,
                    document.StrategyType));
            }

            foreach (var duplicateIntent in document.Intents
                         .GroupBy(intent => intent.IntentId)
                         .Where(group => group.Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    "CREATION_INTENT_ID_DUPLICATE",
                    $"{metadata.DocumentTypeId.Value}:{duplicateIntent.Key.Value}",
                    document.StrategyType));
            }
        }

        foreach (var tool in tools)
        {
            var metadata = tool.Metadata;
            if (!metadata.ToolTypeId.IsCanonical ||
                !IsOwnedCanonical(metadata.ToolTypeId.Value, tool.OwnerId, "tool"))
            {
                diagnostics.Add(Diagnostic(
                    "EXTENSION_OWNER_MISMATCH",
                    metadata.ToolTypeId.Value,
                    tool.StrategyType));
            }

            if (string.IsNullOrWhiteSpace(metadata.DisplayName) ||
                !Enum.IsDefined(metadata.DockSide) ||
                metadata.LegacyIds.Any(alias => alias == metadata.ToolTypeId))
            {
                diagnostics.Add(Diagnostic(
                    "EXTENSION_METADATA_INVALID",
                    metadata.ToolTypeId.Value,
                    tool.StrategyType));
            }
        }

        AddCollisionDiagnostics(
            documents.SelectMany(item =>
                new[] { (Id: item.Metadata.DocumentTypeId, Item: item, IsAlias: false) }
                    .Concat(item.Metadata.LegacyIds.Select(id => (id, item, true)))),
            "DOCUMENT_ID_DUPLICATE",
            "DOCUMENT_ID_ALIAS_DUPLICATE",
            item => item.StrategyType,
            diagnostics);
        AddCollisionDiagnostics(
            tools.SelectMany(item =>
                new[] { (Id: item.Metadata.ToolTypeId, Item: item, IsAlias: false) }
                    .Concat(item.Metadata.LegacyIds.Select(id => (id, item, true)))),
            "TOOL_ID_DUPLICATE",
            "TOOL_ID_ALIAS_DUPLICATE",
            item => item.StrategyType,
            diagnostics);

        foreach (var group in views.GroupBy(item => item.ViewModelType).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new HostCompositionDiagnostic(
                "VIEW_MODEL_REGISTRATION_DUPLICATE",
                group.Key.FullName,
                group.Select(item => ToContributor(item.ViewType)).Distinct().ToArray()));
        }

        return diagnostics;
    }

    private static void AddCollisionDiagnostics<TId, TItem>(
        IEnumerable<(TId Id, TItem Item, bool IsAlias)> identifiers,
        string primaryCode,
        string aliasCode,
        Func<TItem, Type> getType,
        ICollection<HostCompositionDiagnostic> diagnostics)
        where TId : notnull
    {
        foreach (var group in identifiers.GroupBy(item => item.Id).Where(group => group.Count() > 1))
        {
            var entries = group.ToArray();
            var primaryEntries = entries.Where(item => !item.IsAlias).ToArray();
            if (primaryEntries.Length > 1)
            {
                diagnostics.Add(new HostCompositionDiagnostic(
                    primaryCode,
                    group.Key.ToString(),
                    primaryEntries.Select(item => ToContributor(getType(item.Item)))
                        .Distinct().ToArray()));
            }

            if (entries.Any(item => item.IsAlias))
            {
                diagnostics.Add(new HostCompositionDiagnostic(
                    aliasCode,
                    group.Key.ToString(),
                    entries.Select(item => ToContributor(getType(item.Item)))
                        .Distinct().ToArray()));
            }
        }
    }

    private static bool IsOwnedCanonical(string value, PluginId ownerId, string kind) =>
        value.StartsWith($"{ownerId.Value}.{kind}.", StringComparison.Ordinal) &&
        value.All(character => !char.IsAsciiLetterUpper(character));

    private static DocumentCreationMenuEntry ToMenuEntry(
        DocumentMetadata metadata,
        CreationIntentId? intentId,
        string displayName,
        string description,
        string iconPath) =>
        new(metadata.DocumentTypeId, intentId, displayName, description, iconPath, metadata.MenuCategory);

    private static HostCompositionDiagnostic Diagnostic(string code, string? id, Type type) =>
        new(code, id, [ToContributor(type)]);

    private static HostCompositionContributor ToContributor(Type type) =>
        new(type.FullName ?? type.Name, type.Assembly.GetName().Name ?? "Unknown");
}

internal sealed record PluginRegistryPlugin(
    PluginManifest Manifest,
    Assembly EntryAssembly,
    Type ModuleType,
    IReadOnlyList<Type> DocumentTypes,
    IReadOnlyList<Type> ToolTypes,
    IReadOnlyList<PluginViewTypePair> Views,
    IReadOnlyList<Type> LifecycleTypes);

internal sealed record PluginViewTypePair(Type ViewModelType, Type ViewType);

internal sealed record PluginDocumentRegistration(
    PluginId OwnerId,
    IDocumentCreationStrategy Strategy,
    DocumentMetadata Metadata,
    IReadOnlyList<DocumentCreationIntentMetadata> Intents,
    Type StrategyType);

internal sealed record PluginToolRegistration(
    PluginId OwnerId,
    IToolCreationStrategy Strategy,
    ToolMetadata Metadata,
    Type StrategyType);

internal sealed record PluginViewRegistration(
    PluginId OwnerId,
    Type ViewModelType,
    Type ViewType,
    Func<Control> Factory);
