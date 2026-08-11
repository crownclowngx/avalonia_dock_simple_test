using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 定义插件加载上下文与宿主默认上下文之间唯一允许的程序集共享策略。
/// </summary>
/// <remarks>
/// 设计意图：接口只暴露“是否共享”和“取得共享实例”两个决定，调用方不需要知道共享集合如何生成；
/// 将来若公共 SDK 拆包，只需替换策略实现，不必修改插件目录扫描或 ALC 的解析流程。
/// </remarks>
internal interface IPluginSharedAssemblyPolicy
{
    bool IsShared(AssemblyName requestedAssembly);

    Assembly ResolveSharedAssembly(AssemblyName requestedAssembly);
}

/// <summary>
/// 以 <c>MyAvaloniaManagementCommon</c> 为根，复用其在默认上下文中的完整依赖闭包。
/// </summary>
/// <remarks>
/// 设计意图：公共契约中出现的类型必须来自同一个程序集实例，否则即使命名空间和类型名完全相同，
/// CLR 仍会把它们视为不同类型。普通第三方依赖不在此闭包中，默认上下文即使碰巧加载过它，
/// 插件也仍可优先使用自己目录中的版本。
/// </remarks>
internal sealed class HostContractAssemblyPolicy : IPluginSharedAssemblyPolicy
{
    private readonly IReadOnlyDictionary<string, Assembly> _sharedAssemblies;

    internal HostContractAssemblyPolicy()
    {
        _sharedAssemblies = BuildSharedAssemblyClosure(typeof(IPluginModule).Assembly);
    }

    public bool IsShared(AssemblyName requestedAssembly) =>
        requestedAssembly.Name is { } name && _sharedAssemblies.ContainsKey(name);

    public Assembly ResolveSharedAssembly(AssemblyName requestedAssembly)
    {
        if (requestedAssembly.Name is not { } name ||
            !_sharedAssemblies.TryGetValue(name, out var sharedAssembly))
        {
            throw new FileNotFoundException(
                $"程序集 {requestedAssembly.FullName} 不属于宿主共享契约。",
                requestedAssembly.FullName);
        }

        var sharedName = sharedAssembly.GetName();
        if (!HasCompatibleIdentity(requestedAssembly, sharedName))
        {
            throw new FileLoadException(
                $"PLUGIN_SHARED_ASSEMBLY_MISMATCH: 请求 {requestedAssembly.FullName}，宿主提供 {sharedName.FullName}。",
                requestedAssembly.FullName);
        }

        return sharedAssembly;
    }

    private static IReadOnlyDictionary<string, Assembly> BuildSharedAssemblyClosure(
        Assembly rootAssembly)
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<Assembly>();
        pending.Enqueue(rootAssembly);

        while (pending.TryDequeue(out var assembly))
        {
            var name = assembly.GetName().Name;
            if (name is null || assemblies.ContainsKey(name))
            {
                continue;
            }

            var loadContext = AssemblyLoadContext.GetLoadContext(assembly);
            if (loadContext != AssemblyLoadContext.Default)
            {
                throw new InvalidOperationException(
                    $"宿主共享程序集 {assembly.FullName} 未加载到默认上下文。");
            }

            assemblies.Add(name, assembly);
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                try
                {
                    var dependency = AssemblyLoadContext.Default.LoadFromAssemblyName(reference);
                    pending.Enqueue(dependency);
                }
                catch (FileNotFoundException)
                {
                    // 可选引用未部署时不把它伪造为共享契约；真正请求该引用时仍由标准加载流程给出失败。
                }
                catch (FileLoadException)
                {
                    // 默认上下文已确定版本但不兼容时保持现状，避免策略初始化掩盖后续的精确请求信息。
                }
            }
        }

        return assemblies;
    }

    private static bool HasCompatibleIdentity(
        AssemblyName requested,
        AssemblyName provided)
    {
        if (!string.Equals(requested.Name, provided.Name, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                NormalizeCulture(requested.CultureName),
                NormalizeCulture(provided.CultureName),
                StringComparison.OrdinalIgnoreCase) ||
            !GetPublicKeyToken(requested).SequenceEqual(GetPublicKeyToken(provided)))
        {
            return false;
        }

        // ALC 的版本规则允许已加载版本等于或高于请求版本；共享契约沿用同一规则并显式拒绝降级。
        return requested.Version is null ||
               provided.Version is not null && provided.Version >= requested.Version;
    }

    private static string NormalizeCulture(string? cultureName) =>
        string.IsNullOrWhiteSpace(cultureName)
            ? CultureInfo.InvariantCulture.Name
            : cultureName;

    private static byte[] GetPublicKeyToken(AssemblyName assemblyName) =>
        assemblyName.GetPublicKeyToken() ?? [];
}
