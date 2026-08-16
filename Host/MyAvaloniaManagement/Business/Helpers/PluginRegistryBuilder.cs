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
/// Builder 有意不提供删除或覆盖操作。它先收集全部事实，再激活、验证并提交；失败时调用方
/// 丢弃 Builder 和容器，因此不会出现“前几个插件已生效、后一个插件失败”的半发布状态。
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
        IHostDiagnosticSink? diagnosticSink = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        EnsureWritable();
        _built = true;

        var diagnostics = ValidateDeclarations();
        var documents = ActivateDocuments(serviceProvider, diagnostics, diagnosticSink);
        var tools = ActivateTools(serviceProvider, diagnostics, diagnosticSink);
        var lifecycles = ActivateLifecycles(serviceProvider, diagnostics, diagnosticSink);

        if (diagnostics.Count > 0)
        {
            throw new HostCompositionException(diagnostics);
        }

        return new PluginRegistry(
            catalog?.CreatePluginSnapshots(_documents, _tools, _views, _lifecycles) ?? [],
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
        IServiceProvider provider,
        ICollection<HostCompositionDiagnostic> diagnostics,
        IHostDiagnosticSink? sink) =>
        _documents.Select(declaration =>
        {
            try
            {
                var strategy = declaration.Instance as IDocumentCreationStrategy ??
                    declaration.Factory?.Invoke(provider) as IDocumentCreationStrategy ??
                    (IDocumentCreationStrategy)provider.GetRequiredService(
                        declaration.ImplementationType);
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
        IServiceProvider provider,
        ICollection<HostCompositionDiagnostic> diagnostics,
        IHostDiagnosticSink? sink) =>
        _tools.Select(declaration =>
        {
            try
            {
                var strategy = declaration.Instance as IToolCreationStrategy ??
                    declaration.Factory?.Invoke(provider) as IToolCreationStrategy ??
                    (IToolCreationStrategy)provider.GetRequiredService(
                        declaration.ImplementationType);
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
        IServiceProvider provider,
        ICollection<HostCompositionDiagnostic> diagnostics,
        IHostDiagnosticSink? sink) =>
        _lifecycles.Select(declaration =>
        {
            try
            {
                return new PluginLifecycleRegistration(
                    declaration.OwnerId,
                    (IPluginLifecycle)provider.GetRequiredService(declaration.ImplementationType));
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
            HostDiagnosticPhase.ExtensionDiscovery,
            "显式贡献激活或元数据读取失败。")
        {
            PluginId = declaration.OwnerId.Value,
            AssemblyName = declaration.ImplementationType.Assembly.GetName().Name,
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
