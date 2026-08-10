using System;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 作为宿主组合根，集中完成服务注册、插件发现、容器构建和生命周期初始化。
/// 由同一对象反向关闭插件并释放容器，保证启动与清理具有对称的所有权。
/// </summary>
internal sealed class HostRuntime : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly PluginLifecycleManager _lifecycleManager;
    private bool _initialized;
    private bool _disposed;

    private HostRuntime(
        Microsoft.Extensions.DependencyInjection.ServiceProvider provider,
        PluginLifecycleManager lifecycleManager)
    {
        _provider = provider;
        _lifecycleManager = lifecycleManager;
    }

    internal IServiceProvider Services => _provider;

    internal static HostRuntime Create()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddViewModels();

        var pluginAssemblies = AssemblyLoaderHelper.LoadPluginsFromDirectories(
            AssemblyLoadConstant.PLUGINS_SUBDIRECTORY);
        var pluginCatalog = PluginModuleCatalog.Discover(pluginAssemblies);
        pluginCatalog.ConfigureServices(services);
        services.AddSingleton(pluginCatalog);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        return new HostRuntime(
            provider,
            provider.GetRequiredService<PluginLifecycleManager>());
    }

    internal void InitializePlugins()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        _lifecycleManager.InitializeAllAsync().GetAwaiter().GetResult();
        _initialized = true;
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
            }
        }
        finally
        {
            _provider.Dispose();
        }
    }
}
