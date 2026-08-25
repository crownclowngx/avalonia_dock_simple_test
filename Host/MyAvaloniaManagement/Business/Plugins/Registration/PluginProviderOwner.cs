using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Plugins.Registration;

/// <summary>
/// 建立、暂存并最终逆序释放每个声明式插件独占的依赖注入 Provider。
/// </summary>
/// <remarks>
/// Provider 构建成功只代表“候选可解析”，不代表贡献已经发布。全部插件完成配置后，Registry Builder
/// 才能判断跨插件稳定 ID 与模型映射冲突；本所有者随后一次提交无冲突租约、释放被排除租约，并只为
/// 已接受插件登记 Document Scope。该两阶段过程避免为了回滚而复制服务描述符。
/// </remarks>
internal sealed class PluginProviderOwner : IDisposable, IPluginLifecycleResolver,
    IWorkflowActionScopeFactory
{
    private readonly List<PluginProviderLease> _leases = [];
    private DocumentScopeRegistry? _documentScopes;
    private bool _composed;
    private bool _registryCommitted;
    private bool _disposed;

    internal IReadOnlySet<PluginId> AvailablePluginIds =>
        _leases.Where(lease => !_registryCommitted || lease.Accepted)
            .Select(lease => lease.PluginId)
            .ToHashSet();

    internal void Compose(
        PluginModuleCatalog catalog,
        IServiceProvider hostProvider,
        PluginRegistryBuilder registryBuilder,
        DocumentScopeRegistry documentScopes,
        IHostDiagnosticSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(hostProvider);
        ArgumentNullException.ThrowIfNull(registryBuilder);
        ArgumentNullException.ThrowIfNull(documentScopes);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_composed)
        {
            throw new InvalidOperationException("插件 Provider 已经完成组合，不能重复建立。");
        }

        _composed = true;
        _documentScopes = documentScopes;
        foreach (var entry in catalog.Entries)
        {
            var manifest = entry.Manifest ?? throw new InvalidOperationException(
                "manifest v2 是生产插件组合的必需入口事实。");
            var pluginId = new PluginId(manifest.PluginId.Value);
            IPluginModule module;
            try
            {
                module = entry.CreateModule();
            }
            catch (Exception exception)
            {
                diagnostics.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.PluginModuleActivationFailed,
                    HostDiagnosticPhase.PluginModuleDiscovery)
                {
                    PluginId = manifest.PluginId,
                    AssemblyName = entry.Assembly.GetName(),
                    Exception = exception,
                });
                continue;
            }

            ServiceProvider? provider = null;
            var failureCode = HostDiagnosticCodes.PluginServiceRegistrationFailed;
            try
            {
                // 模块开始配置时看到的是真正空集合；Host Port、Scope 基础设施和贡献根只有在
                // Configure 返回并通过所有权校验后才由 Commit Guard 最终追加。
                var pluginServices = new ServiceCollection();
                var pluginBuilder = new PluginRegistryBuilder();
                var registration = new PluginRegistration(
                    pluginId,
                    pluginServices,
                    pluginBuilder);
                module.Configure(registration);
                registration.Seal();
                PluginServiceCommitGuard.ValidateAndCommit(
                    pluginServices,
                    registration,
                    hostProvider);

                failureCode = HostDiagnosticCodes.PluginContainerBuildFailed;
                provider = pluginServices.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true,
                });

                foreach (var lifecycleType in pluginBuilder.GetLifecycleTypes())
                {
                    provider.GetRequiredService(lifecycleType);
                }

                // ValidateOnBuild 只验证 DI 调用图，不一定实际构造 scoped 根。使用一个短命验证
                // Scope 精确解析全部 Handler，并在导入全局 Builder 前完成同步/异步释放。
                var validationScope = provider.CreateAsyncScope();
                try
                {
                    foreach (var handlerType in pluginBuilder.GetWorkflowActionHandlerTypes())
                    {
                        _ = validationScope.ServiceProvider.GetRequiredService(handlerType);
                    }
                }
                finally
                {
                    validationScope.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }

                // Scope 管理器不是插件模型；解析它不会创建 Document 或 Tool。
                // Lifecycle singleton 只验证可解析性，不调用启动/停止，最终编排仍留给 G8。
                var scopeManager = provider.GetRequiredService<DocumentScopeManager>();
                registryBuilder.Import(pluginBuilder);
                _leases.Add(new PluginProviderLease(
                    pluginId, provider, scopeManager));
                provider = null;
            }
            catch (HostCompositionException exception)
            {
                provider?.Dispose();
                PluginRegistrationDiagnosticReporter.Report(
                    exception,
                    manifest,
                    entry,
                    diagnostics);
            }
            catch (Exception exception)
            {
                provider?.Dispose();
                diagnostics.Report(new HostDiagnosticDraft(
                    failureCode,
                    HostDiagnosticPhase.PluginServiceRegistration)
                {
                    PluginId = manifest.PluginId,
                    AssemblyName = entry.Assembly.GetName(),
                    Exception = exception,
                });
            }
        }
    }

    /// <summary>
    /// 提交 Registry 的冲突判断结果；被排除插件不会留下 Provider、Scope 或部分贡献。
    /// </summary>
    internal void CommitRegistryResult(IReadOnlySet<PluginId> rejectedOwners)
    {
        ArgumentNullException.ThrowIfNull(rejectedOwners);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_registryCommitted)
        {
            throw new InvalidOperationException("插件 Registry 结果已经提交。");
        }

        _registryCommitted = true;
        for (var index = _leases.Count - 1; index >= 0; index--)
        {
            var lease = _leases[index];
            if (rejectedOwners.Contains(lease.PluginId))
            {
                lease.Provider.Dispose();
                _leases.RemoveAt(index);
                continue;
            }

            lease.Accepted = true;
        }

        foreach (var lease in _leases)
        {
            _documentScopes!.Register(lease.ScopeManager);
        }
    }

    internal object GetRequiredService(PluginId pluginId, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(serviceType);
        return GetAcceptedLease(pluginId).Provider.GetRequiredService(serviceType);
    }

    PluginLifecycleCallbacks IPluginLifecycleResolver.GetRequiredLifecycle(
        PluginId pluginId,
        Type implementationType)
    {
        var lifecycle = GetRequiredService(pluginId, implementationType);
        return CreateLifecycleCallbacks(lifecycle, implementationType);
    }

    /// <summary>
    /// 把最终 SDK 生命周期收窄为 Host internal 回调句柄。
    /// Provider 边界只验证当前唯一 Core/UI 契约，不承担版本分派或兼容适配职责。
    /// </summary>
    internal static PluginLifecycleCallbacks CreateLifecycleCallbacks(
        object lifecycle,
        Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(implementationType);
        if (lifecycle is not IPluginLifecycle sdk)
        {
            throw new InvalidOperationException(
                $"生命周期实现 {implementationType.FullName} 未实现当前 V3 SDK 契约。");
        }

        return new PluginLifecycleCallbacks(
            sdk.InitializeAsync,
            sdk.ShutdownAsync);
    }

    internal DocumentScopeManager GetDocumentScopeManager(PluginId pluginId) =>
        GetAcceptedLease(pluginId).ScopeManager;

    /// <summary>在动作所有者 Provider 内创建一次独立调用 Scope 并解析冻结 Handler。</summary>
    /// <remarks>
    /// 返回的租约只暴露共享 SDK Handler，不暴露 Provider 或 IServiceProvider。调用方必须在
    /// Handler 真正结束后异步释放租约，Host 关闭也不得越过这一所有权边界强行释放 Provider。
    /// </remarks>
    public IWorkflowActionInvocationScope CreateWorkflowActionScope(
        PluginId pluginId,
        Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        var scope = GetAcceptedLease(pluginId).Provider.CreateAsyncScope();
        try
        {
            var handler = scope.ServiceProvider.GetRequiredService(handlerType)
                as IWorkflowActionHandler ?? throw new InvalidOperationException(
                    $"Workflow Action Handler {handlerType.FullName} 未实现当前 SDK 契约。");
            return new PluginWorkflowActionScope(scope, handler);
        }
        catch
        {
            scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Exception>? disposeFailures = null;
        for (var index = _leases.Count - 1; index >= 0; index--)
        {
            try
            {
                _leases[index].Provider.Dispose();
            }
            catch (Exception exception)
            {
                // Provider 中包含插件 Tool singleton 与根级依赖；一个插件释放失败时，
                // 仍按既定逆序尝试其他插件，最后统一把失败交给 Runtime 边界。
                (disposeFailures ??= []).Add(exception);
            }
        }

        _leases.Clear();
        if (disposeFailures is not null)
        {
            throw new AggregateException("一个或多个插件 Provider 释放失败。", disposeFailures);
        }
    }

    private PluginProviderLease GetAcceptedLease(PluginId pluginId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var lease = _leases.SingleOrDefault(item =>
            item.Accepted && item.PluginId == pluginId);
        return lease ?? throw new InvalidOperationException(
            $"插件 {pluginId.Value} 没有已提交的独立 Provider。");
    }

    private sealed class PluginProviderLease(
        PluginId pluginId,
        ServiceProvider provider,
        DocumentScopeManager scopeManager)
    {
        internal PluginId PluginId { get; } = pluginId;
        internal ServiceProvider Provider { get; } = provider;
        internal DocumentScopeManager ScopeManager { get; } = scopeManager;
        internal bool Accepted { get; set; }
    }
}

/// <summary>封装一次 Workflow Action invocation scope 的唯一释放权。</summary>
internal interface IWorkflowActionScopeFactory
{
    IWorkflowActionInvocationScope CreateWorkflowActionScope(
        PluginId pluginId,
        Type handlerType);
}

/// <summary>Executor 可见的最小 invocation scope；不暴露 Provider 或服务定位能力。</summary>
internal interface IWorkflowActionInvocationScope : IAsyncDisposable
{
    IWorkflowActionHandler Handler { get; }
}

internal sealed class PluginWorkflowActionScope(
    AsyncServiceScope scope,
    IWorkflowActionHandler handler) : IWorkflowActionInvocationScope
{
    public IWorkflowActionHandler Handler { get; } = handler;

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}
