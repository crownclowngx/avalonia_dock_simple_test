using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 从部署目录加载插件程序集的兼容 Facade，保留既有 public 调用方式。
/// 内部按规范化根目录建立线程安全快照，保证同一目录只扫描一次。
/// </summary>
public static class AssemblyLoaderHelper
{
    private static readonly HashSet<string> SkippedPluginDirectories = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "native",
        "runtimes",
        "libvlc"
    };

    private static readonly ConcurrentDictionary<string, PluginLoadContext> PluginContexts =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Assembly> LoadedAssemblies =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<Assembly>>> RootSnapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LoadGate = new();
    private static int _assemblyResolveHandlerRegistered;

    /// <summary>
    /// 加载根目录下的全部插件项目，每个插件目录使用独立的程序集加载上下文。
    /// 隔离上下文可以避免不同插件的私有依赖互相污染，同时保持原有返回类型。
    /// </summary>
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

        // 返回快照副本既保留 public List<Assembly> 契约，也避免调用方修改内部缓存。
        return snapshot.Value.ToList();
    }

    private static IReadOnlyList<Assembly> LoadRootSnapshot(string rootPath)
    {
        lock (LoadGate)
        {
            RegisterAssemblyResolveHandler();
            var loaded = new List<Assembly>();

            try
            {
                if (!Directory.Exists(rootPath))
                {
                    Directory.CreateDirectory(rootPath);
                    return loaded;
                }

                foreach (var pluginDirectory in Directory.GetDirectories(rootPath))
                {
                    var fullPluginPath = Path.GetFullPath(pluginDirectory);
                    var loadContext = PluginContexts.GetOrAdd(
                        fullPluginPath,
                        static path => new PluginLoadContext(path));
                    loaded.AddRange(LoadAssembliesRecursively(
                        fullPluginPath,
                        loadContext));
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"PluginLoad errorCode=PLUGIN_ROOT_SCAN_FAILED type={exception.GetType().Name}");
            }

            return loaded.ToArray();
        }
    }

    private static List<Assembly> LoadAssembliesRecursively(
        string directoryPath,
        PluginLoadContext loadContext)
    {
        var loaded = new List<Assembly>();

        try
        {
            foreach (var dllPath in Directory.GetFiles(directoryPath, "*.dll"))
            {
                var assemblyName = Path.GetFileNameWithoutExtension(dllPath);
                if (LoadedAssemblies.ContainsKey(assemblyName))
                {
                    continue;
                }

                try
                {
                    var assembly = loadContext.LoadFromAssemblyPath(
                        Path.GetFullPath(dllPath));
                    if (LoadedAssemblies.TryAdd(assemblyName, assembly))
                    {
                        loaded.Add(assembly);
                    }
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"PluginLoad errorCode=PLUGIN_ASSEMBLY_LOAD_FAILED assembly={Path.GetFileName(dllPath)} type={exception.GetType().Name}");
                }
            }

            foreach (var subdirectory in Directory.GetDirectories(directoryPath))
            {
                if (SkippedPluginDirectories.Contains(Path.GetFileName(subdirectory)))
                {
                    continue;
                }

                loaded.AddRange(LoadAssembliesRecursively(subdirectory, loadContext));
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"PluginLoad errorCode=PLUGIN_DIRECTORY_SCAN_FAILED type={exception.GetType().Name}");
        }

        return loaded;
    }

    private static void RegisterAssemblyResolveHandler()
    {
        if (Interlocked.Exchange(ref _assemblyResolveHandlerRegistered, 1) == 0)
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainAssemblyResolve;
        }
    }

    private static Assembly? CurrentDomainAssemblyResolve(
        object? sender,
        ResolveEventArgs args)
    {
        try
        {
            var assemblyName = new AssemblyName(args.Name).Name;
            if (assemblyName is null)
            {
                return null;
            }

            if (LoadedAssemblies.TryGetValue(assemblyName, out var loadedAssembly))
            {
                return loadedAssembly;
            }

            foreach (var pluginContext in PluginContexts.Values)
            {
                try
                {
                    if (pluginContext.ResolveAssembly(args.Name) is { } resolved)
                    {
                        return resolved;
                    }
                }
                catch
                {
                // 单个插件上下文失败只跳过该插件，不能阻断其他插件的发现。
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"PluginLoad errorCode=PLUGIN_ASSEMBLY_RESOLVE_FAILED type={exception.GetType().Name}");
        }

        return null;
    }

    /// <summary>
    /// 加载部署子目录正下方的托管程序集，保留历史辅助方法的调用语义。
    /// </summary>
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
