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
public class PluginLoadContext : AssemblyLoadContext
{
    private static readonly IPluginSharedAssemblyPolicy SharedAssemblyPolicy =
        new HostContractAssemblyPolicy();

    private readonly PluginDirectoryLayout _layout;
    private readonly AssemblyDependencyResolver? _dependencyResolver;

    /// <summary>
    /// 为指定插件目录创建不可回收加载上下文。
    /// </summary>
    /// <param name="pluginPath">插件独占部署目录，而不是单个 DLL 路径。</param>
    /// <exception cref="InvalidOperationException">目录不满足标准入口或 Legacy 回退约定。</exception>
    public PluginLoadContext(string pluginPath)
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
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        SharedPolicy = sharedAssemblyPolicy ??
                       throw new ArgumentNullException(nameof(sharedAssemblyPolicy));
        _dependencyResolver = layout.MainAssemblyPath is { } mainAssemblyPath
            ? new AssemblyDependencyResolver(mainAssemblyPath)
            : null;
    }

    private IPluginSharedAssemblyPolicy SharedPolicy { get; }

    /// <summary>
    /// 尝试解析指定程序集名称，保留历史 public 辅助入口。
    /// </summary>
    /// <param name="assemblyName">程序集完整名称或简单名称。</param>
    /// <returns>当前插件或宿主共享上下文中的程序集；无法解析时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 设计意图：生产依赖加载由 CLR 自动调用 <see cref="Load"/>；本方法仅用于兼容既有探测和测试代码。
    /// 它不会遍历其他插件上下文，也不会注册全局解析事件。
    /// </remarks>
    public Assembly? ResolveAssembly(string assemblyName)
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
        try
        {
            // 设计意图：共享契约必须先于插件私有解析，以保证跨边界类型只有一个 CLR 身份。
            // 若共享版本不兼容必须立即失败，不能加载插件副本形成难以诊断的类型转换错误。
            if (SharedPolicy.IsShared(assemblyName))
            {
                return SharedPolicy.ResolveSharedAssembly(assemblyName);
            }

            var assemblyPath = _dependencyResolver?.ResolveAssemblyToPath(assemblyName)
                               ?? _layout.ResolveAssemblyPath(assemblyName);
            return assemblyPath is null
                ? null
                : LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception exception) when (
            exception is FileLoadException or BadImageFormatException)
        {
            Console.Error.WriteLine(
                $"PluginLoad errorCode={GetErrorCode(exception)} plugin={Path.GetFileName(_layout.DirectoryPath)} requested={assemblyName.FullName} stage=ResolveManaged type={exception.GetType().Name}");
            throw;
        }
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        // 设计意图：原生资产只接受当前插件 deps/RID 图给出的确定路径，禁止递归搜索其他插件目录。
        var libraryPath = _dependencyResolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null
            ? nint.Zero
            : LoadUnmanagedDllFromPath(libraryPath);
    }

    private static PluginDirectoryLayout CreateLayout(string pluginPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginPath);
        if (PluginDirectoryLayout.TryCreate(
                pluginPath,
                out var layout,
                out var errorCode,
                out var errorDetail))
        {
            return layout!;
        }

        throw new InvalidOperationException(
            $"{errorCode}: {errorDetail}");
    }

    private static string GetErrorCode(Exception exception) =>
        exception.Message.Contains(
            "PLUGIN_SHARED_ASSEMBLY_MISMATCH",
            StringComparison.Ordinal)
            ? "PLUGIN_SHARED_ASSEMBLY_MISMATCH"
            : "PLUGIN_ASSEMBLY_LOAD_FAILED";
}
