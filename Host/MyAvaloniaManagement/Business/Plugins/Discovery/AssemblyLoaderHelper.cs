using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Plugins.Enablement;

namespace MyAvaloniaManagement.Business.Plugins.Discovery;

/// <summary>
/// 描述一次插件根目录发现的不可变结果。
/// </summary>
/// <remarks>
/// 设计意图：程序集、预检类型和失败诊断必须来自同一次目录快照，避免后续模块发现
/// 再次扫描文件系统后得到与程序集加载阶段不一致的事实。
/// </remarks>
internal sealed class PluginDiscoverySnapshot
{
    private readonly IReadOnlyDictionary<Assembly, PluginManifest> _manifestsByAssembly;
    private readonly IReadOnlyDictionary<Assembly, Type> _moduleTypesByAssembly;

    internal PluginDiscoverySnapshot(
        IEnumerable<Assembly> assemblies,
        IReadOnlyDictionary<Assembly, PluginManifest> manifestsByAssembly,
        IReadOnlyDictionary<Assembly, Type> moduleTypesByAssembly,
        IEnumerable<HostDiagnosticDraft> diagnostics,
        IEnumerable<PluginDiscoveryCandidate>? candidates = null,
        PluginEnablementReadResult? startupSettings = null)
    {
        Assemblies = new ReadOnlyCollection<Assembly>(assemblies.ToArray());
        _manifestsByAssembly = new ReadOnlyDictionary<Assembly, PluginManifest>(
            new Dictionary<Assembly, PluginManifest>(manifestsByAssembly));
        _moduleTypesByAssembly = new ReadOnlyDictionary<Assembly, Type>(
            new Dictionary<Assembly, Type>(moduleTypesByAssembly));
        Diagnostics = new ReadOnlyCollection<HostDiagnosticDraft>(diagnostics.ToArray());
        Candidates = Array.AsReadOnly((candidates ?? []).ToArray());
        StartupSettings = startupSettings ?? PluginEnablementReadResult.Default;
    }

    /// <summary>供无插件 Host 与测试组合根使用的显式空快照。</summary>
    internal static PluginDiscoverySnapshot Empty { get; } = new(
        [],
        new Dictionary<Assembly, PluginManifest>(),
        new Dictionary<Assembly, Type>(),
        []);

    internal IReadOnlyList<Assembly> Assemblies { get; }

    internal IReadOnlyList<HostDiagnosticDraft> Diagnostics { get; }

    /// <summary>完整且不持有程序集的已验证候选；禁用或加载失败也必须留在看板中。</summary>
    internal IReadOnlyList<PluginDiscoveryCandidate> Candidates { get; }
    /// <summary>本次首次发现使用的冻结策略；运行中保存或重读配置不能改写它。</summary>
    internal PluginEnablementReadResult StartupSettings { get; }

    /// <summary>
    /// 取得与已加载程序集来自同一次发现快照的已验证清单。
    /// </summary>
    internal PluginManifest GetManifest(Assembly assembly) =>
        _manifestsByAssembly[assembly];

    /// <summary>
    /// 取得在目录隔离阶段按 manifest 精确名称验证且具备 public 无参构造的模块类型。
    /// </summary>
    /// <remarks>
    /// 设计意图：Catalog 只实例化这份快照中的结论，不枚举程序集并产生另一套入口事实。
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

/// <summary>清单和目录身份来自同次发现；能否加载只由启动策略决定，不冒充运行成功事实。</summary>
internal sealed record PluginDiscoveryCandidate(string DirectoryPath, PluginManifest Manifest);

/// <summary>
/// 从部署目录建立使用 manifest schema 2 的 Managed Plugin 不可变发现快照。
/// </summary>
/// <remarks>
/// 同一规范化插件根与数据根组合只生成一次不可变快照。所有目录先验证清单身份，
/// 启用项再通过 deps 和精确入口类型预检并使用独立加载上下文；禁用项只保留候选身份。
/// </remarks>
internal static class AssemblyLoaderHelper
{
    private static readonly IPluginSharedAssemblyPolicy SharedAssemblyPolicy =
        new HostContractAssemblyPolicy();

    private static readonly ConcurrentDictionary<(string Root, string DataRoot), Lazy<PluginDiscoverySnapshot>> RootSnapshots = new();

    private sealed record ManifestCandidate(string DirectoryPath, PluginManifest Manifest);

    /// <summary>
    /// 取得生产组合根使用的完整发现快照。
    /// </summary>
    internal static PluginDiscoverySnapshot Discover(string rootPluginsDirName,
        PluginEnablementReadResult? startupSettings = null, string? dataRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPluginsDirName);
        var rootPath = PluginRootDirectoryPolicy.Resolve(
            AppContext.BaseDirectory, rootPluginsDirName, OperatingSystem.IsMacOS());
        // 仅规范路径参与缓存键；不能把设置摘要作为键，否则切换开关会产生第二次加载。
        // 未传数据根的内部测试沿用默认全启用，不偷偷读取开发者的生产设置。
        static string KeyPath(string path) => OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        var key = (KeyPath(rootPath), dataRoot is null ? string.Empty : KeyPath(Path.GetFullPath(dataRoot)));
        return RootSnapshots.GetOrAdd(
            key,
            _ => new Lazy<PluginDiscoverySnapshot>(
                () => LoadRootSnapshot(rootPath, startupSettings ?? PluginEnablementReadResult.Default),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static PluginDiscoverySnapshot LoadRootSnapshot(string rootPath, PluginEnablementReadResult startupSettings)
    {
        var loaded = new List<Assembly>();
        var manifestsByAssembly = new Dictionary<Assembly, PluginManifest>();
        var moduleTypesByAssembly = new Dictionary<Assembly, Type>();
        var diagnostics = new List<HostDiagnosticDraft>();
        var inventory = new List<PluginDiscoveryCandidate>();
        if (startupSettings.ErrorCode is { } settingsError)
            diagnostics.Add(new(settingsError, HostDiagnosticPhase.PluginRootDiscovery));

        try
        {
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
                return new PluginDiscoverySnapshot(
                    loaded,
                    manifestsByAssembly,
                    moduleTypesByAssembly,
                    diagnostics, inventory, startupSettings);
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
                        out _))
                {
                    diagnostics.Add(CreateManifestDiagnostic(
                        pluginDirectory,
                        manifest: null,
                        errorCode ?? HostDiagnosticCodes.PluginManifestInvalid));
                    continue;
                }

                candidates.Add(new ManifestCandidate(pluginDirectory, manifest!));
                inventory.Add(new(pluginDirectory, manifest!));
            }

            var duplicateIdentities = candidates
                .GroupBy(candidate => candidate.Manifest.PluginId)
                .Where(group => group.Count() > 1)
                .ToArray();
            if (duplicateIdentities.Length > 0)
            {
                foreach (var group in duplicateIdentities)
                {
                    foreach (var candidate in group)
                    {
                        diagnostics.Add(CreateManifestDiagnostic(
                            candidate.DirectoryPath,
                            candidate.Manifest,
                            HostDiagnosticCodes.PluginManifestIdentityDuplicate));
                    }
                }

                // 设计意图：重复身份会让所有权、状态和依赖关系失去确定含义。
                // 即使其他插件本身有效，也不能在发现全局歧义后继续执行任何插件代码。
                return new PluginDiscoverySnapshot(
                    loaded,
                    manifestsByAssembly,
                    moduleTypesByAssembly,
                    diagnostics, inventory, startupSettings);
            }

            foreach (var candidate in candidates)
            {
                // 保留清单供看板重新启用，但在任何 ALC、DLL、构造及兼容执行之前过滤。
                // 无可靠配置表示暂停加载；不能将读取失败误解释为空禁用集合。
                if (startupSettings.Settings?.IsEnabled(candidate.Manifest.PluginId) != true) continue;
                if (!PluginCompatibilityEvaluator.TryEvaluate(
                        candidate.Manifest,
                        PluginSdkCompatibilityProfile.Current,
                        out var errorCode,
                        out _))
                {
                    diagnostics.Add(CreateManifestDiagnostic(
                        candidate.DirectoryPath,
                        candidate.Manifest,
                        errorCode!));
                    continue;
                }

                LoadPluginDirectory(
                    candidate.DirectoryPath,
                    candidate.Manifest,
                    loaded,
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
                HostDiagnosticPhase.PluginRootDiscovery)
            {
                Exception = exception,
            });
        }

        return new PluginDiscoverySnapshot(
            loaded,
            manifestsByAssembly,
            moduleTypesByAssembly,
            diagnostics, inventory, startupSettings);
    }

    private static void LoadPluginDirectory(
        string pluginDirectory,
        PluginManifest manifest,
        ICollection<Assembly> loaded,
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
                out _))
        {
            diagnostics.Add(new HostDiagnosticDraft(
                errorCode ?? HostDiagnosticCodes.PluginEntryInvalid,
                HostDiagnosticPhase.PluginRootDiscovery)
            {
                PluginDirectory = pluginName,
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
                HostDiagnosticCodes.PluginManifestDescriptionMismatch) with
            {
                Phase = HostDiagnosticPhase.PluginAssemblyLoad,
                AssemblyName = candidateAssembly.GetName(),
            });
            return;
        }

        Type? entryType;
        try
        {
            // 预先解析完整引用表，才能在调用插件 Configure 前发现发布包遗漏的私有依赖。
            // 随后只按清单中的完整名称取一个类型；不得重新使用 GetTypes 扫描模块候选。
            foreach (var reference in candidateAssembly.GetReferencedAssemblies())
            {
                _ = loadContext.LoadFromAssemblyName(reference);
            }

            entryType = candidateAssembly.GetType(
                manifest.EntryPoint.Type,
                throwOnError: false,
                ignoreCase: false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Add(new HostDiagnosticDraft(
                PluginLoadExceptionMapper.GetCode(exception) == HostDiagnosticCodes.PluginSharedAssemblyMismatch
                    ? HostDiagnosticCodes.PluginSharedAssemblyMismatch
                    : exception is FileNotFoundException or FileLoadException or BadImageFormatException
                        ? HostDiagnosticCodes.PluginAssemblyLoadFailed
                        : HostDiagnosticCodes.PluginTypePreflightFailed,
                HostDiagnosticPhase.PluginTypePreflight)
            {
                PluginDirectory = pluginName,
                AssemblyName = candidateAssembly.GetName(),
                Exception = exception,
            });
            return;
        }

        if (!PluginModulePreflight.TryValidate(
                entryType,
                out var moduleType,
                out var moduleErrorCode,
                out _))
        {
            diagnostics.Add(new HostDiagnosticDraft(
                moduleErrorCode!,
                HostDiagnosticPhase.PluginTypePreflight)
            {
                PluginId = manifest.PluginId,
                PluginDirectory = pluginName,
                AssemblyName = candidateAssembly.GetName(),
                StableId = manifest.PluginId.Value,
            });
            return;
        }

        loaded.Add(candidateAssembly);
        manifestsByAssembly.Add(candidateAssembly, manifest);
        moduleTypesByAssembly.Add(candidateAssembly, moduleType!);
    }

    private static HostDiagnosticDraft CreateManifestDiagnostic(
        string pluginDirectory,
        PluginManifest? manifest,
        string code)
    {
        return new HostDiagnosticDraft(
            code,
            HostDiagnosticPhase.PluginManifestPreflight)
        {
            PluginId = manifest?.PluginId,
            PluginDirectory = Path.GetFileName(pluginDirectory),
            AssemblyName = manifest is null
                ? null
                : new AssemblyName(manifest.EntryPoint.Assembly),
            PluginVersion = manifest?.PluginVersion,
            SdkRange = manifest?.Sdk,
        };
    }

    private static HostDiagnosticDraft CreateLoadFailure(
        string pluginName,
        string? assemblyName,
        Exception exception,
        HostDiagnosticPhase phase) =>
        new(
            PluginLoadExceptionMapper.GetCode(exception),
            phase)
        {
            PluginDirectory = pluginName,
            AssemblyName = assemblyName is null ? null : new AssemblyName(assemblyName),
            Exception = exception,
        };

}
