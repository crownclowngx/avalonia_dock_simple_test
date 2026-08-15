using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 为单个插件目录提供独立的托管程序集与原生库加载上下文。
/// </summary>
/// <remarks>
/// 设计意图：一个实例只认识一个插件目录。公共契约显式复用默认上下文，私有依赖只从本目录解析，
/// 从结构上禁止通过简单程序集名访问其他插件。当前产品采用“重启更新”，因此上下文有意保持不可回收；
/// 这不是安全沙箱，也不能隔离原生崩溃或进程级全局状态。
/// </remarks>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly IPluginSharedAssemblyPolicy SharedAssemblyPolicy =
        new HostContractAssemblyPolicy();

    private readonly AssemblyDependencyResolver _dependencyResolver;

    /// <summary>
    /// 为指定插件目录创建不可回收加载上下文。
    /// </summary>
    /// <param name="pluginPath">插件独占部署目录，而不是单个 DLL 路径。</param>
    /// <exception cref="InvalidOperationException">目录缺少有效清单、版本不兼容或不满足清单入口约定。</exception>
    internal PluginLoadContext(string pluginPath)
        : this(CreateLayout(pluginPath), SharedAssemblyPolicy)
    {
    }

    internal PluginLoadContext(
        PluginDirectoryLayout layout,
        IPluginSharedAssemblyPolicy sharedAssemblyPolicy)
        : base(
            $"Plugin:{Path.GetFileName(layout.DirectoryPath)}",
            isCollectible: false)
    {
        ArgumentNullException.ThrowIfNull(layout);
        SharedPolicy = sharedAssemblyPolicy ??
                       throw new ArgumentNullException(nameof(sharedAssemblyPolicy));
        // PluginDirectoryLayout 已经保证入口与 deps 同时存在。这里保持非空解析器，
        // 从类型结构上消除“有 deps/无 deps”两套依赖算法重新分叉的可能。
        _dependencyResolver = new AssemblyDependencyResolver(layout.EntryAssemblyPath);
    }

    private IPluginSharedAssemblyPolicy SharedPolicy { get; }

    /// <summary>
    /// 尝试按当前插件的共享策略和 deps 图解析指定程序集名称。
    /// </summary>
    /// <param name="assemblyName">程序集完整名称或简单名称。</param>
    /// <returns>当前插件或宿主共享上下文中的程序集；无法解析时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 设计意图：生产依赖加载由 CLR 自动调用 <see cref="Load"/>；该探测入口供宿主验证和测试
    /// 同一套解析边界。它不会遍历目录、其他插件上下文，也不会注册全局解析事件。
    /// </remarks>
    internal Assembly? ResolveAssembly(string assemblyName)
    {
        try
        {
            // 直接调用当前上下文的解析策略；若返回 null，不允许 LoadFromAssemblyName
            // 再回落到默认上下文，以保持该探测方法“只检查当前插件边界”的历史语义。
            return Load(new AssemblyName(assemblyName));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 设计意图：加载上下文只实现解析规则，不承担诊断展示或失败策略。
        // CLR 抛出的异常会由目录预检、模块注册等有明确插件身份的阶段统一记录。
        if (SharedPolicy.IsShared(assemblyName))
        {
            return SharedPolicy.ResolveSharedAssembly(assemblyName);
        }

        var assemblyPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null
            ? null
            : LoadFromAssemblyPath(assemblyPath);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        // 设计意图：原生资产只接受当前插件 deps/RID 图给出的确定路径，禁止递归搜索其他插件目录。
        var libraryPath = _dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null
            ? nint.Zero
            : LoadUnmanagedDllFromPath(libraryPath);
    }

    private static PluginDirectoryLayout CreateLayout(string pluginPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        if (!PluginManifestReader.TryRead(
                pluginPath,
                out var manifest,
                out var manifestErrorCode,
                out var manifestErrorDetail))
        {
            throw new InvalidOperationException(
                $"{manifestErrorCode}: {manifestErrorDetail}");
        }

        if (!PluginCompatibilityEvaluator.TryEvaluate(
                manifest!,
                HostCompatibilityProfile.Current,
                out var compatibilityErrorCode,
                out var compatibilityErrorDetail))
        {
            throw new InvalidOperationException(
                $"{compatibilityErrorCode}: {compatibilityErrorDetail}");
        }

        if (PluginDirectoryLayout.TryCreate(
                pluginPath,
                manifest!,
                out var layout,
                out var errorCode,
                out var errorDetail))
        {
            return layout!;
        }

        throw new InvalidOperationException(
            $"{errorCode}: {errorDetail}");
    }
}
