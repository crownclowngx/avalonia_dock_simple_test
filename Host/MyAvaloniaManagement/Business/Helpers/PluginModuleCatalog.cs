using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 发现并验证显式实现模块契约的托管插件。
/// </summary>
/// <remarks>
/// 模块必须在调用任何 ConfigureServices 之前形成唯一身份，这样失败的发现不会向共享
/// IServiceCollection 留下无法回滚的部分注册。一个程序集只允许一个模块，也是为了让
/// Assembly 到 PluginId 的所有权映射始终无歧义。
/// </remarks>
public sealed class PluginModuleCatalog
{
    private readonly IReadOnlyDictionary<Assembly, PluginId> _pluginIdsByAssembly;
    private readonly IReadOnlyList<Assembly> _discoveredAssemblies;

    private PluginModuleCatalog(
        IReadOnlyList<IPluginModule> modules,
        IReadOnlyDictionary<Assembly, PluginId> pluginIdsByAssembly,
        IReadOnlyList<Assembly> discoveredAssemblies)
    {
        Modules = modules;
        _pluginIdsByAssembly = pluginIdsByAssembly;
        _discoveredAssemblies = discoveredAssemblies;
    }

    public IReadOnlyList<IPluginModule> Modules { get; }

    internal IReadOnlyList<Assembly> DiscoveredAssemblies => _discoveredAssemblies;

    public bool IsManaged(Assembly assembly) => _pluginIdsByAssembly.ContainsKey(assembly);

    public bool TryGetPluginId(Assembly assembly, out PluginId pluginId) =>
        _pluginIdsByAssembly.TryGetValue(assembly, out pluginId!);

    public static PluginModuleCatalog Discover(IEnumerable<Assembly> pluginAssemblies)
    {
        ArgumentNullException.ThrowIfNull(pluginAssemblies);
        var assemblies = pluginAssemblies.Distinct().ToArray();
        var modules = new List<IPluginModule>();
        var diagnostics = new List<HostCompositionDiagnostic>();

        foreach (var assembly in assemblies)
        {
            var loadableTypes = AssemblyTypeCatalog.GetLoadableTypes(
                    assembly,
                    exception => Console.Error.WriteLine(
                        $"PluginCatalog errorCode=MODULE_TYPE_SCAN_PARTIAL assembly={assembly.FullName} type={exception.GetType().Name} details={FormatLoaderErrors(exception)}"));
            var moduleTypes = loadableTypes
                .Where(type => typeof(IPluginModule).IsAssignableFrom(type)
                               && !type.IsAbstract
                               && !type.IsInterface
                               && type.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            if (moduleTypes.Length > 1)
            {
                diagnostics.Add(Diagnostic("PLUGIN_MODULE_MULTIPLE", assembly.GetName().Name, moduleTypes));
                continue;
            }

            if (moduleTypes.Length == 0)
            {
                continue;
            }

            try
            {
                var module = (IPluginModule)Activator.CreateInstance(moduleTypes[0])!;
                if (module.PluginId is null ||
                    !module.PluginId.IsCanonical ||
                    !module.PluginId.Value.StartsWith("myavalonia.plugin.", StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic(
                        "PLUGIN_ID_INVALID",
                        module.PluginId?.Value,
                        moduleTypes));
                    continue;
                }

                modules.Add(module);
            }
            catch (Exception)
            {
                // 模块构造或 PluginId getter 失败同样属于组合错误，不能记录日志后把程序集伪装成 Legacy。
                // 这里使用稳定诊断而不是暴露异常文本，既保留来源定位，也避免插件异常消息污染启动协议。
                diagnostics.Add(Diagnostic(
                    "PLUGIN_ID_INVALID",
                    null,
                    moduleTypes));
            }
        }

        foreach (var group in modules.GroupBy(module => module.PluginId).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new HostCompositionDiagnostic(
                "PLUGIN_ID_DUPLICATE",
                group.Key.Value,
                group.Select(module => ToContributor(module.GetType())).ToArray()));
        }

        if (diagnostics.Count > 0)
        {
            throw new HostCompositionException(diagnostics);
        }

        var orderedModules = modules
            .OrderBy(module => module.PluginId.Value, StringComparer.Ordinal)
            .ToArray();
        var map = orderedModules.ToDictionary(
            module => module.GetType().Assembly,
            module => module.PluginId);
        return new PluginModuleCatalog(orderedModules, map, assemblies);
    }

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var module in Modules)
        {
            module.ConfigureServices(services);
        }
    }

    private static HostCompositionDiagnostic Diagnostic(
        string code,
        string? stableId,
        IEnumerable<Type> types) =>
        new(code, stableId, types.Select(ToContributor).ToArray());

    private static HostCompositionContributor ToContributor(Type type) =>
        new(type.FullName ?? type.Name, type.Assembly.GetName().Name ?? type.Assembly.FullName ?? "Unknown");

    private static string FormatLoaderErrors(Exception exception) =>
        exception is ReflectionTypeLoadException typeLoadException
            ? string.Join(" | ", typeLoadException.LoaderExceptions
                .Where(item => item is not null)
                .Select(item => item!.Message.Replace(Environment.NewLine, " ", StringComparison.Ordinal)))
            : exception.Message.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
}
