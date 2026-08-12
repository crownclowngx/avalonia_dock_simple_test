using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
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
    private readonly IReadOnlyDictionary<Assembly, PluginManifest> _manifestsByAssembly;
    private readonly IReadOnlyList<Assembly> _discoveredAssemblies;
    private readonly IReadOnlyDictionary<Assembly, IReadOnlyList<Type>> _typesByAssembly;

    private PluginModuleCatalog(
        IReadOnlyList<IPluginModule> modules,
        IReadOnlyDictionary<Assembly, PluginId> pluginIdsByAssembly,
        IReadOnlyDictionary<Assembly, PluginManifest> manifestsByAssembly,
        IReadOnlyList<Assembly> discoveredAssemblies,
        IReadOnlyDictionary<Assembly, IReadOnlyList<Type>> typesByAssembly)
    {
        Modules = modules;
        _pluginIdsByAssembly = pluginIdsByAssembly;
        _manifestsByAssembly = manifestsByAssembly;
        _discoveredAssemblies = discoveredAssemblies;
        _typesByAssembly = typesByAssembly;
    }

    public IReadOnlyList<IPluginModule> Modules { get; }

    internal IReadOnlyList<Assembly> DiscoveredAssemblies => _discoveredAssemblies;

    internal IReadOnlyList<Type> GetDiscoveryTypes(Assembly assembly) =>
        _typesByAssembly[assembly];

    public bool IsManaged(Assembly assembly) => _pluginIdsByAssembly.ContainsKey(assembly);

    public bool TryGetPluginId(Assembly assembly, out PluginId pluginId) =>
        _pluginIdsByAssembly.TryGetValue(assembly, out pluginId!);

    internal bool TryGetManifest(Assembly assembly, out PluginManifest manifest) =>
        _manifestsByAssembly.TryGetValue(assembly, out manifest!);

    public static PluginModuleCatalog Discover(IEnumerable<Assembly> pluginAssemblies)
    {
        ArgumentNullException.ThrowIfNull(pluginAssemblies);
        var assemblies = pluginAssemblies.Distinct().ToArray();
        return DiscoverCore(
            assemblies,
            assembly => AssemblyTypeCatalog.GetLoadableTypes(
                assembly,
                exception => Console.Error.WriteLine(
                    $"PluginCatalog errorCode=MODULE_TYPE_SCAN_PARTIAL assembly={assembly.FullName} type={exception.GetType().Name} details={FormatLoaderErrors(exception)}")),
            getManifest: _ => null,
            diagnosticSink: null);
    }

    /// <summary>
    /// 使用插件目录阶段已经完成的严格类型预检结果发现模块。
    /// </summary>
    /// <remarks>
    /// 设计意图：生产启动不得再次执行允许“部分类型成功”的兼容扫描，否则同一个插件
    /// 可能在预检和模块发现阶段呈现两套不一致的类型集合。
    /// </remarks>
    internal static PluginModuleCatalog Discover(
        PluginDiscoverySnapshot snapshot,
        IHostDiagnosticSink? diagnosticSink = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return DiscoverCore(
            snapshot.Assemblies,
            snapshot.GetPreflightTypes,
            snapshot.GetManifest,
            diagnosticSink);
    }

    private static PluginModuleCatalog DiscoverCore(
        IReadOnlyList<Assembly> assemblies,
        Func<Assembly, IReadOnlyList<Type>> getTypes,
        Func<Assembly, PluginManifest?> getManifest,
        IHostDiagnosticSink? diagnosticSink)
    {
        var modules = new List<IPluginModule>();
        var diagnostics = new List<HostCompositionDiagnostic>();
        var typesByAssembly = new Dictionary<Assembly, IReadOnlyList<Type>>();
        var manifestsByAssembly = new Dictionary<Assembly, PluginManifest>();

        foreach (var assembly in assemblies)
        {
            var loadableTypes = getTypes(assembly);
            typesByAssembly.Add(assembly, loadableTypes);
            var manifest = getManifest(assembly);
            if (manifest is not null)
            {
                manifestsByAssembly.Add(assembly, manifest);
            }
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

                if (manifest is not null && module.PluginId != manifest.PluginId)
                {
                    diagnostics.Add(Diagnostic(
                        HostDiagnosticCodes.PluginManifestDescriptionMismatch,
                        manifest.PluginId.Value,
                        moduleTypes));
                    diagnosticSink?.Report(new HostDiagnosticDraft(
                        HostDiagnosticCodes.PluginManifestDescriptionMismatch,
                        HostDiagnosticPhase.PluginModuleDiscovery,
                        "插件模块身份与加载前清单声明不一致，宿主已中止组合。")
                    {
                        PluginId = manifest.PluginId.Value,
                        PluginDirectory = Path.GetFileName(
                            Path.GetDirectoryName(assembly.Location)),
                        AssemblyName = assembly.GetName().Name,
                        PluginVersion = PluginVersionText.Format(manifest.PluginVersion),
                        HostApiRange = manifest.HostApi.ToString(),
                        CommonContractRange = manifest.CommonContract.ToString(),
                        StableId = manifest.PluginId.Value,
                        TechnicalDetail =
                            $"manifestPluginId={manifest.PluginId.Value}; modulePluginId={module.PluginId.Value}",
                    });
                    continue;
                }

                modules.Add(module);
            }
            catch (Exception exception)
            {
                // 模块构造或 PluginId getter 失败同样属于组合错误，不能记录日志后把程序集伪装成 Legacy。
                // 这里使用稳定诊断而不是暴露异常文本，既保留来源定位，也避免插件异常消息污染启动协议。
                diagnostics.Add(Diagnostic(
                    "PLUGIN_ID_INVALID",
                    null,
                    moduleTypes));
                diagnosticSink?.Report(new HostDiagnosticDraft(
                    "PLUGIN_ID_INVALID",
                    HostDiagnosticPhase.PluginModuleDiscovery,
                    "插件模块构造或 PluginId 读取失败。")
                {
                    AssemblyName = assembly.GetName().Name,
                    StableId = moduleTypes[0].FullName,
                    Exception = exception,
                });
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
        return new PluginModuleCatalog(
            orderedModules,
            map,
            manifestsByAssembly,
            assemblies,
            typesByAssembly);
    }

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var module in Modules)
        {
            module.ConfigureServices(services);
        }
    }

    /// <summary>
    /// 按稳定 PluginId 顺序执行模块服务注册，并在插件代码抛出异常时立即记录致命诊断。
    /// </summary>
    /// <remarks>
    /// IServiceCollection 没有通用事务语义；发生异常后调用方必须丢弃整个集合，
    /// 不能尝试猜测插件已经添加、替换或移除了哪些描述符。
    /// </remarks>
    internal void ConfigureServices(
        IServiceCollection services,
        IHostDiagnosticSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(diagnostics);
        foreach (var module in Modules)
        {
            try
            {
                module.ConfigureServices(services);
            }
            catch (Exception exception)
            {
                diagnostics.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.PluginServiceRegistrationFailed,
                    HostDiagnosticPhase.PluginServiceRegistration,
                    "插件服务注册失败，宿主已放弃本次容器构建。")
                {
                    PluginId = module.PluginId.Value,
                    AssemblyName = module.GetType().Assembly.GetName().Name,
                    Exception = exception,
                });
                throw;
            }
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
