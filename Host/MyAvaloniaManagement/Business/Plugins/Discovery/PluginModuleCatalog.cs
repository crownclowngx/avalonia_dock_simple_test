using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Plugins.Discovery;

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
    }

    internal IReadOnlyList<PluginModuleEntry> Entries { get; }

    /// <summary>为容器所有权测试建立不经过磁盘加载的精确模块目录。</summary>
    /// <remarks>
    /// 测试仍必须显式提供 PluginId 与模块实例；本入口不扫描程序集，也不用于生产启动。
    /// 它允许多个测试模块位于同一程序集，同时保持生产 Catalog 的排序和 manifest 身份语义。
    /// </remarks>
    internal static PluginModuleCatalog CreateForTests(
        IEnumerable<(PluginId PluginId, IPluginModule Module)> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var entries = modules.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item.PluginId);
            ArgumentNullException.ThrowIfNull(item.Module);
            var moduleType = item.Module.GetType();
            var assembly = moduleType.Assembly;
            return new PluginModuleEntry(
                () => item.Module,
                moduleType,
                assembly,
                    new PluginManifest(
                        PluginManifestReader.CurrentSchemaVersion,
                        item.PluginId,
                    new Version(1, 0, 0, 0),
                    new PluginEntryPoint(
                        (assembly.GetName().Name ?? "TestPlugin") + ".dll",
                        moduleType.FullName ?? moduleType.Name),
                    new PluginVersionRange(
                        new Version(2, 0, 0, 0),
                        new Version(3, 0, 0, 0))));
        }).OrderBy(entry => entry.Manifest!.PluginId.Value, StringComparer.Ordinal).ToArray();
        return new PluginModuleCatalog(entries);
    }

    /// <summary>为模块公共构造失败隔离测试建立延迟构造目录。</summary>
    internal static PluginModuleCatalog CreateForTests(
        IEnumerable<(PluginId PluginId, Type ModuleType)> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var entries = modules.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item.PluginId);
            ArgumentNullException.ThrowIfNull(item.ModuleType);
            var assembly = item.ModuleType.Assembly;
            return new PluginModuleEntry(
                () => (IPluginModule)Activator.CreateInstance(item.ModuleType)!,
                item.ModuleType,
                assembly,
                    new PluginManifest(
                        PluginManifestReader.CurrentSchemaVersion,
                        item.PluginId,
                    new Version(1, 0, 0, 0),
                    new PluginEntryPoint(
                        (assembly.GetName().Name ?? "TestPlugin") + ".dll",
                        item.ModuleType.FullName ?? item.ModuleType.Name),
                    new PluginVersionRange(
                        new Version(2, 0, 0, 0),
                        new Version(3, 0, 0, 0))));
        }).OrderBy(entry => entry.Manifest!.PluginId.Value, StringComparer.Ordinal).ToArray();
        return new PluginModuleCatalog(entries);
    }

    /// <summary>复用插件目录阶段已经完成的精确入口预检结果建立延迟模块目录。</summary>
    /// <remarks>
    /// Catalog 不构造模块、不执行 Configure、也不拥有 Provider。模块构造被延迟到 Host Provider
    /// 建立之后，由插件 Provider 所有者执行并隔离失败，保持启动顺序和对象图所有权一致。
    /// </remarks>
    internal static PluginModuleCatalog Discover(PluginDiscoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var entries = new List<PluginModuleEntry>();

        foreach (var assembly in snapshot.Assemblies)
        {
            var moduleType = snapshot.GetModuleType(assembly);
            entries.Add(new PluginModuleEntry(
                () => (IPluginModule)Activator.CreateInstance(moduleType)!,
                moduleType,
                assembly,
                snapshot.GetManifest(assembly)));
        }

        return new PluginModuleCatalog(entries
            .OrderBy(entry => entry.Manifest!.PluginId.Value, StringComparer.Ordinal)
            .ToArray());
    }

    internal IReadOnlyList<PluginRegistryPlugin> CreatePluginSnapshots(
        IEnumerable<PluginDocumentRegistration> documents,
        IEnumerable<PluginToolRegistration> tools,
        IEnumerable<PluginLifecycleDeclaration> lifecycles,
        IReadOnlySet<PluginId>? availablePluginIds = null)
    {
        var result = new List<PluginRegistryPlugin>();
        foreach (var entry in Entries)
        {
            if (entry.Manifest is not { } manifest)
            {
                continue;
            }
            var ownerId = new PluginId(manifest.PluginId.Value);
            if (availablePluginIds is not null && !availablePluginIds.Contains(ownerId))
            {
                continue;
            }
            result.Add(new PluginRegistryPlugin(
                manifest,
                entry.Assembly,
                entry.ModuleType,
                documents.Where(item => item.OwnerId == ownerId)
                    .Select(item => item.ModelType).ToArray(),
                tools.Where(item => item.OwnerId == ownerId)
                    .Select(item => item.ModelType).ToArray(),
                documents.Where(item => item.OwnerId == ownerId)
                    .Select(item => new PluginViewTypePair(item.ModelType, item.ViewType))
                    .Concat(tools.Where(item => item.OwnerId == ownerId)
                        .Select(item => new PluginViewTypePair(item.ModelType, item.ViewType)))
                    .ToArray(),
                lifecycles.Where(item => item.OwnerId == ownerId)
                    .Select(item => item.ImplementationType).ToArray()));
        }

        return result;
    }

}

/// <summary>把已验证 manifest、精确模块类型和延迟构造函数绑定为一个目录项。</summary>
/// <remarks>延迟函数只构造 manifest 指定的模块；不会扫描程序集中的其他实现。</remarks>
internal sealed record PluginModuleEntry(
    Func<IPluginModule> CreateModule,
    Type ModuleType,
    Assembly Assembly,
    PluginManifest? Manifest);
