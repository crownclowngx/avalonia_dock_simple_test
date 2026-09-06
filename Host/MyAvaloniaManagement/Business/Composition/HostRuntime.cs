using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.Business.WorkflowActions;

namespace MyAvaloniaManagement.Business.Composition;

/// <summary>
/// 作为宿主组合根，集中完成服务注册、插件发现、容器构建和所有权释放。
/// Registry 提交后由 Host internal 协调器完成生命周期启动；退出时按所有权顺序反向释放。
/// </summary>
internal sealed class HostRuntime : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly HostRuntimeShutdown _shutdown;
    private bool _disposed;

    /// <summary>只接收已经建立的所有权；测试与生产共用同一启动回滚边界。</summary>
    internal HostRuntime(Microsoft.Extensions.DependencyInjection.ServiceProvider provider, HostRuntimeShutdown shutdown)
    {
        _provider = provider;
        _shutdown = shutdown;
    }

    internal static HostRuntime Create(HostDiagnosticSession diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var services = new ServiceCollection();
        var registryBuilder = new PluginRegistryBuilder();
        var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        var participants = new HostShutdownParticipants();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes, participants);
        services.AddViewModels();
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);

        var discovery = AssemblyLoaderHelper.Discover(
            PluginDeploymentConstants.PluginsSubdirectory);
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

        var shutdown = new HostRuntimeShutdown(pluginProviders, provider, documentScopes.CloseAll,
            participants, HostResourceRetention.ProcessLifetime, diagnostics);
        var runtime = new HostRuntime(provider, shutdown);
        return Initialize(runtime, () =>
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
                var registry = provider.GetRequiredService<PluginRegistry>();
                // Catalog 在 UI 启动前完成 Host/Plugin 最终合并；即使合法命名空间原则上不会
                // 碰撞，也不能把损坏快照推迟到第一次用户执行时才发现。
                provider.GetRequiredService<WorkbenchCommandCatalog>();
                provider.GetRequiredService<WorkflowActionCatalogStore>().Commit(
                    registry,
                    provider.GetRequiredService<PluginAvailabilityReadModel>());
                var lifecycles = provider.GetRequiredService<PluginLifecycleCoordinator>();
                lifecycles.InitializeAllAsync().GetAwaiter().GetResult();
                provider.GetRequiredService<WorkspaceSession>();
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
        }, diagnostics);
    }

    /// <summary>
    /// 组合完成后的启动事务边界。关闭流程复用正常退出，回滚失败只附加诊断，始终重新抛出原始启动异常。
    /// 通过显式初始化委托注入失败点，不为测试增加 public SDK，也不在 catch 里解析缺失服务。
    /// </summary>
    internal static HostRuntime Initialize(HostRuntime runtime, Action initialization, IHostDiagnosticSink diagnostics)
    {
        try
        {
            initialization();
            return runtime;
        }
        catch
        {
            try { runtime.Dispose(); }
            catch (Exception cleanupException)
            {
                try
                {
                    diagnostics.Report(new HostDiagnosticDraft(HostDiagnosticCodes.HostStartupCleanupFailed,
                        HostDiagnosticPhase.HostBootstrap) { Exception = cleanupException });
                }
                catch { /* 清理诊断失败不能覆盖原始启动异常。 */ }
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
        _disposed = true;
        // 消息循环可能已经停止。仅在同步桥接期间清除上下文，回滚返回后恢复调用方上下文。
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var result = _shutdown.RunAsync().GetAwaiter().GetResult();
            if (result.Failures.Count > 0)
                throw new AggregateException("HostRuntime 退出时一个或多个资源释放失败。", result.Failures);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
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
                PluginId = MyAvaloniaManagement.PluginSdk.PluginId.TryParse(
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
