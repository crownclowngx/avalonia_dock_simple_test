using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 描述一个插件目录中可以参与托管程序集加载的文件布局。
/// </summary>
/// <remarks>
/// 设计意图：把“哪个文件是插件入口”和“依赖名称对应哪个物理文件”的判断从
/// <see cref="PluginLoadContext"/> 中拆出，使加载上下文只负责解析顺序，不再同时承担目录扫描职责。
/// 入口只接受已经通过兼容预检的清单声明；私有依赖索引仍提供无 deps 包的确定性回退。
/// </remarks>
internal sealed class PluginDirectoryLayout
{
    private static readonly HashSet<string> SkippedDirectories = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "native",
        "runtimes",
        "libvlc"
    };

    private readonly IReadOnlyDictionary<string, string> _assemblyPaths;

    private PluginDirectoryLayout(
        string directoryPath,
        string? mainAssemblyPath,
        IReadOnlyList<string> entryAssemblyPaths,
        IReadOnlyDictionary<string, string> assemblyPaths)
    {
        DirectoryPath = directoryPath;
        MainAssemblyPath = mainAssemblyPath;
        EntryAssemblyPaths = entryAssemblyPaths;
        _assemblyPaths = assemblyPaths;
    }

    /// <summary>
    /// 规范化后的插件目录绝对路径。
    /// </summary>
    internal string DirectoryPath { get; }

    /// <summary>
    /// 用于创建 <see cref="System.Runtime.Loader.AssemblyDependencyResolver"/> 的主程序集。
    /// 入口没有同名 deps 文件时为 <see langword="null"/>。
    /// </summary>
    internal string? MainAssemblyPath { get; }

    /// <summary>
    /// 需要主动交给宿主进行模块和策略发现的入口程序集。
    /// 私有依赖只按需加载，不再被误当成插件入口进行全量类型扫描。
    /// </summary>
    internal IReadOnlyList<string> EntryAssemblyPaths { get; }

    /// <summary>
    /// 尝试建立插件目录布局。
    /// </summary>
    /// <param name="pluginDirectory">一个插件独占的部署目录。</param>
    /// <param name="layout">成功时返回不可变布局。</param>
    /// <param name="errorCode">失败时返回稳定诊断码。</param>
    /// <param name="errorDetail">失败时返回不包含异常堆栈的简短原因。</param>
    /// <param name="manifest">已经通过严格解析与版本兼容检查的清单。</param>
    /// <returns>目录是否满足清单入口和私有依赖唯一性约定。</returns>
    internal static bool TryCreate(
        string pluginDirectory,
        PluginManifest manifest,
        out PluginDirectoryLayout? layout,
        out string? errorCode,
        out string? errorDetail)
    {
        layout = null;
        errorCode = null;
        errorDetail = null;

        try
        {
            ArgumentNullException.ThrowIfNull(manifest);
            var directoryPath = Path.GetFullPath(pluginDirectory);
            if (!Directory.Exists(directoryPath))
            {
                errorCode = "PLUGIN_ENTRY_INVALID";
                errorDetail = "插件目录不存在。";
                return false;
            }

            var managedPaths = EnumerateManagedAssemblyPaths(directoryPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (!TryBuildAssemblyIndex(
                    managedPaths,
                    out var assemblyPaths,
                    out errorDetail))
            {
                errorCode = "PLUGIN_PRIVATE_DEPENDENCY_AMBIGUOUS";
                return false;
            }

            var entryAssemblyPath = Path.GetFullPath(
                Path.Combine(directoryPath, manifest.EntryAssembly));
            if (!File.Exists(entryAssemblyPath))
            {
                errorCode = "PLUGIN_ENTRY_INVALID";
                errorDetail = $"清单入口 {manifest.EntryAssembly} 不存在。";
                return false;
            }

            // 设计意图：清单是入口的唯一事实源。deps 只决定是否启用标准解析器，
            // 不再反向决定哪些 DLL 会被宿主主动加载和执行类型扫描。
            var dependencyPath = Path.ChangeExtension(entryAssemblyPath, ".deps.json");
            var mainAssemblyPath = File.Exists(dependencyPath)
                ? entryAssemblyPath
                : null;
            try
            {
                _ = AssemblyName.GetAssemblyName(entryAssemblyPath);
            }
            catch (BadImageFormatException)
            {
                errorCode = "PLUGIN_ENTRY_INVALID";
                errorDetail = $"清单入口 {manifest.EntryAssembly} 不是有效托管程序集。";
                return false;
            }

            layout = new PluginDirectoryLayout(
                directoryPath,
                mainAssemblyPath,
                [entryAssemblyPath],
                assemblyPaths);
            return true;
        }
        catch (Exception exception)
        {
            errorCode = "PLUGIN_ENTRY_INVALID";
            errorDetail = exception.GetType().Name;
            return false;
        }
    }

    /// <summary>
    /// 按程序集简单名称查找当前插件目录中的唯一托管依赖。
    /// </summary>
    /// <remarks>
    /// 设计意图：该索引只用于当前插件内部的 Legacy 回退，绝不访问其他插件目录。
    /// 同一插件目录出现两个同名程序集时会在布局建立阶段拒绝，避免 ALC 的单名称单版本规则产生隐式选择。
    /// </remarks>
    internal string? ResolveAssemblyPath(AssemblyName assemblyName) =>
        assemblyName.Name is { } name && _assemblyPaths.TryGetValue(name, out var path)
            ? path
            : null;

    private static IEnumerable<string> EnumerateManagedAssemblyPaths(string directoryPath)
    {
        foreach (var dllPath in Directory.GetFiles(
                     directoryPath,
                     "*.dll",
                     SearchOption.TopDirectoryOnly))
        {
            if (IsManagedAssembly(dllPath))
            {
                yield return Path.GetFullPath(dllPath);
            }
        }

        foreach (var subdirectory in Directory.GetDirectories(directoryPath))
        {
            if (SkippedDirectories.Contains(Path.GetFileName(subdirectory)))
            {
                continue;
            }

            foreach (var dllPath in EnumerateManagedAssemblyPaths(subdirectory))
            {
                yield return dllPath;
            }
        }
    }

    private static bool TryBuildAssemblyIndex(
        IEnumerable<string> managedPaths,
        out IReadOnlyDictionary<string, string> assemblyPaths,
        out string? errorDetail)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in managedPaths)
        {
            var assemblyName = AssemblyName.GetAssemblyName(path).Name;
            if (assemblyName is null)
            {
                continue;
            }

            if (index.TryGetValue(assemblyName, out var existingPath) &&
                !string.Equals(existingPath, path, StringComparison.OrdinalIgnoreCase))
            {
                assemblyPaths = index;
                errorDetail = $"同一插件目录包含多个名为 {assemblyName} 的托管程序集。";
                return false;
            }

            index.Add(assemblyName, path);
        }

        assemblyPaths = index;
        errorDetail = null;
        return true;
    }

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            _ = AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException)
        {
            // 原生 DLL 可能位于插件根目录；它不属于托管入口索引，按原生解析规则处理。
            return false;
        }
        catch (FileLoadException)
        {
            return false;
        }
    }
}
