using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Events;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 建立、保存并最终逆序释放每个插件独占的依赖注入 Provider。
/// </summary>
/// <remarks>
/// <para>
/// 本类型是插件级对象图的唯一所有者。每个模块都从新的空 <see cref="ServiceCollection"/> 开始，
/// 插件永远拿不到宿主的 <see cref="IServiceCollection"/>，也不会看到前一个插件的描述符。
/// Microsoft DI 原生支持的开放泛型、keyed service 和多实现注册均不受限制；错误注册最多破坏当前
/// 插件自己的 Provider，因此不再需要复制、比较和提交描述符的防御事务。
/// </para>
/// <para>
/// 这里使用的模式只有朴素的所有者和顺序组合。插件之间没有父容器回退，也没有任意
/// <see cref="IServiceProvider"/> 桥；宿主只显式提供事件总线等阶段性窄端口。
/// </para>
/// </remarks>
internal sealed class PluginProviderOwner : IDisposable
{
    private readonly List<PluginProviderLease> _leases = [];
    private bool _composed;
    private bool _disposed;

    internal IReadOnlySet<PluginId> AvailablePluginIds =>
        _leases.Select(lease => lease.PluginId).ToHashSet();

    /// <summary>
    /// 按规范 PluginId 顺序配置并建立插件 Provider；单个插件失败时记录受控诊断并继续处理后续插件。
    /// </summary>
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
        foreach (var entry in catalog.Entries)
        {
            var manifest = entry.Manifest ?? throw new InvalidOperationException(
                "manifest v2 是生产插件组合的必需入口事实。");
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
                var context = new PluginRegistrationContext(
                    manifest.PluginId,
                    pluginServices,
                    pluginBuilder);
                module.Configure(context);
                context.Seal();

                failureCode = HostDiagnosticCodes.PluginContainerBuildFailed;
                provider = pluginServices.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true,
                });

                // 在发布任何声明前先激活当前插件的宿主可见单例。构造失败只会丢弃本 Provider，
                // 不会让全局 Registry 进入“发布了一半”的状态。
                foreach (var serviceType in pluginBuilder.GetRequiredServiceTypes())
                {
                    provider.GetRequiredService(serviceType);
                }

                var scopeManager = provider.GetRequiredService<DocumentScopeManager>();
                documentScopes.Register(scopeManager);
                registryBuilder.Import(pluginBuilder);
                _leases.Add(new PluginProviderLease(manifest.PluginId, provider));
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

    /// <summary>由 Registry 激活阶段在对应插件 Provider 中解析已声明的宿主可见对象。</summary>
    internal object GetRequiredService(PluginId pluginId, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(serviceType);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var lease = _leases.SingleOrDefault(item => item.PluginId == pluginId) ??
            throw new InvalidOperationException($"插件 {pluginId.Value} 没有可用的独立 Provider。");
        return lease.Provider.GetRequiredService(serviceType);
    }

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

    private static IServiceCollection CreatePluginServices(IServiceProvider hostProvider)
    {
        var services = new ServiceCollection();

        // 事件总线是 Legacy 阶段仍在使用的明确 Host Port。注册宿主拥有的现有实例，
        // 插件 Provider 不取得其释放权，跨插件通信也只通过这一强类型端口发生。
        services.AddSingleton(hostProvider.GetRequiredService<IHostEventBus>());

        // G12 才会把 Bili Tool 对 public Legacy Manager 的读取替换为插件内部 readiness。
        // 此处保留一个精确、只读的阶段桥以维持 G4 可运行基线；没有通用父 Provider 回退。
        services.AddSingleton(_ => hostProvider.GetRequiredService<PluginLifecycleManager>());

        // 每个插件自己的 ScopeFactory 创建 Document Scope。由此产生的 scoped 业务服务、
        // IDocumentLifetime 和 Document 都来自同一个插件 Provider，而不是宿主根容器。
        services.AddScoped<DocumentLifetime>();
        services.AddScoped<IDocumentLifetime>(provider =>
            provider.GetRequiredService<DocumentLifetime>());
        services.AddSingleton<DocumentScopeManager>();
        services.AddSingleton<IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<DocumentScopeManager>());
        return services;
    }

    private sealed record PluginProviderLease(PluginId PluginId, ServiceProvider Provider);
}
