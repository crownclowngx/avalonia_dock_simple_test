using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
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
        IEnumerable<IToolCreationStrategy> additionalToolStrategies,
        IHostDiagnosticSink? diagnosticSink = null)
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
            // 插件程序集复用启动预检形成的不可变类型集合；宿主自身程序集不属于插件候选，
            // 仍使用兼容扫描。这样不会在服务注册后重新得到一套不同的插件类型结果。
            var types = assembly == hostAssembly
                ? AssemblyTypeCatalog.GetLoadableTypes(
                    assembly,
                    exception => Report(
                        diagnosticSink,
                        "STRATEGY_TYPE_SCAN_PARTIAL",
                        assembly.FullName,
                        exception))
                : pluginModuleCatalog.GetDiscoveryTypes(assembly);
            foreach (var type in types)
            {
                DiscoverDocumentStrategy(
                    type, ownerId, serviceProvider, documents, discoveryDiagnostics, diagnosticSink);
                DiscoverToolStrategy(
                    type, ownerId, serviceProvider, tools, discoveryDiagnostics, diagnosticSink);
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
        return catalog.GetRequiredPluginId(assembly);
    }

    private static void DiscoverDocumentStrategy(
        Type type,
        PluginId ownerId,
        IServiceProvider serviceProvider,
        ICollection<DocumentRegistration> registrations,
        ICollection<HostCompositionDiagnostic> diagnostics,
        IHostDiagnosticSink? diagnosticSink)
    {
        if (!typeof(IDocumentCreationStrategy).IsAssignableFrom(type) ||
            type.IsAbstract ||
            type.IsInterface) return;
        try
        {
            // Host 与插件策略现在共享唯一的 DI 激活语义。策略可以只声明其真实依赖，
            // 不需要为旧二进制加载路径保留一个无业务意义的 public 无参构造。
            var strategy = (IDocumentCreationStrategy)ActivatorUtilities.CreateInstance(
                serviceProvider,
                type);
            var metadata = strategy.GetMetadata() ?? throw new InvalidOperationException("Document 元数据不能为空。");
            var intents = strategy is IDocumentCreationIntentProvider provider
                ? (provider.GetCreationIntents() ?? []).ToArray()
                : [];
            registrations.Add(new DocumentRegistration(strategy, metadata, intents, type, ownerId));
        }
        catch (Exception exception)
        {
            ReportActivationFailure(diagnosticSink, ownerId, type, exception);
            diagnostics.Add(Diagnostic(
                "EXTENSION_ACTIVATION_FAILED",
                null,
                type));
        }
    }

    private static void DiscoverToolStrategy(
        Type type,
        PluginId ownerId,
        IServiceProvider serviceProvider,
        ICollection<ToolRegistration> registrations,
        ICollection<HostCompositionDiagnostic> diagnostics,
        IHostDiagnosticSink? diagnosticSink)
    {
        if (!typeof(IToolCreationStrategy).IsAssignableFrom(type) ||
            type.IsAbstract ||
            type.IsInterface) return;
        try
        {
            var strategy = (IToolCreationStrategy)ActivatorUtilities.CreateInstance(
                serviceProvider,
                type);
            var metadata = strategy.GetMetadata() ?? throw new InvalidOperationException("Tool 元数据不能为空。");
            registrations.Add(new ToolRegistration(strategy, metadata, type, ownerId));
        }
        catch (Exception exception)
        {
            ReportActivationFailure(diagnosticSink, ownerId, type, exception);
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

    private static void Report(
        IHostDiagnosticSink? diagnosticSink,
        string errorCode,
        string? stableId,
        Exception exception)
    {
        if (diagnosticSink is null)
        {
            Console.Error.WriteLine(
                $"HostExtensionRegistry errorCode={errorCode} stableId={stableId ?? "-"} type={exception.GetType().Name}");
            return;
        }

        diagnosticSink.Report(new HostDiagnosticDraft(
            errorCode,
            HostDiagnosticPhase.ExtensionDiscovery,
            "宿主扩展类型扫描失败。")
        {
            StableId = stableId,
            Exception = exception,
        });
    }

    private static void ReportActivationFailure(
        IHostDiagnosticSink? diagnosticSink,
        PluginId ownerId,
        Type type,
        Exception exception)
    {
        diagnosticSink?.Report(new HostDiagnosticDraft(
            HostDiagnosticCodes.ExtensionActivationFailed,
            HostDiagnosticPhase.ExtensionDiscovery,
            "扩展策略激活或元数据读取失败。")
        {
            PluginId = ownerId.Value,
            AssemblyName = type.Assembly.GetName().Name,
            StableId = type.FullName,
            Exception = exception,
        });
    }

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
