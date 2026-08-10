using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 发现显式实现模块契约的托管插件，并保存本次发现使用的完整程序集快照。
/// 策略注册与视图定位复用该快照，避免对插件目录进行重复扫描。
/// </summary>
public sealed class PluginModuleCatalog
{
    private readonly HashSet<Assembly> _managedAssemblies;
    private readonly IReadOnlyList<Assembly> _discoveredAssemblies;

    private PluginModuleCatalog(
        IReadOnlyList<IPluginModule> modules,
        IReadOnlyList<Assembly> discoveredAssemblies)
    {
        Modules = modules;
        _discoveredAssemblies = discoveredAssemblies;
        _managedAssemblies = modules
            .Select(module => module.GetType().Assembly)
            .ToHashSet();
    }

    public IReadOnlyList<IPluginModule> Modules { get; }

    internal IReadOnlyList<Assembly> DiscoveredAssemblies => _discoveredAssemblies;

    public bool IsManaged(Assembly assembly) => _managedAssemblies.Contains(assembly);

    public static PluginModuleCatalog Discover(IEnumerable<Assembly> pluginAssemblies)
    {
        ArgumentNullException.ThrowIfNull(pluginAssemblies);
        var assemblies = pluginAssemblies.Distinct().ToArray();
        var modules = new List<IPluginModule>();

        foreach (var assembly in assemblies)
        {
            var moduleTypes = AssemblyTypeCatalog.GetLoadableTypes(
                    assembly,
                    exception => Console.Error.WriteLine(
                        $"PluginCatalog errorCode=MODULE_TYPE_SCAN_PARTIAL assembly={assembly.FullName} type={exception.GetType().Name}"))
                .Where(type => typeof(IPluginModule).IsAssignableFrom(type)
                               && !type.IsAbstract
                               && !type.IsInterface
                               && type.GetConstructor(Type.EmptyTypes) is not null);

            foreach (var moduleType in moduleTypes)
            {
                try
                {
                    modules.Add((IPluginModule)Activator.CreateInstance(moduleType)!);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"PluginCatalog errorCode=MODULE_ACTIVATION_FAILED module={moduleType.FullName} type={exception.GetType().Name}");
                }
            }
        }

        return new PluginModuleCatalog(
            modules
                .OrderBy(module => module.PluginId, StringComparer.Ordinal)
                .ToArray(),
            assemblies);
    }

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var module in Modules)
        {
            module.ConfigureServices(services);
        }
    }
}
