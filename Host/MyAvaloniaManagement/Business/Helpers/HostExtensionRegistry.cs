using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 统一登记宿主及插件的文档、工具创建策略与元数据。
/// 将发现、激活和首次注册胜出规则移出 Dock 工厂，使工厂只负责布局适配和创建委托。
/// </summary>
internal sealed class HostExtensionRegistry
{
    private readonly Dictionary<string, IDocumentCreationStrategy> _documentStrategies =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, IToolCreationStrategy> _toolStrategies =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, DocumentMetadata> _documentMetadata =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToolMetadata> _toolMetadata =
        new(StringComparer.Ordinal);

    internal HostExtensionRegistry(
        IServiceProvider serviceProvider,
        PluginModuleCatalog pluginModuleCatalog)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(pluginModuleCatalog);

        var hostAssembly = Assembly.GetExecutingAssembly();
        var assemblies = new[] { hostAssembly }
            .Concat(AssemblyLoaderHelper.LoadPluginsFromDirectories(
                AssemblyLoadConstant.PLUGINS_SUBDIRECTORY))
            .Concat(pluginModuleCatalog.DiscoveredAssemblies)
            .Distinct()
            .ToArray();

        foreach (var assembly in assemblies)
        {
            var types = AssemblyTypeCatalog.GetLoadableTypes(
                assembly,
                exception => Report(
                    "STRATEGY_TYPE_SCAN_PARTIAL",
                    assembly.FullName,
                    exception));

            foreach (var type in types)
            {
                DiscoverDocumentStrategy(
                    type,
                    assembly,
                    serviceProvider,
                    pluginModuleCatalog);
                DiscoverToolStrategy(
                    type,
                    assembly,
                    hostAssembly,
                    serviceProvider,
                    pluginModuleCatalog);
            }
        }
    }

    internal IReadOnlyDictionary<string, DocumentMetadata> DocumentMetadata =>
        _documentMetadata;

    internal IReadOnlyDictionary<string, ToolMetadata> ToolMetadata =>
        _toolMetadata;

    internal IEnumerable<IToolCreationStrategy> ToolStrategies =>
        _toolStrategies.Values;

    internal bool TryGetToolStrategy(
        string toolTypeId,
        out IToolCreationStrategy strategy) =>
        _toolStrategies.TryGetValue(toolTypeId, out strategy!);

    internal void RegisterDocumentStrategy(IDocumentCreationStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        var metadata = strategy.GetMetadata();
        ArgumentNullException.ThrowIfNull(metadata);

        if (_documentStrategies.TryAdd(metadata.DocumentTypeId, strategy))
        {
            _documentMetadata.Add(metadata.DocumentTypeId, metadata);
        }
    }

    internal void RegisterToolStrategy(IToolCreationStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        var metadata = strategy.GetMetadata();
        ArgumentNullException.ThrowIfNull(metadata);

        if (_toolStrategies.TryAdd(metadata.ToolTypeId, strategy))
        {
            _toolMetadata.Add(metadata.ToolTypeId, metadata);
        }
    }

    internal Document CreateDocument(DocumentCreationParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (_documentStrategies.TryGetValue(
                parameters.DocumentType,
                out var strategy))
        {
            return strategy.CreateDocument(parameters);
        }

        throw new NotSupportedException(
            $"不支持的Document类型: {parameters.DocumentType}");
    }

    internal IEnumerable<DocumentCreationMenuEntry> GetCreationEntries()
    {
        foreach (var (documentTypeId, metadata) in _documentMetadata)
        {
            if (!metadata.ShowInMenu ||
                !_documentStrategies.TryGetValue(documentTypeId, out var strategy))
            {
                continue;
            }

            if (strategy is not IDocumentCreationIntentProvider intentProvider)
            {
                yield return ToMenuEntry(
                    metadata,
                    string.Empty,
                    metadata.DisplayName,
                    metadata.Description,
                    metadata.IconPath);
                continue;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var intent in intentProvider.GetCreationIntents())
            {
                if (!seen.Add(intent.IntentId))
                {
                    throw new InvalidOperationException(
                        $"文档 {documentTypeId} 包含重复创建意图: {intent.IntentId}");
                }

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

    private void DiscoverDocumentStrategy(
        Type type,
        Assembly assembly,
        IServiceProvider serviceProvider,
        PluginModuleCatalog pluginModuleCatalog)
    {
        if (!typeof(IDocumentCreationStrategy).IsAssignableFrom(type) ||
            type.IsAbstract ||
            type.IsInterface ||
            (!pluginModuleCatalog.IsManaged(assembly) &&
             type.GetConstructor(Type.EmptyTypes) is null))
        {
            return;
        }

        try
        {
            RegisterDocumentStrategy(
                PluginStrategyActivator.Create<IDocumentCreationStrategy>(
                    type,
                    assembly,
                    serviceProvider,
                    pluginModuleCatalog));
        }
        catch (Exception exception)
        {
            Report("DOCUMENT_STRATEGY_ACTIVATION_FAILED", type.FullName, exception);
        }
    }

    private void DiscoverToolStrategy(
        Type type,
        Assembly assembly,
        Assembly hostAssembly,
        IServiceProvider serviceProvider,
        PluginModuleCatalog pluginModuleCatalog)
    {
        var isHostAssembly = assembly == hostAssembly;
        if (!typeof(IToolCreationStrategy).IsAssignableFrom(type) ||
            type.IsAbstract ||
            type.IsInterface ||
            (!isHostAssembly &&
             !pluginModuleCatalog.IsManaged(assembly) &&
             type.GetConstructor(Type.EmptyTypes) is null))
        {
            return;
        }

        try
        {
            var strategy = isHostAssembly
                ? (IToolCreationStrategy)ActivatorUtilities.CreateInstance(
                    serviceProvider,
                    type)
                : PluginStrategyActivator.Create<IToolCreationStrategy>(
                    type,
                    assembly,
                    serviceProvider,
                    pluginModuleCatalog);
            RegisterToolStrategy(strategy);
        }
        catch (Exception exception)
        {
            Report("TOOL_STRATEGY_ACTIVATION_FAILED", type.FullName, exception);
        }
    }

    private static DocumentCreationMenuEntry ToMenuEntry(
        DocumentMetadata metadata,
        string intentId,
        string displayName,
        string description,
        string iconPath) =>
        new(
            metadata.DocumentTypeId,
            intentId,
            displayName,
            description,
            iconPath,
            metadata.MenuCategory);

    private static void Report(
        string errorCode,
        string? stableId,
        Exception exception) =>
        Console.Error.WriteLine(
            $"HostExtensionRegistry errorCode={errorCode} stableId={stableId ?? "-"} type={exception.GetType().Name}");
}
