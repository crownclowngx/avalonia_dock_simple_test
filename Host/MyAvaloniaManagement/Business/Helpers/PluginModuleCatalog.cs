using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 激活严格 manifest v2 已经精确指定并预检的 Managed Plugin 模块。
/// </summary>
/// <remarks>
/// Catalog 只回答“哪个清单对应哪个模块”，不发现入口、Document、Tool 或 View。入口类型完全来自
/// 加载阶段形成的不可变快照，Catalog 不提供无清单扫描旁路。
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

    /// <summary>复用插件目录阶段已经完成的精确入口预检结果激活模块。</summary>
    internal static PluginModuleCatalog Discover(
        PluginDiscoverySnapshot snapshot,
        IHostDiagnosticSink? diagnosticSink = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var entries = new List<PluginModuleEntry>();
        var diagnostics = new List<HostCompositionDiagnostic>();

        foreach (var assembly in snapshot.Assemblies)
        {
            var moduleType = snapshot.GetModuleType(assembly);
            try
            {
                var module = (IPluginModule)Activator.CreateInstance(moduleType)!;
                entries.Add(new PluginModuleEntry(
                    module,
                    moduleType,
                    assembly,
                    snapshot.GetManifest(assembly)));
            }
            catch (Exception exception)
            {
                diagnostics.Add(Diagnostic(
                    "PLUGIN_MODULE_ACTIVATION_FAILED",
                    assembly.GetName().Name,
                    [moduleType]));
                diagnosticSink?.Report(new HostDiagnosticDraft(
                    "PLUGIN_MODULE_ACTIVATION_FAILED",
                    HostDiagnosticPhase.PluginModuleDiscovery)
                {
                    AssemblyName = assembly.GetName(),
                    StableId = moduleType.FullName,
                    Exception = exception,
                });
            }
        }

        if (diagnostics.Count > 0)
        {
            throw new HostCompositionException(diagnostics);
        }

        return new PluginModuleCatalog(entries
            .OrderBy(entry => entry.Manifest!.PluginId.Value, StringComparer.Ordinal)
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

        // 保护基线只捕获一次宿主服务。每个插件仍会从“宿主 + 已提交的前序插件”建立工作副本，
        // 因而不能删除或重排前序注册，但可继续为自己的私有接口追加多个实现。
        var protectionPolicy = HostServiceDescriptorPolicy.Capture(services);

        foreach (var entry in Entries)
        {
            var manifest = entry.Manifest ?? throw new InvalidOperationException(
                "manifest v2 是生产插件组合的必需入口事实。");
            var registration = new PluginServiceRegistrationTransaction(services);
            var context = new PluginRegistrationContext(
                manifest.PluginId,
                registration.Services,
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
                    HostDiagnosticPhase.PluginServiceRegistration)
                {
                    PluginId = manifest.PluginId,
                    AssemblyName = entry.Assembly.GetName(),
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

            if (!registration.TryCommit(protectionPolicy, out var violation))
            {
                var serviceType = violation!.Descriptor.ServiceType;
                diagnostics.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.PluginHostServiceMutation,
                    HostDiagnosticPhase.PluginServiceRegistration)
                {
                    PluginId = manifest.PluginId,
                    AssemblyName = entry.Assembly.GetName(),
                    StableId = serviceType.FullName ?? serviceType.Name,
                });

                throw new HostCompositionException([
                    new HostCompositionDiagnostic(
                        HostDiagnosticCodes.PluginHostServiceMutation,
                        manifest.PluginId.Value,
                        [ToContributor(serviceType)])
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

}

internal sealed record PluginModuleEntry(
    IPluginModule Module,
    Type ModuleType,
    Assembly Assembly,
    PluginManifest? Manifest);
