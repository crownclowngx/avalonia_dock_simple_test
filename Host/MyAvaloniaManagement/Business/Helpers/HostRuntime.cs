using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Presentation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagement.ViewModels;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 作为宿主组合根，集中完成服务注册、插件发现、容器构建和生命周期初始化。
/// 由同一对象反向关闭插件并释放容器，保证启动与清理具有对称的所有权。
/// </summary>
internal sealed class HostRuntime : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly PluginLifecycleManager _lifecycleManager;
    private readonly HostDiagnosticSession _diagnostics;
    private readonly HashSet<string> _reportedLifecycleStates = new(StringComparer.Ordinal);
    private bool _initialized;
    private bool _disposed;

    private HostRuntime(
        Microsoft.Extensions.DependencyInjection.ServiceProvider provider,
        PluginLifecycleManager lifecycleManager,
        HostDiagnosticSession diagnostics)
    {
        _provider = provider;
        _lifecycleManager = lifecycleManager;
        _diagnostics = diagnostics;
    }

    internal static HostRuntime Create(HostDiagnosticSession diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var services = new ServiceCollection();
        var registryBuilder = new PluginRegistryBuilder();
        services.AddApplicationServices(registryBuilder);
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
            pluginCatalog = PluginModuleCatalog.Discover(discovery, diagnostics);
        }
        catch (HostCompositionException exception)
        {
            ReportCompositionDiagnostics(
                diagnostics,
                exception,
                HostDiagnosticPhase.PluginModuleDiscovery,
                "插件模块身份或程序集归属不满足宿主契约。");
            throw;
        }

        // Catalog 本身也是宿主组合基础设施，必须在插件获得注册入口前进入 G6 保护基线。
        // 若放在 Configure 之后，插件虽然无法引用 internal 类型，却仍可能通过反射追加同类型描述符。
        services.AddSingleton(pluginCatalog);
        pluginCatalog.Configure(services, registryBuilder, diagnostics);
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
                HostDiagnosticPhase.HostContainerBuild,
                "宿主依赖注入容器构建失败，主工作台不能安全启动。")
            {
                Exception = exception,
            });
            throw;
        }

        try
        {
            // 显式解析 Registry 会激活并验证全部贡献。必须在生命周期回调和 UI 启动前完成，
            // 防止重复 ID 直到用户打开窗口时才暴露，也保证失败时能立即释放根容器。
            try
            {
                provider.GetRequiredService<PluginRegistry>();
                provider.GetRequiredService<ManagementFactory>();
            }
            catch (HostCompositionException exception)
            {
                ReportCompositionDiagnostics(
                    diagnostics,
                    exception,
                    HostDiagnosticPhase.ExtensionDiscovery,
                    "Document 或 Tool 扩展无法形成无歧义注册表。");
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.ExtensionDiscoveryFailed,
                    HostDiagnosticPhase.ExtensionDiscovery,
                    "扩展策略激活或元数据读取失败，主工作台不能安全启动。")
                {
                    Exception = exception,
                });
                throw;
            }
            return new HostRuntime(
                provider,
                provider.GetRequiredService<PluginLifecycleManager>(),
                diagnostics);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    internal void InitializePlugins()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        _lifecycleManager.InitializeAllAsync().GetAwaiter().GetResult();
        ReportLifecycleFailures();
        _initialized = true;
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
        try
        {
            if (_initialized)
            {
                Program.ShutdownPlugins(_lifecycleManager);
                ReportLifecycleFailures();
            }
        }
        finally
        {
            _provider.Dispose();
        }
    }

    private void ReportLifecycleFailures()
    {
        foreach (var state in _lifecycleManager.States.Where(state =>
                     state.Status is PluginLifecycleStatus.Failed
                         or PluginLifecycleStatus.Blocked
                         or PluginLifecycleStatus.TimedOut))
        {
            var fingerprint = $"{state.PluginId.Value}|{state.Stage}|{state.Status}|{state.ErrorCode}";
            if (!_reportedLifecycleStates.Add(fingerprint))
            {
                continue;
            }

            _diagnostics.Report(new HostDiagnosticDraft(
                state.ErrorCode ?? HostDiagnosticCodes.LifecycleFailed,
                HostDiagnosticPhase.PluginLifecycle,
                state.ErrorMessage ?? "插件生命周期操作失败。")
            {
                PluginId = state.PluginId.Value,
                StableId = state.BlockingPluginId?.Value,
                TechnicalDetail = state.Duration is null
                    ? null
                    : $"stage={state.Stage}; durationMs={state.Duration.Value.TotalMilliseconds:0.###}",
            });
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
        HostDiagnosticPhase phase,
        string userMessage)
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

            sink.Report(new HostDiagnosticDraft(item.Code, phase, userMessage)
            {
                PluginId = item.StableId?.StartsWith(
                    "myavalonia.plugin.",
                    StringComparison.Ordinal) == true
                    ? item.StableId
                    : null,
                StableId = item.StableId,
                AssemblyName = item.Contributors.Count == 1
                    ? item.Contributors[0].AssemblyName
                    : null,
                Exception = exception,
                TechnicalDetail = string.Join(
                    Environment.NewLine,
                    item.Contributors.Select(source =>
                        $"{source.TypeName} ({source.AssemblyName})")),
            });
        }
    }
}

/// <summary>
/// 表示启动失败已经转换为用户可见诊断，调用方不应再次把它包装为未知错误。
/// </summary>
internal sealed class HostStartupException(string message) : Exception(message);
