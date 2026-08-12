using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MyAvaloniaManagement.Business.Diagnostics;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 描述一次插件根目录发现的不可变结果。
/// </summary>
/// <remarks>
/// 设计意图：程序集、预检类型和失败诊断必须来自同一次目录快照，避免后续模块发现
/// 再次扫描文件系统后得到与程序集加载阶段不一致的事实。
/// </remarks>
internal sealed class PluginDiscoverySnapshot
{
    private readonly IReadOnlyDictionary<Assembly, IReadOnlyList<Type>> _typesByAssembly;

    internal PluginDiscoverySnapshot(
        IEnumerable<Assembly> assemblies,
        IReadOnlyDictionary<Assembly, IReadOnlyList<Type>> typesByAssembly,
        IEnumerable<HostDiagnosticDraft> diagnostics)
    {
        Assemblies = new ReadOnlyCollection<Assembly>(assemblies.ToArray());
        _typesByAssembly = new ReadOnlyDictionary<Assembly, IReadOnlyList<Type>>(
            new Dictionary<Assembly, IReadOnlyList<Type>>(typesByAssembly));
        Diagnostics = new ReadOnlyCollection<HostDiagnosticDraft>(diagnostics.ToArray());
    }

    internal IReadOnlyList<Assembly> Assemblies { get; }

    internal IReadOnlyList<HostDiagnosticDraft> Diagnostics { get; }

    internal IReadOnlyList<Type> GetPreflightTypes(Assembly assembly) =>
        _typesByAssembly[assembly];

    internal void PublishDiagnostics(IHostDiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        foreach (var diagnostic in Diagnostics)
        {
            sink.Report(diagnostic);
        }
    }
}

/// <summary>
/// 从部署目录加载插件入口程序集的兼容 Facade，保留既有 public 调用方式。
/// </summary>
/// <remarks>
/// 生产组合根使用包含诊断和类型预检结果的内部入口；历史调用仍只取得程序集列表。
/// 同一规范化根目录只生成一次不可变快照，且每个插件目录使用独立加载上下文，
/// 因而不同插件可以携带同名不同版本的私有依赖。
/// </remarks>
public static class AssemblyLoaderHelper
{
    private static readonly IPluginSharedAssemblyPolicy SharedAssemblyPolicy =
        new HostContractAssemblyPolicy();

    private static readonly ConcurrentDictionary<string, Lazy<PluginDiscoverySnapshot>> RootSnapshots =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 加载根目录下全部通过入口加载和完整类型预检的插件程序集。
    /// </summary>
    /// <remarks>
    /// 此兼容入口不暴露新的宿主诊断类型；错误继续镜像到 Console，生产宿主则通过
    /// 统一诊断会话消费同一份快照。
    /// </remarks>
    public static List<Assembly> LoadPluginsFromDirectories(string rootPluginsDirName)
    {
        var snapshot = Discover(rootPluginsDirName);
        foreach (var diagnostic in snapshot.Diagnostics)
        {
            Console.Error.WriteLine(
                $"PluginLoad errorCode={diagnostic.Code} plugin={diagnostic.PluginDirectory ?? "-"} stage={diagnostic.Phase} type={diagnostic.Exception?.GetType().Name ?? "-"}");
        }

        return snapshot.Assemblies.ToList();
    }

    /// <summary>
    /// 取得生产组合根使用的完整发现快照。
    /// </summary>
    internal static PluginDiscoverySnapshot Discover(string rootPluginsDirName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPluginsDirName);
        var rootPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, rootPluginsDirName));
        return RootSnapshots.GetOrAdd(
            rootPath,
            static path => new Lazy<PluginDiscoverySnapshot>(
                () => LoadRootSnapshot(path),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static PluginDiscoverySnapshot LoadRootSnapshot(string rootPath)
    {
        var loaded = new List<Assembly>();
        var typesByAssembly = new Dictionary<Assembly, IReadOnlyList<Type>>();
        var diagnostics = new List<HostDiagnosticDraft>();

        try
        {
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
                return new PluginDiscoverySnapshot(loaded, typesByAssembly, diagnostics);
            }

            foreach (var pluginDirectory in Directory.GetDirectories(rootPath)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                LoadPluginDirectory(pluginDirectory, loaded, typesByAssembly, diagnostics);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or
                NotSupportedException or System.Security.SecurityException)
        {
            diagnostics.Add(new HostDiagnosticDraft(
                HostDiagnosticCodes.PluginRootScanFailed,
                HostDiagnosticPhase.PluginRootDiscovery,
                "无法完成插件根目录扫描，宿主不能确认本次启动的插件集合。")
            {
                Exception = exception,
                TechnicalDetail = $"root={rootPath}{Environment.NewLine}{exception}",
            });
        }

        return new PluginDiscoverySnapshot(loaded, typesByAssembly, diagnostics);
    }

    private static void LoadPluginDirectory(
        string pluginDirectory,
        ICollection<Assembly> loaded,
        IDictionary<Assembly, IReadOnlyList<Type>> typesByAssembly,
        ICollection<HostDiagnosticDraft> diagnostics)
    {
        var pluginName = Path.GetFileName(pluginDirectory);
        if (!PluginDirectoryLayout.TryCreate(
                pluginDirectory,
                out var layout,
                out var errorCode,
                out var errorDetail))
        {
            diagnostics.Add(new HostDiagnosticDraft(
                errorCode ?? HostDiagnosticCodes.PluginEntryInvalid,
                HostDiagnosticPhase.PluginRootDiscovery,
                errorDetail ?? "插件目录不满足入口约定。")
            {
                PluginDirectory = pluginName,
                TechnicalDetail = $"directory={Path.GetFullPath(pluginDirectory)}",
            });
            return;
        }

        if (layout!.EntryAssemblyPaths.Count == 0)
        {
            diagnostics.Add(new HostDiagnosticDraft(
                HostDiagnosticCodes.PluginEntryInvalid,
                HostDiagnosticPhase.PluginRootDiscovery,
                "插件目录中没有可加载的托管入口程序集。")
            {
                PluginDirectory = pluginName,
                TechnicalDetail = $"directory={Path.GetFullPath(pluginDirectory)}",
            });
            return;
        }

        PluginLoadContext loadContext;
        try
        {
            loadContext = new PluginLoadContext(layout!, SharedAssemblyPolicy);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Add(CreateLoadFailure(
                pluginName,
                pluginDirectory,
                assemblyName: null,
                exception,
                HostDiagnosticPhase.PluginAssemblyLoad));
            return;
        }

        var candidateAssemblies = new List<Assembly>();
        foreach (var entryAssemblyPath in layout!.EntryAssemblyPaths)
        {
            try
            {
                candidateAssemblies.Add(loadContext.LoadFromAssemblyPath(entryAssemblyPath));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                diagnostics.Add(CreateLoadFailure(
                    pluginName,
                    pluginDirectory,
                    Path.GetFileNameWithoutExtension(entryAssemblyPath),
                    exception,
                    HostDiagnosticPhase.PluginAssemblyLoad));
                return;
            }
        }

        var candidateTypes = new Dictionary<Assembly, IReadOnlyList<Type>>();
        foreach (var assembly in candidateAssemblies)
        {
            try
            {
                // GetTypes 不会解析只出现在方法体中的程序集引用。预先解析完整引用表，
                // 才能在调用插件 ConfigureServices 前发现发布包遗漏的私有依赖。
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    _ = loadContext.LoadFromAssemblyName(reference);
                }

                candidateTypes.Add(assembly, assembly.GetTypes());
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                diagnostics.Add(new HostDiagnosticDraft(
                    PluginLoadExceptionMapper.GetCode(exception) == HostDiagnosticCodes.PluginSharedAssemblyMismatch
                        ? HostDiagnosticCodes.PluginSharedAssemblyMismatch
                        : exception is FileNotFoundException or FileLoadException or BadImageFormatException
                            ? HostDiagnosticCodes.PluginAssemblyLoadFailed
                            : HostDiagnosticCodes.PluginTypePreflightFailed,
                    HostDiagnosticPhase.PluginTypePreflight,
                    "插件程序集无法完成类型预检，已隔离整个插件目录。")
                {
                    PluginDirectory = pluginName,
                    AssemblyName = assembly.GetName().Name,
                    Exception = exception,
                    TechnicalDetail = FormatTypePreflightDetail(pluginDirectory, exception),
                });
                return;
            }
        }

        foreach (var assembly in candidateAssemblies)
        {
            loaded.Add(assembly);
            typesByAssembly.Add(assembly, candidateTypes[assembly]);
        }
    }

    private static HostDiagnosticDraft CreateLoadFailure(
        string pluginName,
        string pluginDirectory,
        string? assemblyName,
        Exception exception,
        HostDiagnosticPhase phase) =>
        new(
            PluginLoadExceptionMapper.GetCode(exception),
            phase,
            "插件入口程序集或其依赖加载失败，已隔离该插件目录。")
        {
            PluginDirectory = pluginName,
            AssemblyName = assemblyName,
            Exception = exception,
            TechnicalDetail = $"directory={Path.GetFullPath(pluginDirectory)}{Environment.NewLine}{exception}",
        };

    private static string FormatTypePreflightDetail(string pluginDirectory, Exception exception)
    {
        var loaderDetails = exception is ReflectionTypeLoadException reflection
            ? string.Join(
                Environment.NewLine,
                reflection.LoaderExceptions
                    .Where(item => item is not null)
                    .Select(item => item!.ToString()))
            : exception.ToString();
        return $"directory={Path.GetFullPath(pluginDirectory)}{Environment.NewLine}{loaderDetails}";
    }

    /// <summary>
    /// 加载部署子目录正下方的托管程序集，保留历史辅助方法的调用语义。
    /// </summary>
    /// <remarks>
    /// 该方法不是插件隔离入口，仅为既有非插件调用保留。
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
