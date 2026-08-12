using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.Save;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 经完整校验后发布的宿主扩展只读注册表。
/// </summary>
/// <remarks>
/// 构造过程先收集全部候选贡献，再统一校验主 ID、别名、所有权和创建意图，最后一次性提交。
/// 任何失败都不会留下“第一项已注册、第二项被忽略”的半成品状态。
/// </remarks>
internal sealed class HostExtensionRegistry
{
    private IReadOnlyDictionary<DocumentTypeId, DocumentRegistration> _documents = null!;
    private IReadOnlyDictionary<ToolTypeId, ToolRegistration> _tools = null!;
    private IReadOnlyDictionary<DocumentTypeId, DocumentTypeId> _documentAliases = null!;
    private IReadOnlyDictionary<ToolTypeId, ToolTypeId> _toolAliases = null!;

    internal HostExtensionRegistry(
        IServiceProvider serviceProvider,
        PluginModuleCatalog pluginModuleCatalog,
        IEnumerable<IDocumentCreationStrategy> additionalDocumentStrategies,
        IEnumerable<IToolCreationStrategy> additionalToolStrategies)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(pluginModuleCatalog);

        var hostAssembly = Assembly.GetExecutingAssembly();
        var assemblies = new[] { hostAssembly }
            .Concat(pluginModuleCatalog.DiscoveredAssemblies)
            .Distinct()
            .ToArray();
        var documents = new List<DocumentRegistration>();
        var tools = new List<ToolRegistration>();
        var discoveryDiagnostics = new List<HostCompositionDiagnostic>();

        foreach (var assembly in assemblies)
        {
            var ownerId = ResolveOwnerId(assembly, hostAssembly, pluginModuleCatalog);
            var types = AssemblyTypeCatalog.GetLoadableTypes(
                assembly,
                exception => Report("STRATEGY_TYPE_SCAN_PARTIAL", assembly.FullName, exception));
            foreach (var type in types)
            {
                DiscoverDocumentStrategy(
                    type, assembly, ownerId, serviceProvider, pluginModuleCatalog, documents, discoveryDiagnostics);
                DiscoverToolStrategy(
                    type, assembly, hostAssembly, ownerId, serviceProvider, pluginModuleCatalog, tools, discoveryDiagnostics);
            }
        }

        // 显式贡献仅供宿主组合根和测试使用；生产插件仍由程序集发现，避免运行期后注册。
        foreach (var strategy in additionalDocumentStrategies)
        {
            var metadata = strategy.GetMetadata();
            var intents = strategy is IDocumentCreationIntentProvider provider
                ? (provider.GetCreationIntents() ?? []).ToArray()
                : [];
            documents.Add(new DocumentRegistration(
                strategy, metadata, intents, strategy.GetType(), HostExtensionIds.Owner));
        }

        foreach (var strategy in additionalToolStrategies)
        {
            tools.Add(new ToolRegistration(
                strategy, strategy.GetMetadata(), strategy.GetType(), HostExtensionIds.Owner));
        }

        Commit(documents, tools, discoveryDiagnostics);
    }

    /// <summary>
    /// 测试组合根使用的显式 Builder 入口。生产插件仍只通过程序集发现接入。
    /// </summary>
    internal HostExtensionRegistry(
        IEnumerable<IDocumentCreationStrategy> documentStrategies,
        IEnumerable<IToolCreationStrategy> toolStrategies)
    {
        ArgumentNullException.ThrowIfNull(documentStrategies);
        ArgumentNullException.ThrowIfNull(toolStrategies);
        var documents = documentStrategies.Select(strategy =>
        {
            var metadata = strategy.GetMetadata();
            var intents = strategy is IDocumentCreationIntentProvider provider
                ? (provider.GetCreationIntents() ?? []).ToArray()
                : [];
            return new DocumentRegistration(
                strategy,
                metadata,
                intents,
                strategy.GetType(),
                HostExtensionIds.Owner);
        }).ToArray();
        var tools = toolStrategies.Select(strategy => new ToolRegistration(
            strategy,
            strategy.GetMetadata(),
            strategy.GetType(),
            HostExtensionIds.Owner)).ToArray();
        Commit(documents, tools);
    }

    private void Commit(
        IReadOnlyList<DocumentRegistration> documents,
        IReadOnlyList<ToolRegistration> tools,
        IReadOnlyList<HostCompositionDiagnostic>? discoveryDiagnostics = null)
    {
        var diagnostics = (discoveryDiagnostics ?? [])
            .Concat(Validate(documents, tools))
            .ToArray();
        if (diagnostics.Length > 0) throw new HostCompositionException(diagnostics);
        _documents = documents.ToDictionary(item => item.Metadata.DocumentTypeId);
        _tools = tools.ToDictionary(item => item.Metadata.ToolTypeId);
        _documentAliases = documents
            .SelectMany(item => item.Metadata.LegacyIds.Select(alias =>
                (Alias: alias, Canonical: item.Metadata.DocumentTypeId)))
            .ToDictionary(item => item.Alias, item => item.Canonical);
        _toolAliases = tools
            .SelectMany(item => item.Metadata.LegacyIds.Select(alias =>
                (Alias: alias, Canonical: item.Metadata.ToolTypeId)))
            .ToDictionary(item => item.Alias, item => item.Canonical);
    }

    internal IReadOnlyDictionary<DocumentTypeId, DocumentMetadata> DocumentMetadata =>
        _documents.ToDictionary(item => item.Key, item => item.Value.Metadata);

    internal IReadOnlyDictionary<ToolTypeId, ToolMetadata> ToolMetadata =>
        _tools.ToDictionary(item => item.Key, item => item.Value.Metadata);

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
        var document = registration.Strategy.CreateDocument(normalized);
        if (document is ISavableDocument savable && savable.SaveDocumentTypeId != canonical)
        {
            throw new HostCompositionException([
                new HostCompositionDiagnostic(
                    "DOCUMENT_TYPE_MISMATCH",
                    canonical.Value,
                    [ToContributor(registration.StrategyType)])
            ]);
        }

        return document;
    }

    internal IEnumerable<DocumentCreationMenuEntry> GetCreationEntries()
    {
        foreach (var registration in _documents.Values)
        {
            var metadata = registration.Metadata;
            if (!metadata.ShowInMenu) continue;
            if (registration.Intents.Count == 0)
            {
                yield return ToMenuEntry(metadata, null, metadata.DisplayName, metadata.Description, metadata.IconPath);
                continue;
            }

            foreach (var intent in registration.Intents)
            {
                yield return ToMenuEntry(
                    metadata,
                    intent.IntentId,
                    intent.DisplayName,
                    string.IsNullOrWhiteSpace(intent.Description) ? metadata.Description : intent.Description,
                    string.IsNullOrWhiteSpace(intent.IconPath) ? metadata.IconPath : intent.IconPath);
            }
        }
    }

    private static IReadOnlyList<HostCompositionDiagnostic> Validate(
        IReadOnlyList<DocumentRegistration> documents,
        IReadOnlyList<ToolRegistration> tools)
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
                diagnostics.Add(Diagnostic("EXTENSION_METADATA_INVALID", metadata.DocumentTypeId.Value, document.StrategyType));
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
                diagnostics.Add(Diagnostic("EXTENSION_METADATA_INVALID", metadata.ToolTypeId.Value, tool.StrategyType));
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
                    primaryEntries
                        .Select(item => ToContributor(getType(item.Item)))
                        .Distinct()
                        .ToArray()));
            }

            if (entries.Any(item => item.IsAlias))
            {
                diagnostics.Add(new HostCompositionDiagnostic(
                    aliasCode,
                    group.Key.ToString(),
                    entries
                        .Select(item => ToContributor(getType(item.Item)))
                        .Distinct()
                        .ToArray()));
            }
        }
    }

    private static bool IsOwnedCanonical(string value, PluginId ownerId, string kind) =>
        value.StartsWith($"{ownerId.Value}.{kind}.", StringComparison.Ordinal) &&
        value.All(character => !char.IsAsciiLetterUpper(character));

    private static PluginId ResolveOwnerId(
        Assembly assembly,
        Assembly hostAssembly,
        PluginModuleCatalog catalog)
    {
        if (assembly == hostAssembly) return HostExtensionIds.Owner;
        if (catalog.TryGetPluginId(assembly, out var pluginId)) return pluginId;
        var assemblyName = assembly.GetName().Name ?? "extension";
        var slug = string.Concat(assemblyName.Select(character =>
                char.IsAsciiLetterOrDigit(character)
                    ? char.ToLowerInvariant(character)
                    : '-'))
            .Trim('-');
        return new PluginId($"myavalonia.legacy.{(slug.Length == 0 ? "extension" : slug)}");
    }

    private static void DiscoverDocumentStrategy(
        Type type,
        Assembly assembly,
        PluginId ownerId,
        IServiceProvider serviceProvider,
        PluginModuleCatalog catalog,
        ICollection<DocumentRegistration> registrations,
        ICollection<HostCompositionDiagnostic> diagnostics)
    {
        if (!typeof(IDocumentCreationStrategy).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface ||
            (!catalog.IsManaged(assembly) && type.GetConstructor(Type.EmptyTypes) is null)) return;
        try
        {
            var strategy = PluginStrategyActivator.Create<IDocumentCreationStrategy>(
                type, assembly, serviceProvider, catalog);
            var metadata = strategy.GetMetadata() ?? throw new InvalidOperationException("Document 元数据不能为空。");
            var intents = strategy is IDocumentCreationIntentProvider provider
                ? (provider.GetCreationIntents() ?? []).ToArray()
                : [];
            registrations.Add(new DocumentRegistration(strategy, metadata, intents, type, ownerId));
        }
        catch (Exception)
        {
            diagnostics.Add(Diagnostic(
                "EXTENSION_ACTIVATION_FAILED",
                null,
                type));
        }
    }

    private static void DiscoverToolStrategy(
        Type type,
        Assembly assembly,
        Assembly hostAssembly,
        PluginId ownerId,
        IServiceProvider serviceProvider,
        PluginModuleCatalog catalog,
        ICollection<ToolRegistration> registrations,
        ICollection<HostCompositionDiagnostic> diagnostics)
    {
        var isHost = assembly == hostAssembly;
        if (!typeof(IToolCreationStrategy).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface ||
            (!isHost && !catalog.IsManaged(assembly) && type.GetConstructor(Type.EmptyTypes) is null)) return;
        try
        {
            var strategy = isHost
                ? (IToolCreationStrategy)ActivatorUtilities.CreateInstance(serviceProvider, type)
                : PluginStrategyActivator.Create<IToolCreationStrategy>(type, assembly, serviceProvider, catalog);
            var metadata = strategy.GetMetadata() ?? throw new InvalidOperationException("Tool 元数据不能为空。");
            registrations.Add(new ToolRegistration(strategy, metadata, type, ownerId));
        }
        catch (Exception)
        {
            diagnostics.Add(Diagnostic(
                "EXTENSION_ACTIVATION_FAILED",
                null,
                type));
        }
    }

    private static DocumentCreationMenuEntry ToMenuEntry(
        DocumentMetadata metadata,
        CreationIntentId? intentId,
        string displayName,
        string description,
        string iconPath) =>
        new(metadata.DocumentTypeId, intentId, displayName, description, iconPath, metadata.MenuCategory);

    private static HostCompositionDiagnostic Diagnostic(string code, string? stableId, Type type) =>
        new(code, stableId, [ToContributor(type)]);

    private static HostCompositionContributor ToContributor(Type type) =>
        new(type.FullName ?? type.Name, type.Assembly.GetName().Name ?? type.Assembly.FullName ?? "Unknown");

    private static void Report(string errorCode, string? stableId, Exception exception) =>
        Console.Error.WriteLine(
            $"HostExtensionRegistry errorCode={errorCode} stableId={stableId ?? "-"} type={exception.GetType().Name}");

    private sealed record DocumentRegistration(
        IDocumentCreationStrategy Strategy,
        DocumentMetadata Metadata,
        IReadOnlyList<DocumentCreationIntentMetadata> Intents,
        Type StrategyType,
        PluginId OwnerId);

    private sealed record ToolRegistration(
        IToolCreationStrategy Strategy,
        ToolMetadata Metadata,
        Type StrategyType,
        PluginId OwnerId);
}
