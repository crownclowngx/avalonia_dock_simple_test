using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 在组合阶段收集贡献声明，并在 DI 容器可用后一次性构建不可变 Registry。
/// </summary>
/// <remarks>
/// Builder 有意不提供删除或覆盖操作。每个插件先写入自己的临时 Builder，只有对应私有 Provider
/// 构建成功后才合并到本对象；最终再统一激活、验证并发布不可变 Registry。
/// </remarks>
internal sealed class PluginRegistryBuilder
{
    private readonly List<StrategyDeclaration> _documents = [];
    private readonly List<StrategyDeclaration> _tools = [];
    private readonly List<ViewDeclaration> _views = [];
    private readonly List<StrategyDeclaration> _lifecycles = [];
    private bool _built;

    internal void AddDocument(PluginId ownerId, Type strategyType) =>
        Add(_documents, ownerId, strategyType);

    internal void AddTool(PluginId ownerId, Type strategyType) =>
        Add(_tools, ownerId, strategyType);

    internal void AddLifecycle(PluginId ownerId, Type lifecycleType) =>
        Add(_lifecycles, ownerId, lifecycleType);

    /// <summary>
    /// 在一个插件的独立 Provider 已完整构建并通过宿主可见服务激活后，原子合并该插件的贡献声明。
    /// </summary>
    /// <remarks>
    /// 临时 Builder 只保存普通内存声明，不触碰全局 Registry。模块配置或 Provider 构建失败时直接
    /// 丢弃临时 Builder，成功时才一次追加，从而避免借助 IServiceCollection 事务实现失败回滚。
    /// </remarks>
    internal void Import(PluginRegistryBuilder source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureWritable();
        _documents.AddRange(source._documents);
        _tools.AddRange(source._tools);
        _views.AddRange(source._views);
        _lifecycles.AddRange(source._lifecycles);
    }

    /// <summary>返回当前插件在发布前必须能够由其私有 Provider 激活的具体服务类型。</summary>
    internal IReadOnlyList<Type> GetRequiredServiceTypes() =>
        _documents.Concat(_tools).Concat(_lifecycles)
            .Where(item => item.Instance is null && item.Factory is null)
            .Select(item => item.ImplementationType)
            .Distinct()
            .ToArray();

    /// <summary>
    /// 允许测试组合根登记已经创建的策略替身；生产模块没有此入口。
    /// </summary>
    internal void AddDocumentInstance(
        PluginId ownerId,
        IDocumentCreationStrategy strategy)
    {
        EnsureWritable();
        _documents.Add(new StrategyDeclaration(ownerId, strategy.GetType(), strategy, null));
    }

    /// <summary>允许测试组合根登记已经创建的 Tool 策略替身。</summary>
    internal void AddToolInstance(PluginId ownerId, IToolCreationStrategy strategy)
    {
        EnsureWritable();
        _tools.Add(new StrategyDeclaration(ownerId, strategy.GetType(), strategy, null));
    }

    internal void AddDocumentFactoryForTests(
        PluginId ownerId,
        Type contributorType,
        Func<IServiceProvider, IDocumentCreationStrategy> factory)
    {
        EnsureWritable();
        _documents.Add(new StrategyDeclaration(
            ownerId, contributorType, null, provider => factory(provider)));
    }

    internal void AddToolFactoryForTests(
        PluginId ownerId,
        Type contributorType,
        Func<IServiceProvider, IToolCreationStrategy> factory)
    {
        EnsureWritable();
        _tools.Add(new StrategyDeclaration(
            ownerId, contributorType, null, provider => factory(provider)));
    }

    internal void AddView(
        PluginId ownerId,
        Type viewModelType,
        Type viewType,
        Func<Control> factory)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(viewModelType);
        ArgumentNullException.ThrowIfNull(viewType);
        ArgumentNullException.ThrowIfNull(factory);
        _views.Add(new ViewDeclaration(ownerId, viewModelType, viewType, factory));
    }

    internal PluginRegistry Build(
        IServiceProvider serviceProvider,
        PluginModuleCatalog? catalog,
        IHostDiagnosticSink? diagnosticSink = null,
        PluginProviderOwner? pluginProviders = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        EnsureWritable();
        _built = true;

        var diagnostics = ValidateDeclarations();
        var documents = ActivateDocuments(
            serviceProvider, pluginProviders, diagnostics, diagnosticSink);
        var tools = ActivateTools(
            serviceProvider, pluginProviders, diagnostics, diagnosticSink);
        var lifecycles = ActivateLifecycles(
            serviceProvider, pluginProviders, diagnostics, diagnosticSink);

        if (diagnostics.Count > 0)
        {
            throw new HostCompositionException(diagnostics);
        }

        return new PluginRegistry(
            catalog?.CreatePluginSnapshots(
                _documents,
                _tools,
                _views,
                _lifecycles,
                pluginProviders?.AvailablePluginIds) ?? [],
            documents,
            tools,
            _views.Select(view => new PluginViewRegistration(
                view.OwnerId,
                view.ViewModelType,
                view.ViewType,
                view.Factory)).ToArray(),
            lifecycles);
    }

    private List<HostCompositionDiagnostic> ValidateDeclarations()
    {
        var diagnostics = new List<HostCompositionDiagnostic>();
        AddInvalidTypeDiagnostics(_documents, diagnostics);
        AddInvalidTypeDiagnostics(_tools, diagnostics);
        AddInvalidTypeDiagnostics(_lifecycles, diagnostics);
        AddDuplicateTypeDiagnostics(_documents, "DOCUMENT_CONTRIBUTION_TYPE_DUPLICATE", diagnostics);
        AddDuplicateTypeDiagnostics(_tools, "TOOL_CONTRIBUTION_TYPE_DUPLICATE", diagnostics);
        AddDuplicateTypeDiagnostics(_lifecycles, "LIFECYCLE_CONTRIBUTION_TYPE_DUPLICATE", diagnostics);

        foreach (var group in _views.GroupBy(item => item.ViewModelType).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new HostCompositionDiagnostic(
                "VIEW_MODEL_REGISTRATION_DUPLICATE",
                group.Key.FullName,
                group.Select(item => ToContributor(item.ViewType)).Distinct().ToArray()));
        }

        foreach (var group in _lifecycles.GroupBy(item => item.OwnerId).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new HostCompositionDiagnostic(
                "LIFECYCLE_PLUGIN_ID_DUPLICATE",
                group.Key.Value,
                group.Select(item => ToContributor(item.ImplementationType)).Distinct().ToArray()));
        }

        return diagnostics;
    }

    /// <summary>
    /// 在请求 DI 激活前拒绝不能形成实例的声明，避免容器实现的异常文字成为插件契约。
    /// </summary>
    /// <remarks>
    /// Generic Context 已在源码编译期排除大部分错误，但反射调用、动态生成程序集和抽象贡献仍可能
    /// 绕过普通 C# 调用点。这里给它们统一稳定码，确保错误在 Registry 发布前可审阅地失败。
    /// </remarks>
    private static void AddInvalidTypeDiagnostics(
        IEnumerable<StrategyDeclaration> declarations,
        ICollection<HostCompositionDiagnostic> diagnostics)
    {
        foreach (var declaration in declarations.Where(item =>
                     item.Instance is null &&
                     item.Factory is null &&
                     (item.ImplementationType.IsAbstract ||
                      item.ImplementationType.IsInterface ||
                      item.ImplementationType.ContainsGenericParameters ||
                      item.ImplementationType.GetConstructors().Length == 0)))
        {
            diagnostics.Add(Diagnostic(
                "CONTRIBUTION_TYPE_INVALID",
                declaration.OwnerId.Value,
                declaration.ImplementationType));
        }
    }

    private IReadOnlyList<PluginDocumentRegistration> ActivateDocuments(
        IServiceProvider hostProvider,
        PluginProviderOwner? pluginProviders,
        ICollection<HostCompositionDiagnostic> diagnostics,
        IHostDiagnosticSink? sink) =>
        _documents.Select(declaration =>
        {
            try
            {
                var strategy = declaration.Instance as IDocumentCreationStrategy ??
                    declaration.Factory?.Invoke(hostProvider) as IDocumentCreationStrategy ??
                    (IDocumentCreationStrategy)Resolve(
                        declaration, hostProvider, pluginProviders);
                var metadata = strategy.GetMetadata() ??
                    throw new InvalidOperationException("Document 元数据不能为空。");
                var intents = strategy is IDocumentCreationIntentProvider intentProvider
                    ? (intentProvider.GetCreationIntents() ?? []).ToArray()
                    : [];
                return new PluginDocumentRegistration(
                    declaration.OwnerId,
                    strategy,
                    metadata,
                    intents,
                    declaration.ImplementationType);
            }
            catch (Exception exception)
            {
                ReportActivationFailure(sink, declaration, exception);
                diagnostics.Add(Diagnostic(
                    "EXTENSION_ACTIVATION_FAILED",
                    declaration.OwnerId.Value,
                    declaration.ImplementationType));
                return null;
            }
        }).Where(item => item is not null).Select(item => item!).ToArray();

    private IReadOnlyList<PluginToolRegistration> ActivateTools(
        IServiceProvider hostProvider,
        PluginProviderOwner? pluginProviders,
        ICollection<HostCompositionDiagnostic> diagnostics,
        IHostDiagnosticSink? sink) =>
        _tools.Select(declaration =>
        {
            try
            {
                var strategy = declaration.Instance as IToolCreationStrategy ??
                    declaration.Factory?.Invoke(hostProvider) as IToolCreationStrategy ??
                    (IToolCreationStrategy)Resolve(
                        declaration, hostProvider, pluginProviders);
                var metadata = strategy.GetMetadata() ??
                    throw new InvalidOperationException("Tool 元数据不能为空。");
                return new PluginToolRegistration(
                    declaration.OwnerId,
                    strategy,
                    metadata,
                    declaration.ImplementationType);
            }
            catch (Exception exception)
            {
                ReportActivationFailure(sink, declaration, exception);
                diagnostics.Add(Diagnostic(
                    "EXTENSION_ACTIVATION_FAILED",
                    declaration.OwnerId.Value,
                    declaration.ImplementationType));
                return null;
            }
        }).Where(item => item is not null).Select(item => item!).ToArray();

    private IReadOnlyList<PluginLifecycleRegistration> ActivateLifecycles(
        IServiceProvider hostProvider,
        PluginProviderOwner? pluginProviders,
        ICollection<HostCompositionDiagnostic> diagnostics,
        IHostDiagnosticSink? sink) =>
        _lifecycles.Select(declaration =>
        {
            try
            {
                return new PluginLifecycleRegistration(
                    declaration.OwnerId,
                    (IPluginLifecycle)Resolve(
                        declaration, hostProvider, pluginProviders));
            }
            catch (Exception exception)
            {
                ReportActivationFailure(sink, declaration, exception);
                diagnostics.Add(Diagnostic(
                    "EXTENSION_ACTIVATION_FAILED",
                    declaration.OwnerId.Value,
                    declaration.ImplementationType));
                return null;
            }
        }).Where(item => item is not null).Select(item => item!).ToArray();

    private static object Resolve(
        StrategyDeclaration declaration,
        IServiceProvider hostProvider,
        PluginProviderOwner? pluginProviders) =>
        pluginProviders is not null && declaration.OwnerId != Business.Constants.HostExtensionIds.Owner
            ? pluginProviders.GetRequiredService(
                declaration.OwnerId,
                declaration.ImplementationType)
            : hostProvider.GetRequiredService(declaration.ImplementationType);

    private static void AddDuplicateTypeDiagnostics(
        IEnumerable<StrategyDeclaration> declarations,
        string code,
        ICollection<HostCompositionDiagnostic> diagnostics)
    {
        foreach (var group in declarations.GroupBy(item => item.ImplementationType)
                     .Where(group => group.Count(item =>
                         item.Instance is null && item.Factory is null) > 1))
        {
            diagnostics.Add(new HostCompositionDiagnostic(
                code,
                group.Key.FullName,
                [ToContributor(group.Key)]));
        }
    }

    private static void ReportActivationFailure(
        IHostDiagnosticSink? sink,
        StrategyDeclaration declaration,
        Exception exception) =>
        sink?.Report(new HostDiagnosticDraft(
            HostDiagnosticCodes.ExtensionActivationFailed,
            HostDiagnosticPhase.ExtensionDiscovery)
        {
            PluginId = declaration.OwnerId,
            AssemblyName = declaration.ImplementationType.Assembly.GetName(),
            StableId = declaration.ImplementationType.FullName,
            Exception = exception,
        });

    private static HostCompositionDiagnostic Diagnostic(
        string code,
        string? stableId,
        Type type) => new(code, stableId, [ToContributor(type)]);

    private static HostCompositionContributor ToContributor(Type type) =>
        new(type.FullName ?? type.Name, type.Assembly.GetName().Name ?? "Unknown");

    private void Add(List<StrategyDeclaration> target, PluginId ownerId, Type type)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(type);
        if (type.IsAbstract ||
            type.IsInterface ||
            type.ContainsGenericParameters ||
            type.GetConstructors().Length == 0)
        {
            throw new HostCompositionException([
                Diagnostic("CONTRIBUTION_TYPE_INVALID", ownerId.Value, type)
            ]);
        }

        target.Add(new StrategyDeclaration(ownerId, type, null, null));
    }

    private void EnsureWritable()
    {
        if (_built)
        {
            throw new InvalidOperationException("Plugin Registry 已经构建，不能再次修改或发布。");
        }
    }

    internal sealed record StrategyDeclaration(
        PluginId OwnerId,
        Type ImplementationType,
        object? Instance,
        Func<IServiceProvider, object>? Factory);

    internal sealed record ViewDeclaration(
        PluginId OwnerId,
        Type ViewModelType,
        Type ViewType,
        Func<Control> Factory);
}
