using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.ViewModels;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 作为宿主组合根，集中完成服务注册、插件发现、容器构建和所有权释放。
/// Registry 提交后由 Host internal 协调器完成生命周期启动；退出时按所有权顺序反向释放。
/// </summary>
internal sealed class HostRuntime : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly PluginProviderOwner _pluginProviders;
    private readonly DocumentScopeRegistry _documentScopes;
    private readonly PluginLifecycleCoordinator _lifecycles;
    private readonly PluginLifecycleStateStore _lifecycleStates;
    private bool _disposed;

    private HostRuntime(
        Microsoft.Extensions.DependencyInjection.ServiceProvider provider,
        PluginProviderOwner pluginProviders,
        DocumentScopeRegistry documentScopes,
        PluginLifecycleCoordinator lifecycles,
        PluginLifecycleStateStore lifecycleStates)
    {
        _provider = provider;
        _pluginProviders = pluginProviders;
        _documentScopes = documentScopes;
        _lifecycles = lifecycles;
        _lifecycleStates = lifecycleStates;
    }

    internal static HostRuntime Create(HostDiagnosticSession diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var services = new ServiceCollection();
        var registryBuilder = new PluginRegistryBuilder();
        var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
        services.AddViewModels();
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);

        var discovery = AssemblyLoaderHelper.Discover(
            AssemblyLoadConstant.PLUGINS_SUBDIRECTORY);
        discovery.PublishDiagnostics(diagnostics);
        ThrowIfStartupMustAbort(diagnostics);

        PluginModuleCatalog pluginCatalog;
        try
        {
            pluginCatalog = PluginModuleCatalog.Discover(discovery);
        }
        catch (HostCompositionException exception)
        {
            ReportCompositionDiagnostics(
                diagnostics,
                exception,
                HostDiagnosticPhase.PluginModuleDiscovery);
            throw;
        }

        // Catalog 与插件 Provider 所有者均是宿主组合基础设施。插件只获得新建的私有集合，
        // 因而既看不到也无法修改这里的任何宿主描述符。
        services.AddSingleton(pluginCatalog);
        Microsoft.Extensions.DependencyInjection.ServiceProvider provider;
        try
        {
            provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
        }
        catch (Exception exception)
        {
            diagnostics.Report(new HostDiagnosticDraft(
                HostDiagnosticCodes.HostContainerBuildFailed,
                HostDiagnosticPhase.HostContainerBuild)
            {
                Exception = exception,
            });
            throw;
        }

        try
        {
            pluginProviders.Compose(
                pluginCatalog,
                provider,
                registryBuilder,
                documentScopes,
                diagnostics);

            // 显式解析 Registry 只校验已经冻结的声明并提交冲突结果，不创建 Document/Tool。
            // 该步骤必须在 UI 启动前完成，以便立即释放冲突 Provider，并保证 UI 只看到最终快照。
            try
            {
                provider.GetRequiredService<PluginRegistry>();
                var lifecycles = provider.GetRequiredService<PluginLifecycleCoordinator>();
                lifecycles.InitializeAllAsync().GetAwaiter().GetResult();
                provider.GetRequiredService<ManagementFactory>();
            }
            catch (HostCompositionException exception)
            {
                ReportCompositionDiagnostics(
                    diagnostics,
                    exception,
                    HostDiagnosticPhase.ExtensionDiscovery);
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.ExtensionDiscoveryFailed,
                    HostDiagnosticPhase.ExtensionDiscovery)
                {
                    Exception = exception,
                });
                throw;
            }
            return new HostRuntime(
                provider,
                pluginProviders,
                documentScopes,
                provider.GetRequiredService<PluginLifecycleCoordinator>(),
                provider.GetRequiredService<PluginLifecycleStateStore>());
        }
        catch
        {
            try
            {
                documentScopes.CloseAll();
            }
            finally
            {
                try
                {
                    pluginProviders.Dispose();
                }
                finally
                {
                    provider.Dispose();
                }
            }
            throw;
        }
    }

    /// <summary>使用当前 Runtime 独占的容器创建生产 Avalonia 应用。</summary>
    /// <remarks>
    /// Builder 工厂捕获的是本 Runtime 的 provider，不存在进程全局 Current 容器；消息循环结束后
    /// Runtime 仍按反向插件生命周期顺序释放同一个 provider。
    /// </remarks>
    internal Avalonia.AppBuilder BuildAvaloniaApp()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return HostAvaloniaBuilder.Build(_provider);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var failures = new List<Exception>();

        // 先关闭可用性入口，保证退出清理期间不会再创建新的插件对象图。
        _lifecycleStates.BeginShutdown();
        var factory = _provider.GetService<ManagementFactory>();
        factory?.BeginShutdown();

        try
        {
            // Adapter/View 必须先于插件 Provider 释放，否则 View 的 DataContext 会短暂指向
            // 已经 Dispose 的 Tool singleton，Document 展示事件也可能在 Scope 结束后继续投影。
            factory?.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            // Adapter/View 清理异常也不能阻断 Scope 兜底。
            _documentScopes.CloseAll();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            // Avalonia 消息循环已经结束，不能捕获一个不再泵送的 UI 同步上下文。
            SynchronizationContext.SetSynchronizationContext(null);
            _lifecycles.ShutdownAllAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            _pluginProviders.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            _provider.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("HostRuntime 退出时一个或多个资源释放失败。", failures);
        }
    }

    private static void ThrowIfStartupMustAbort(HostDiagnosticSession diagnostics)
    {
        if (diagnostics.Snapshot.Any(item =>
                item.Disposition == HostDiagnosticDisposition.AbortStartup))
        {
            throw new HostStartupException("宿主启动诊断包含致命错误。");
        }
    }

    internal static void ReportCompositionDiagnostics(
        IHostDiagnosticSink sink,
        HostCompositionException exception,
        HostDiagnosticPhase phase)
    {
        foreach (var item in exception.Diagnostics)
        {
            if (sink is HostDiagnosticSession session && session.Snapshot.Any(existing =>
                    existing.Code == item.Code &&
                    existing.Phase == phase &&
                    (existing.StableId == item.StableId ||
                     item.Code is HostDiagnosticCodes.ExtensionActivationFailed or "PLUGIN_ID_INVALID") &&
                    (item.Contributors.Count != 1 ||
                     existing.AssemblyName == item.Contributors[0].AssemblyName)))
            {
                continue;
            }

            sink.Report(new HostDiagnosticDraft(item.Code, phase)
            {
                PluginId = MyAvaloniaManagementCommon.Plugin.PluginId.TryParse(
                               item.StableId,
                               out var pluginId) &&
                           pluginId!.Value.StartsWith(
                               "myavalonia.plugin.",
                               StringComparison.Ordinal)
                    ? pluginId
                    : null,
                StableId = item.StableId,
                AssemblyName = item.Contributors.Count == 1
                    ? new AssemblyName(item.Contributors[0].AssemblyName)
                    : null,
                Exception = exception,
            });
        }
    }
}

/// <summary>
/// 表示启动失败已经转换为用户可见诊断，调用方不应再次把它包装为未知错误。
/// </summary>
internal sealed class HostStartupException(string message) : Exception(message);
