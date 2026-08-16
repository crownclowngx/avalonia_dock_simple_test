using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 发现严格清单入口中的唯一 Managed Plugin 模块。
/// </summary>
/// <remarks>
/// Catalog 只回答“哪个清单对应哪个模块”，不发现 Document、Tool 或 View。生产入口使用加载阶段
/// 已经形成的预检类型集合，因此插件程序集只为确定唯一模块扫描一次。
/// </remarks>
internal sealed class PluginModuleCatalog
{
    private PluginModuleCatalog(IReadOnlyList<PluginModuleEntry> entries)
    {
        Entries = entries;
        Modules = entries.Select(entry => entry.Module).ToArray();
    }

    internal IReadOnlyList<PluginModuleEntry> Entries { get; }

    internal IReadOnlyList<IPluginModule> Modules { get; }

    /// <summary>测试和预检工具使用的无清单发现入口；结果不能进入生产组合。</summary>
    internal static PluginModuleCatalog Discover(IEnumerable<Assembly> pluginAssemblies)
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
            getPreflightModuleType: _ => null,
            diagnosticSink: null);
    }

    /// <summary>复用插件目录阶段已经完成的严格类型预检结果发现模块。</summary>
    internal static PluginModuleCatalog Discover(
        PluginDiscoverySnapshot snapshot,
        IHostDiagnosticSink? diagnosticSink = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return DiscoverCore(
            snapshot.Assemblies,
            snapshot.GetPreflightTypes,
            snapshot.GetManifest,
            snapshot.GetModuleType,
            diagnosticSink);
    }

    private static PluginModuleCatalog DiscoverCore(
        IReadOnlyList<Assembly> assemblies,
        Func<Assembly, IReadOnlyList<Type>> getTypes,
        Func<Assembly, PluginManifest?> getManifest,
        Func<Assembly, Type?> getPreflightModuleType,
        IHostDiagnosticSink? diagnosticSink)
    {
        var entries = new List<PluginModuleEntry>();
        var diagnostics = new List<HostCompositionDiagnostic>();

        foreach (var assembly in assemblies)
        {
            var loadableTypes = getTypes(assembly);
            var moduleType = getPreflightModuleType(assembly);
            if (moduleType is null && !PluginModulePreflight.TryValidate(
                    loadableTypes,
                    out moduleType,
                    out var moduleErrorCode,
                    out _))
            {
                diagnostics.Add(Diagnostic(
                    moduleErrorCode!,
                    assembly.GetName().Name,
                    loadableTypes.Where(type =>
                        typeof(IPluginModule).IsAssignableFrom(type) &&
                        !type.IsAbstract &&
                        !type.IsInterface)));
                continue;
            }

            try
            {
                var module = (IPluginModule)Activator.CreateInstance(moduleType!)!;
                entries.Add(new PluginModuleEntry(
                    module,
                    moduleType!,
                    assembly,
                    getManifest(assembly)));
            }
            catch (Exception exception)
            {
                diagnostics.Add(Diagnostic(
                    "PLUGIN_MODULE_ACTIVATION_FAILED",
                    assembly.GetName().Name,
                    [moduleType!]));
                diagnosticSink?.Report(new HostDiagnosticDraft(
                    "PLUGIN_MODULE_ACTIVATION_FAILED",
                    HostDiagnosticPhase.PluginModuleDiscovery,
                    "插件模块无法通过公共无参构造创建。")
                {
                    AssemblyName = assembly.GetName().Name,
                    StableId = moduleType!.FullName,
                    Exception = exception,
                });
            }
        }

        if (diagnostics.Count > 0)
        {
            throw new HostCompositionException(diagnostics);
        }

        return new PluginModuleCatalog(entries
            .OrderBy(entry => entry.Manifest?.PluginId.Value ?? entry.Assembly.FullName,
                StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>
    /// 在每个 manifest 身份下执行唯一一次模块配置，并封闭对应注册上下文。
    /// </summary>
    internal void Configure(
        IServiceCollection services,
        PluginRegistryBuilder registryBuilder,
        IHostDiagnosticSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registryBuilder);
        ArgumentNullException.ThrowIfNull(diagnostics);

        foreach (var entry in Entries)
        {
            var manifest = entry.Manifest ?? throw new InvalidOperationException(
                "没有 manifest 的测试 Catalog 不能进入生产插件组合。");
            var context = new PluginRegistrationContext(
                manifest.PluginId,
                services,
                registryBuilder);
            try
            {
                entry.Module.Configure(context);
                var bypasses = context.SealAndGetBypassedContributionTypes();
                if (bypasses.Count > 0)
                {
                    throw new HostCompositionException(bypasses.Select(type =>
                        new HostCompositionDiagnostic(
                            "CONTRIBUTION_REGISTRATION_BYPASS",
                            manifest.PluginId.Value,
                            [ToContributor(type)])).ToArray());
                }
            }
            catch (Exception exception)
            {
                // IServiceCollection 没有事务能力；异常后由组合根丢弃整个集合，不能发布部分结果。
                context.SealAndGetBypassedContributionTypes();
                diagnostics.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.PluginServiceRegistrationFailed,
                    HostDiagnosticPhase.PluginServiceRegistration,
                    "插件显式注册失败，宿主已放弃本次容器构建。")
                {
                    PluginId = manifest.PluginId.Value,
                    AssemblyName = entry.Assembly.GetName().Name,
                    Exception = exception,
                });
                if (exception is HostCompositionException)
                {
                    throw;
                }

                throw new HostCompositionException([
                    new HostCompositionDiagnostic(
                        HostDiagnosticCodes.PluginServiceRegistrationFailed,
                        manifest.PluginId.Value,
                        [ToContributor(entry.ModuleType)])
                ]);
            }
        }
    }

    internal IReadOnlyList<PluginRegistryPlugin> CreatePluginSnapshots(
        IEnumerable<PluginRegistryBuilder.StrategyDeclaration> documents,
        IEnumerable<PluginRegistryBuilder.StrategyDeclaration> tools,
        IEnumerable<PluginRegistryBuilder.ViewDeclaration> views,
        IEnumerable<PluginRegistryBuilder.StrategyDeclaration> lifecycles)
    {
        var result = new List<PluginRegistryPlugin>();
        foreach (var entry in Entries)
        {
            if (entry.Manifest is not { } manifest)
            {
                continue;
            }
            result.Add(new PluginRegistryPlugin(
                manifest,
                entry.Assembly,
                entry.ModuleType,
                documents.Where(item => item.OwnerId == manifest.PluginId)
                    .Select(item => item.ImplementationType).ToArray(),
                tools.Where(item => item.OwnerId == manifest.PluginId)
                    .Select(item => item.ImplementationType).ToArray(),
                views.Where(item => item.OwnerId == manifest.PluginId)
                    .Select(item => new PluginViewTypePair(item.ViewModelType, item.ViewType)).ToArray(),
                lifecycles.Where(item => item.OwnerId == manifest.PluginId)
                    .Select(item => item.ImplementationType).ToArray()));
        }

        return result;
    }

    private static HostCompositionDiagnostic Diagnostic(
        string code,
        string? stableId,
        IEnumerable<Type> types) =>
        new(code, stableId, types.Select(ToContributor).ToArray());

    private static HostCompositionContributor ToContributor(Type type) =>
        new(type.FullName ?? type.Name, type.Assembly.GetName().Name ?? "Unknown");

    private static string FormatLoaderErrors(Exception exception) =>
        exception is ReflectionTypeLoadException typeLoadException
            ? string.Join(" | ", typeLoadException.LoaderExceptions
                .Where(item => item is not null)
                .Select(item => item!.Message.Replace(Environment.NewLine, " ", StringComparison.Ordinal)))
            : exception.Message.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
}

internal sealed record PluginModuleEntry(
    IPluginModule Module,
    Type ModuleType,
    Assembly Assembly,
    PluginManifest? Manifest);
