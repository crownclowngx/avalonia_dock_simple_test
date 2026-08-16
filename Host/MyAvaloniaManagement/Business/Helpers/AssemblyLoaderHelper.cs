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
    private readonly IReadOnlyDictionary<Assembly, PluginManifest> _manifestsByAssembly;
    private readonly IReadOnlyDictionary<Assembly, Type> _moduleTypesByAssembly;

    internal PluginDiscoverySnapshot(
        IEnumerable<Assembly> assemblies,
        IReadOnlyDictionary<Assembly, IReadOnlyList<Type>> typesByAssembly,
        IReadOnlyDictionary<Assembly, PluginManifest> manifestsByAssembly,
        IReadOnlyDictionary<Assembly, Type> moduleTypesByAssembly,
        IEnumerable<HostDiagnosticDraft> diagnostics)
    {
        Assemblies = new ReadOnlyCollection<Assembly>(assemblies.ToArray());
        _typesByAssembly = new ReadOnlyDictionary<Assembly, IReadOnlyList<Type>>(
            new Dictionary<Assembly, IReadOnlyList<Type>>(typesByAssembly));
        _manifestsByAssembly = new ReadOnlyDictionary<Assembly, PluginManifest>(
            new Dictionary<Assembly, PluginManifest>(manifestsByAssembly));
        _moduleTypesByAssembly = new ReadOnlyDictionary<Assembly, Type>(
            new Dictionary<Assembly, Type>(moduleTypesByAssembly));
        Diagnostics = new ReadOnlyCollection<HostDiagnosticDraft>(diagnostics.ToArray());
    }

    internal IReadOnlyList<Assembly> Assemblies { get; }

    internal IReadOnlyList<HostDiagnosticDraft> Diagnostics { get; }

    internal IReadOnlyList<Type> GetPreflightTypes(Assembly assembly) =>
        _typesByAssembly[assembly];

    /// <summary>
    /// 取得与已加载程序集来自同一次发现快照的已验证清单。
    /// </summary>
    internal PluginManifest GetManifest(Assembly assembly) =>
        _manifestsByAssembly[assembly];

    /// <summary>
    /// 取得在目录隔离阶段已经验证为唯一且具备 public 无参构造的模块类型。
    /// </summary>
    /// <remarks>
    /// 设计意图：Catalog 只实例化这份快照中的结论，不再次扫描类型并产生另一套模块事实。
    /// </remarks>
    internal Type GetModuleType(Assembly assembly) =>
        _moduleTypesByAssembly[assembly];

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
/// 从部署目录建立 Managed Plugin v1 不可变发现快照。
/// </summary>
/// <remarks>
/// 同一规范化根目录只生成一次不可变快照。每个插件目录必须通过清单、deps、入口类型和
/// 唯一模块结构预检，并使用独立加载上下文；失败目录不会进入后续服务注册或扩展发现。
/// </remarks>
internal static class AssemblyLoaderHelper
{
    private static readonly IPluginSharedAssemblyPolicy SharedAssemblyPolicy =
        new HostContractAssemblyPolicy();

    private static readonly ConcurrentDictionary<string, Lazy<PluginDiscoverySnapshot>> RootSnapshots =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record ManifestCandidate(string DirectoryPath, PluginManifest Manifest);

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
        var manifestsByAssembly = new Dictionary<Assembly, PluginManifest>();
        var moduleTypesByAssembly = new Dictionary<Assembly, Type>();
        var diagnostics = new List<HostDiagnosticDraft>();

        try
        {
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
                return new PluginDiscoverySnapshot(
                    loaded,
                    typesByAssembly,
                    manifestsByAssembly,
                    moduleTypesByAssembly,
                    diagnostics);
            }

            // 第一阶段严格限制为文件系统与 JSON 操作。只有所有清单身份都无歧义后，
            // 第二阶段才允许创建 ALC 和加载入口 DLL。
            var candidates = new List<ManifestCandidate>();
            foreach (var pluginDirectory in Directory.GetDirectories(rootPath)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!PluginManifestReader.TryRead(
                        pluginDirectory,
                        out var manifest,
                        out var errorCode,
                        out var errorDetail))
                {
                    diagnostics.Add(CreateManifestDiagnostic(
                        pluginDirectory,
                        manifest: null,
                        errorCode ?? HostDiagnosticCodes.PluginManifestInvalid,
                        errorDetail ?? "插件清单无效。"));
                    continue;
                }

                candidates.Add(new ManifestCandidate(pluginDirectory, manifest!));
            }

            var duplicateIdentities = candidates
                .GroupBy(candidate => candidate.Manifest.PluginId)
                .Where(group => group.Count() > 1)
                .ToArray();
            if (duplicateIdentities.Length > 0)
            {
                foreach (var group in duplicateIdentities)
                {
                    var directories = string.Join(
                        "、",
                        group.Select(candidate => Path.GetFileName(candidate.DirectoryPath))
                            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                    foreach (var candidate in group)
                    {
                        diagnostics.Add(CreateManifestDiagnostic(
                            candidate.DirectoryPath,
                            candidate.Manifest,
                            HostDiagnosticCodes.PluginManifestIdentityDuplicate,
                            $"多个插件目录声明了同一 pluginId：{directories}。"));
                    }
                }

                // 设计意图：重复身份会让所有权、状态和依赖关系失去确定含义。
                // 即使其他插件本身有效，也不能在发现全局歧义后继续执行任何插件代码。
                return new PluginDiscoverySnapshot(
                    loaded,
                    typesByAssembly,
                    manifestsByAssembly,
                    moduleTypesByAssembly,
                    diagnostics);
            }

            foreach (var candidate in candidates)
            {
                if (!PluginCompatibilityEvaluator.TryEvaluate(
                        candidate.Manifest,
                        HostCompatibilityProfile.Current,
                        out var errorCode,
                        out var errorDetail))
                {
                    diagnostics.Add(CreateManifestDiagnostic(
                        candidate.DirectoryPath,
                        candidate.Manifest,
                        errorCode!,
                        errorDetail!));
                    continue;
                }

                LoadPluginDirectory(
                    candidate.DirectoryPath,
                    candidate.Manifest,
                    loaded,
                    typesByAssembly,
                    manifestsByAssembly,
                    moduleTypesByAssembly,
                    diagnostics);
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

        return new PluginDiscoverySnapshot(
            loaded,
            typesByAssembly,
            manifestsByAssembly,
            moduleTypesByAssembly,
            diagnostics);
    }

    private static void LoadPluginDirectory(
        string pluginDirectory,
        PluginManifest manifest,
        ICollection<Assembly> loaded,
        IDictionary<Assembly, IReadOnlyList<Type>> typesByAssembly,
        IDictionary<Assembly, PluginManifest> manifestsByAssembly,
        IDictionary<Assembly, Type> moduleTypesByAssembly,
        ICollection<HostDiagnosticDraft> diagnostics)
    {
        var pluginName = Path.GetFileName(pluginDirectory);
        if (!PluginDirectoryLayout.TryCreate(
                pluginDirectory,
                manifest,
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

        Assembly candidateAssembly;
        try
        {
            candidateAssembly = loadContext.LoadFromAssemblyPath(layout!.EntryAssemblyPath);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Add(CreateLoadFailure(
                pluginName,
                pluginDirectory,
                Path.GetFileNameWithoutExtension(layout!.EntryAssemblyPath),
                exception,
                HostDiagnosticPhase.PluginAssemblyLoad));
            return;
        }

        var assemblyVersion = candidateAssembly.GetName().Version;
        if (!PluginCompatibilityEvaluator.HasMatchingPluginVersion(manifest, assemblyVersion))
        {
            diagnostics.Add(CreateManifestDiagnostic(
                pluginDirectory,
                manifest,
                HostDiagnosticCodes.PluginManifestDescriptionMismatch,
                "清单 pluginVersion 与入口程序集 AssemblyVersion 不一致。") with
            {
                Phase = HostDiagnosticPhase.PluginAssemblyLoad,
                AssemblyName = candidateAssembly.GetName().Name,
                TechnicalDetail =
                    $"manifestVersion={PluginVersionText.Format(manifest.PluginVersion)}; " +
                    $"assemblyVersion={PluginVersionText.Format(assemblyVersion ?? new Version(0, 0, 0, 0))}",
            });
            return;
        }

        IReadOnlyList<Type> candidateTypes;
        try
        {
            // GetTypes 不会解析只出现在方法体中的程序集引用。预先解析完整引用表，
            // 才能在调用插件 Configure 前发现发布包遗漏的私有依赖。
            foreach (var reference in candidateAssembly.GetReferencedAssemblies())
            {
                _ = loadContext.LoadFromAssemblyName(reference);
            }

            candidateTypes = candidateAssembly.GetTypes();
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
                AssemblyName = candidateAssembly.GetName().Name,
                Exception = exception,
                TechnicalDetail = FormatTypePreflightDetail(pluginDirectory, exception),
            });
            return;
        }

        if (!PluginModulePreflight.TryValidate(
                candidateTypes,
                out var moduleType,
                out var moduleErrorCode,
                out var moduleErrorDetail))
        {
            diagnostics.Add(new HostDiagnosticDraft(
                moduleErrorCode!,
                HostDiagnosticPhase.PluginTypePreflight,
                $"{moduleErrorDetail} 已隔离该插件目录。")
            {
                PluginId = manifest.PluginId.Value,
                PluginDirectory = pluginName,
                AssemblyName = candidateAssembly.GetName().Name,
                StableId = manifest.PluginId.Value,
                TechnicalDetail = $"directory={Path.GetFullPath(pluginDirectory)}",
            });
            return;
        }

        loaded.Add(candidateAssembly);
        typesByAssembly.Add(candidateAssembly, candidateTypes);
        manifestsByAssembly.Add(candidateAssembly, manifest);
        moduleTypesByAssembly.Add(candidateAssembly, moduleType!);
    }

    private static HostDiagnosticDraft CreateManifestDiagnostic(
        string pluginDirectory,
        PluginManifest? manifest,
        string code,
        string userMessage)
    {
        var host = HostCompatibilityProfile.Current;
        return new HostDiagnosticDraft(
            code,
            HostDiagnosticPhase.PluginManifestPreflight,
            userMessage)
        {
            PluginId = manifest?.PluginId.Value,
            PluginDirectory = Path.GetFileName(pluginDirectory),
            AssemblyName = manifest?.EntryAssembly,
            PluginVersion = manifest is null
                ? null
                : PluginVersionText.Format(manifest.PluginVersion),
            HostApiRange = manifest?.HostApi.ToString(),
            CommonContractRange = manifest?.CommonContract.ToString(),
            TechnicalDetail =
                $"directory={Path.GetFullPath(pluginDirectory)}; " +
                $"hostApi={PluginVersionText.Format(host.HostApiVersion)}; " +
                $"commonContract={PluginVersionText.Format(host.CommonContractVersion)}",
        };
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

}
