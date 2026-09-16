using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Plugins.Registration;

namespace MyAvaloniaManagement.Business.Compatibility;

/// <summary>窗口仅能读取证据或导入数据；此端口没有发现、加载或执行插件的能力。</summary>
internal interface IPluginDashboardEvidence
{
    Task<PluginDashboardSnapshot> ReadAsync(bool inspectArtifacts, CancellationToken token);
    Task ImportAsync(string path, CancellationToken token);
}

/// <summary>检查结果是带时间的磁盘快照，不承诺检查之后文件仍然没有变化。</summary>
internal sealed record PluginArtifactInspection(string PluginId, PluginArtifact? Artifact, string State);
internal sealed record PluginDashboardSnapshot(HostCompatibilityIdentity? Host,
    IReadOnlyList<PluginArtifactInspection> Artifacts, IReadOnlyList<StoredCompatibilityReport> Reports,
    int InvalidReports, DateTimeOffset? CheckedAtUtc, string Notice);

/// <summary>把文件 I/O 留在后台，并把当前会话的程序集事实与磁盘产物分开。</summary>
/// <remarks>
/// 设计思路：Registry 只提供已提交的身份和程序集，不解析 Provider、不调用工厂。
/// 首次检查记录文件集，后续检查发现变化即要求重启；入口及已加载私有程序集还逐一核对 MVID。
/// 尚未执行首次检查时不声称拥有启动时的全目录摘要；报告读取和导入不会自动扫描大型插件。
/// </remarks>
internal sealed class PluginDashboardEvidence(PluginRegistry registry, CompatibilityReportStore store,
    TimeProvider time, string? deliveredDirectory = null) : IPluginDashboardEvidence
{
    private readonly ConcurrentDictionary<string, string> _firstHashes = new(StringComparer.Ordinal);

    public Task ImportAsync(string path, CancellationToken token) => store.ImportAsync(path, token);

    public Task<PluginDashboardSnapshot> ReadAsync(bool inspectArtifacts, CancellationToken token) => Task.Run(async () =>
    {
        var (local, invalid) = await store.ReadAsync(token);
        var reports = local.ToList();
        if (deliveredDirectory is not null)
        {
            var delivered = await new CompatibilityReportStore(deliveredDirectory).ReadAsync(token);
            invalid += delivered.Invalid;
            reports.AddRange(delivered.Reports.Select(item => item with { Source = "随 Host 交付（未认证来源）" }));
        }
        // 同 ID 同内容只展示一次；冲突内容完整保留，由投影显示冲突，不能任意挑一次成功。
        reports = reports.DistinctBy(item => CompatibilityReportJson.Serialize(item.Report)).ToList();
        if (!inspectArtifacts) return new PluginDashboardSnapshot(null, [], reports, invalid, null,
            "尚未检查安装产物；报告仅作历史参考。点击“检查安装产物”后判断适用性。");

        HostCompatibilityIdentity? host = null;
        var notice = "结果对应本次文件检查；检查不会执行插件业务，也不会自动重载插件。";
        try { host = await HostCompatibilityCapture.CaptureAsync(token); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { notice = "无法确认当前 Host 文件身份，请检查安装内容或重启后重试；报告适用性保持未知。"; }
        var artifacts = new List<PluginArtifactInspection>();
        foreach (var plugin in registry.Plugins)
        {
            token.ThrowIfCancellationRequested();
            var id = plugin.Manifest.PluginId.Value;
            try
            {
                var entry = plugin.EntryAssembly;
                var entryPath = AssemblyFilePath.ReadOptional(entry) ?? throw new InvalidDataException("入口无文件位置。");
                var directory = Path.GetDirectoryName(entryPath)!;
                var loaded = AssemblyLoadContext.GetLoadContext(entry)?.Assemblies ?? [entry];
                var files = loaded.Select(assembly => (Assembly: assembly, Path: AssemblyFilePath.ReadOptional(assembly)))
                    .Where(item => item.Path is not null &&
                        Path.GetFullPath(item.Path).StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .Select(item => (item.Path!, item.Assembly.ManifestModule.ModuleVersionId)).ToArray();
                artifacts.Add(await InspectAsync(id, directory, files, token));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or BadImageFormatException or System.Text.Json.JsonException)
            { artifacts.Add(new(id, null, "产物不可读取或检查期间发生变化；保留当前会话诊断，请重试。")); }
        }
        return new PluginDashboardSnapshot(host, artifacts, reports, invalid, time.GetUtcNow(), notice);
    }, token);

    /// <summary>文件检查只依赖已捕获的值；不持有加载上下文，也不会为获取元数据而加载程序集。</summary>
    internal async Task<PluginArtifactInspection> InspectAsync(string id, string directory,
        IReadOnlyList<(string Path, Guid ModuleId)> loadedFiles, CancellationToken token)
    {
        var artifact = await PluginArtifactReader.ReadAsync(directory, token);
        var changed = artifact.PluginId != id || loadedFiles.Any(file => PluginArtifactReader.ReadModuleId(file.Path) != file.ModuleId);
        var first = _firstHashes.GetOrAdd(id, artifact.Identity.Sha256);
        changed |= first != artifact.Identity.Sha256;
        return changed ? new(id, null, "安装内容已变化，当前会话仍为原加载结果；请重启后检查。") :
            new(id, artifact, "已检查磁盘文件，并核对当前会话已加载的托管程序集身份。");
    }
}
