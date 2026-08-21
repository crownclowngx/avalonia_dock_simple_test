using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 建立、暂存并最终逆序释放每个声明式插件独占的依赖注入 Provider。
/// </summary>
/// <remarks>
/// Provider 构建成功只代表“候选可解析”，不代表贡献已经发布。全部插件完成配置后，Registry Builder
/// 才能判断跨插件稳定 ID 与模型映射冲突；本所有者随后一次提交无冲突租约、释放被排除租约，并只为
/// 已接受插件登记 Document Scope。该两阶段过程避免为了回滚而复制服务描述符。
/// </remarks>
internal sealed class PluginProviderOwner : IDisposable
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
                var pluginServices = CreatePluginServices(hostProvider);
                var pluginBuilder = new PluginRegistryBuilder();
                var registration = new PluginRegistration(
                    pluginId,
                    pluginServices,
                    pluginBuilder);
                module.Configure(registration);
                registration.Seal();

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

                // Scope 管理器不是插件模型；解析它不会创建 Document 或 Tool。
                // Lifecycle singleton 只验证可解析性，不调用启动/停止，最终编排仍留给 G8。
                var scopeManager = provider.GetRequiredService<DocumentScopeManager>();
                registryBuilder.Import(pluginBuilder);
                _leases.Add(new PluginProviderLease(
                    pluginId, provider, scopeManager));
                provider = null;
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

    internal DocumentScopeManager GetDocumentScopeManager(PluginId pluginId) =>
        GetAcceptedLease(pluginId).ScopeManager;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var index = _leases.Count - 1; index >= 0; index--)
        {
            _leases[index].Provider.Dispose();
        }

        _leases.Clear();
    }

    private PluginProviderLease GetAcceptedLease(PluginId pluginId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var lease = _leases.SingleOrDefault(item =>
            item.Accepted && item.PluginId == pluginId);
        return lease ?? throw new InvalidOperationException(
            $"插件 {pluginId.Value} 没有已提交的独立 Provider。");
    }

    private static IServiceCollection CreatePluginServices(IServiceProvider hostProvider)
    {
        var services = new ServiceCollection();

        // 只注入最终 SDK 明确承诺的 Host Port；不存在父 Provider 或任意 IServiceProvider 回退。
        services.AddSingleton(hostProvider.GetRequiredService<IHostEventBus>());
        services.AddScoped<DocumentLifetime>();
        services.AddScoped<IDocumentLifetime>(provider =>
            provider.GetRequiredService<DocumentLifetime>());

        // Legacy Scope 接口暂时仅用于 G9-G12 前的业务插件源码测试，不参与声明式贡献事实。
        services.AddScoped<MyAvaloniaManagementCommon.DocumentCreation.IDocumentLifetime>(provider =>
            provider.GetRequiredService<DocumentLifetime>());
        services.AddSingleton<DocumentScopeManager>();
        services.AddSingleton<MyAvaloniaManagementCommon.DocumentCreation.IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<DocumentScopeManager>());
        return services;
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
