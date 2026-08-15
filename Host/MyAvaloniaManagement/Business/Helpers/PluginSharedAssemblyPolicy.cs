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
/// 以基础 Plugin SDK 和宿主支持的 UI Profile 为根，复用默认上下文中的依赖闭包。
/// </summary>
/// <remarks>
/// 设计意图：公共契约中出现的类型必须来自同一个程序集实例，否则即使命名空间和类型名完全相同，
/// CLR 仍会把它们视为不同类型。Semi、Ursa 与 Dock UI 只有被列入受支持 Profile 后才共享；
/// 普通第三方业务依赖不在此闭包中，即使默认上下文碰巧加载过，插件仍优先使用自己目录的版本。
/// </remarks>
internal sealed class HostContractAssemblyPolicy : IPluginSharedAssemblyPolicy
{
    /// <summary>
    /// UI Profile 的运行时程序集根。
    /// </summary>
    /// <remarks>
    /// 设计意图：基础 SDK 不引用主题实现，因此共享集合不能再靠 Common 的偶然传递依赖生成。
    /// 这里列的是宿主明确承诺并直接部署的 UI 家族；各根的依赖仍由闭包算法自动发现，避免维护
    /// 一份脆弱的传递 DLL 清单。新增普通插件依赖不得加入此处，否则会破坏插件私有版本隔离。
    /// </remarks>
    private static readonly string[] SupportedUiProfileAssemblyNames =
    [
        "Avalonia.Themes.Fluent",
        "Dock.Avalonia",
        "Dock.Avalonia.Themes.Fluent",
        "Dock.Controls.ProportionalStackPanel",
        "Dock.Controls.Recycling",
        "Dock.Controls.Recycling.Model",
        "Semi.Avalonia",
        "Ursa",
        "Ursa.Themes.Semi",
    ];

    private readonly IReadOnlyDictionary<string, Assembly> _sharedAssemblies;

    internal HostContractAssemblyPolicy()
    {
        var roots = new List<Assembly>
        {
            typeof(IPluginModule).Assembly,
        };

        foreach (var assemblyName in SupportedUiProfileAssemblyNames)
        {
            // Host 对 Profile 包具有直接引用；缺少任一程序集表示发布包本身损坏，
            // 应在加载插件前失败，而不是让某个插件运行到 XAML 解析阶段才报错。
            roots.Add(AssemblyLoadContext.Default.LoadFromAssemblyName(
                new AssemblyName(assemblyName)));
        }

        _sharedAssemblies = BuildSharedAssemblyClosure(roots);
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
        IEnumerable<Assembly> rootAssemblies)
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<Assembly>();
        foreach (var rootAssembly in rootAssemblies)
        {
            pending.Enqueue(rootAssembly);
        }

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
