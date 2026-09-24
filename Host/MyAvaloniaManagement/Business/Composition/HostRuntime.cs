using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Startup;
using MyAvaloniaManagement.Business.Docking;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Enablement;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.Business.Restart;
using MyAvaloniaManagement.Business.Plugins.Installation;
using MyAvaloniaManagement.PluginSdk;

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
    private PluginInstallationSession? _installation;

    /// <summary>只接收已经建立的所有权；测试与生产共用同一启动回滚边界。</summary>
    internal HostRuntime(Microsoft.Extensions.DependencyInjection.ServiceProvider provider, HostRuntimeShutdown shutdown)
    {
        _provider = provider;
        _shutdown = shutdown;
    }

    /// <summary>只组合非 UI 服务；调用者在启动工作线程执行，工作区与控件延迟到 AttachWorkbench。</summary>
    internal static async Task<HostRuntime> CreateAsync(HostDiagnosticSession diagnostics,
        IHostRestartHandoff? restart = null, IStartupProgressSink? progress = null,
        CancellationToken cancellationToken = default, PluginInstallationSession? installation = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();
        progress.ReportSafely(new(StartupStage.Preparing));
        var services = new ServiceCollection();
        var registryBuilder = new PluginRegistryBuilder();
        var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        var participants = new HostShutdownParticipants();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes, participants);
        services.AddViewModels();
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        // 实例由 Program 创建和释放，DI 只借用窄端口，不能拥有跨进程交接的寿命。
        if (restart is not null) services.AddSingleton(restart);
        if (installation is not null)
        {
            services.AddSingleton<IPluginInstallationActions>(installation.Service);
            services.AddSingleton<IPluginInstallationRestartBarrier>(installation.Service);
        }

        var dataRoot = HostDataRootPolicy.ResolveDefault();
        var enablementStore = new PluginEnablementSettingsStore(System.IO.Path.Combine(dataRoot, PluginEnablementSettingsStore.FileName));
        var settings = enablementStore.Load();
        var discovery = AssemblyLoaderHelper.Discover(
            PluginDeploymentConstants.PluginsSubdirectory, settings, dataRoot, progress, cancellationToken);
        // 本次启动事实由发现缓存拥有，下次意图由服务拥有；两个引用不能在保存时一起更新。
        services.AddSingleton(discovery);
        var enablement = new PluginEnablementService(enablementStore, settings,
            discovery.Candidates.Select(candidate => candidate.Manifest.PluginId), diagnostics);
        services.AddSingleton<IPluginEnablementState>(enablement);
        services.AddSingleton<IPluginEnablementActions>(enablement);
        services.AddSingleton<IPluginEnablementRestartBarrier>(enablement);
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
        var runtime = new HostRuntime(provider, shutdown) { _installation = installation };
        return await InitializeAsync(runtime, async () =>
        {
            pluginProviders.Compose(
                pluginCatalog,
                provider,
                registryBuilder,
                documentScopes,
                diagnostics, progress, cancellationToken);

            // 显式解析 Registry 只校验已经冻结的声明并提交冲突结果，不创建 Document/Tool。
            // 该步骤必须在主工作台创建前完成，以便立即释放冲突 Provider，并保证 UI 只看到最终快照。
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.ReportSafely(new(StartupStage.Validating));
                var registry = provider.GetRequiredService<PluginRegistry>();
                // 两类目录均为纯声明，后台即可发现冲突，不会通过 Handler 连带创建工作区。
                // Handler 的一对一绑定校验仍由 AttachWorkbench 在 UI 线程完成。
                provider.GetRequiredService<WorkbenchCommandCatalog>();
                provider.GetRequiredService<WorkflowActionCatalogStore>().Commit(
                    registry,
                    provider.GetRequiredService<PluginAvailabilityReadModel>());
                var lifecycles = provider.GetRequiredService<PluginLifecycleCoordinator>();
                await lifecycles.InitializeAllAsync(cancellationToken, progress).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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
        }, diagnostics).ConfigureAwait(false);
    }

    /// <summary>
    /// 组合完成后的启动事务边界。关闭流程复用正常退出，回滚失败只附加诊断，始终重新抛出原始启动异常。
    /// 通过显式初始化委托注入失败点，不为测试增加 public SDK，也不在 catch 里解析缺失服务。
    /// </summary>
    internal static HostRuntime Initialize(HostRuntime runtime, Action initialization, IHostDiagnosticSink diagnostics) =>
        InitializeAsync(runtime, () => { initialization(); return Task.CompletedTask; }, diagnostics).GetAwaiter().GetResult();

    /// <summary>异步回滚沿用唯一关闭链；原异常优先，清理失败只追加诊断，不掩盖取消或启动失败。</summary>
    internal static async Task<HostRuntime> InitializeAsync(HostRuntime runtime, Func<Task> initialization, IHostDiagnosticSink diagnostics)
    {
        try { await initialization().ConfigureAwait(false); return runtime; }
        catch
        {
            try
            {
                var result = await runtime.ShutdownAsync().ConfigureAwait(false);
                if (result.Failures.Count != 0) throw new AggregateException(result.Failures);
            }
            catch (Exception cleanupException)
            {
                try { diagnostics.Report(new HostDiagnosticDraft(HostDiagnosticCodes.HostStartupCleanupFailed,
                    HostDiagnosticPhase.HostBootstrap) { Exception = cleanupException }); }
                catch { /* 清理诊断不能覆盖原始启动异常。 */ }
            }
            throw;
        }
    }

    /// <summary>只在 UI 线程装配完整资源及工作台。Runtime 从未在后台解析 WorkspaceSession 或创建 View。</summary>
    internal void AttachWorkbench(App application, Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
    {
        Avalonia.Threading.Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        application.InstallWorkbenchResources(_provider.GetRequiredService<ViewLocator>(),
            _provider.GetRequiredService<DocumentControlRecycling>());
        _provider.GetRequiredService<HostWorkbenchCommandBindings>();
        _provider.GetRequiredService<IHostDesktopShell>().Attach(application, desktop);
    }

    /// <summary>失败摘要留在正式工作台；按钮复用同一插件看板服务，不另建诊断窗口体系。</summary>
    internal void ShowStartupWarning(MyAvaloniaManagement.Views.MainWindow window, int count) =>
        window.ShowStartupWarning(count, _provider.GetRequiredService<PluginStatusWindowService>().ShowOrActivate);

    /// <summary>主窗口已展示后确认安装；磁盘摘要在后台计算，贡献可用性沿用唯一生命周期读模型。</summary>
    internal async Task ConfirmInstallationStartupAsync()
    {
        if (_installation is null) return;
        var operation = _installation.Service.Status.Operation;
        if (operation?.Phase != PluginInstallPhase.AwaitingStartup) return;
        var id = PluginId.Parse(operation.PluginId);
        var discovery = _provider.GetRequiredService<PluginDiscoverySnapshot>();
        var settings = discovery.StartupSettings.Settings;
        // 配置损坏导致无法决定启用状态时不能冒充“已禁用且安装成功”。
        var enabled = settings?.IsEnabled(id) != false;
        var available = settings is not null && _provider.GetRequiredService<PluginAvailabilityReadModel>().IsAvailable(id);
        await Task.Run(() => _installation.Applier.ConfirmAsync(enabled, available, CancellationToken.None));
        await _installation.Service.RefreshAsync();
    }

    /// <summary>异步收尾用于消息循环仍可用的启动失败路径；正常进程退出也共享这个任务。</summary>
    internal Task<HostRuntimeShutdownResult> ShutdownAsync()
    {
        _disposed = true;
        return _shutdown.RunAsync();
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
        var result = Shutdown();
        if (result.Failures.Count > 0)
            throw new AggregateException("HostRuntime 退出时一个或多个资源释放失败。", result.Failures);
    }

    /// <summary>向进程入口交付完整关闭事实；保留资源与 Dispose 失败都不能被解释成可重启。</summary>
    internal HostRuntimeShutdownResult Shutdown()
    {
        _disposed = true;
        // 消息循环可能已经停止。仅在同步桥接期间清除上下文，回滚返回后恢复调用方上下文。
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            return _shutdown.RunAsync().GetAwaiter().GetResult();
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
