using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 发现选择接入宿主依赖注入的插件模块，并记录对应的托管程序集。
/// <para>
/// 该目录只把真正声明 <see cref="IPluginModule"/> 的程序集标记为托管程序集。
/// 其他程序集仍由 ManagementFactory 使用原有的无参构造路径实例化策略，
/// 从而避免因为宿主增加 DI 能力而改变历史插件的构造函数选择或初始化时机。
/// </para>
/// </summary>
public sealed class PluginModuleCatalog
{
    private readonly HashSet<Assembly> _managedAssemblies;

    private PluginModuleCatalog(IReadOnlyList<IPluginModule> modules)
    {
        Modules = modules;
        _managedAssemblies = modules.Select(x => x.GetType().Assembly).ToHashSet();
    }

    public IReadOnlyList<IPluginModule> Modules { get; }

    /// <summary>
    /// 判断指定程序集是否已显式声明插件模块，只有返回 true 时才允许用 DI 创建其中的策略。
    /// </summary>
    public bool IsManaged(Assembly assembly) => _managedAssemblies.Contains(assembly);

    /// <summary>
    /// 从已经按原有插件目录规则加载的程序集中发现模块。
    /// 模块本身必须提供无参构造函数，因为此阶段根级 ServiceProvider 尚未构建。
    /// </summary>
    public static PluginModuleCatalog Discover(IEnumerable<Assembly> pluginAssemblies)
    {
        var modules = new List<IPluginModule>();
        foreach (var assembly in pluginAssemblies)
        {
            try
            {
                var moduleTypes = assembly.GetTypes()
                    .Where(type => typeof(IPluginModule).IsAssignableFrom(type)
                                   && !type.IsAbstract
                                   && !type.IsInterface
                                   && type.GetConstructor(Type.EmptyTypes) != null);

                foreach (var moduleType in moduleTypes)
                {
                    modules.Add((IPluginModule)Activator.CreateInstance(moduleType)!);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"扫描插件模块 {assembly.FullName} 失败: {ex.Message}");
            }
        }

        return new PluginModuleCatalog(modules
            .OrderBy(x => x.PluginId, StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>
    /// 按稳定的 PluginId 顺序调用模块注册，确保不同启动过程中的服务注册顺序一致。
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        foreach (var module in Modules)
        {
            module.ConfigureServices(services);
        }
    }
}
