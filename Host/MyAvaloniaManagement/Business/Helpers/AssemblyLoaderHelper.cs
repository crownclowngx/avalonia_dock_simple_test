using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 从部署目录加载插件入口程序集的兼容 Facade，保留既有 public 调用方式。
/// </summary>
/// <remarks>
/// 设计意图：调用方只需要插件根目录名称，不需要了解入口约定、共享契约策略和 ALC 解析细节。
/// 同一规范化根目录只生成一次不可变快照，但快照内部不再维护跨插件程序集名称缓存，
/// 因而两个插件可以各自在自己的上下文中加载同名不同版本私有依赖。
/// </remarks>
public static class AssemblyLoaderHelper
{
    private static readonly IPluginSharedAssemblyPolicy SharedAssemblyPolicy =
        new HostContractAssemblyPolicy();

    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<Assembly>>> RootSnapshots =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 加载根目录下的全部插件项目，每个插件目录使用独立的程序集加载上下文。
    /// </summary>
    /// <remarks>
    /// 标准插件只主动加载唯一 deps 入口，私有依赖随后由该插件的
    /// <see cref="PluginLoadContext"/> 按需解析；没有 deps 文件的历史目录继续使用有序 DLL 回退。
    /// 返回快照副本，避免调用方修改内部缓存。
    /// </remarks>
    public static List<Assembly> LoadPluginsFromDirectories(string rootPluginsDirName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPluginsDirName);

        var rootPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, rootPluginsDirName));
        var snapshot = RootSnapshots.GetOrAdd(
            rootPath,
            static path => new Lazy<IReadOnlyList<Assembly>>(
                () => LoadRootSnapshot(path),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return snapshot.Value.ToList();
    }

    private static IReadOnlyList<Assembly> LoadRootSnapshot(string rootPath)
    {
        var loaded = new List<Assembly>();

        try
        {
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
                return loaded;
            }

            foreach (var pluginDirectory in Directory.GetDirectories(rootPath)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                LoadPluginDirectory(pluginDirectory, loaded);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"PluginLoad errorCode=PLUGIN_ROOT_SCAN_FAILED type={exception.GetType().Name}");
        }

        return loaded.ToArray();
    }

    private static void LoadPluginDirectory(
        string pluginDirectory,
        ICollection<Assembly> loaded)
    {
        var pluginName = Path.GetFileName(pluginDirectory);
        if (!PluginDirectoryLayout.TryCreate(
                pluginDirectory,
                out var layout,
                out var errorCode,
                out var errorDetail))
        {
            Console.Error.WriteLine(
                $"PluginLoad errorCode={errorCode} plugin={pluginName} stage=DiscoverEntry detail={errorDetail}");
            return;
        }

        var loadContext = new PluginLoadContext(layout!, SharedAssemblyPolicy);
        foreach (var entryAssemblyPath in layout!.EntryAssemblyPaths)
        {
            try
            {
                loaded.Add(loadContext.LoadFromAssemblyPath(entryAssemblyPath));
            }
            catch (Exception exception)
            {
                // 设计意图：插件目录是最小失败隔离单元。一个入口失败不能阻止后续目录被发现，
                // 但同一目录不再尝试从其他插件借用同名依赖来“修复”当前失败。
                Console.Error.WriteLine(
                    $"PluginLoad errorCode=PLUGIN_ASSEMBLY_LOAD_FAILED plugin={pluginName} assembly={Path.GetFileName(entryAssemblyPath)} stage=LoadEntry type={exception.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// 加载部署子目录正下方的托管程序集，保留历史辅助方法的调用语义。
    /// </summary>
    /// <remarks>
    /// 设计意图：该方法不是插件隔离入口，仅为既有非插件调用保留；插件代码必须使用
    /// <see cref="LoadPluginsFromDirectories"/>，避免将私有依赖装入默认上下文。
    /// </remarks>
    public static List<Assembly> LoadAssembliesFromSubdirectory(string subdirectoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subdirectoryName);
        var loaded = new List<Assembly>();

        try
        {
            var directory = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, subdirectoryName));
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                return loaded;
            }

            foreach (var dllPath in Directory.GetFiles(directory, "*.dll"))
            {
                try
                {
                    loaded.Add(Assembly.LoadFrom(dllPath));
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"PluginLoad errorCode=SUBDIRECTORY_ASSEMBLY_LOAD_FAILED assembly={Path.GetFileName(dllPath)} type={exception.GetType().Name}");
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"PluginLoad errorCode=SUBDIRECTORY_SCAN_FAILED type={exception.GetType().Name}");
        }

        return loaded;
    }
}
