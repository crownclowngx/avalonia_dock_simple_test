using System;
using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Business.Compatibility;

internal sealed record CompatibilityMatrixColumn(string Key, string Title, string Detail);
internal sealed record CompatibilityMatrixCell(string Text, string Detail);
internal sealed record CompatibilityMatrixRow(string PluginId, string ArtifactText, IReadOnlyList<CompatibilityMatrixCell> Cells);
internal sealed record CompatibilityMatrix(IReadOnlyList<CompatibilityMatrixColumn> Columns, IReadOnlyList<CompatibilityMatrixRow> Rows);

/// <summary>纯展示投影：行绑定插件产物，列绑定 Host 文件、规则和环境；相同版本不合并不同二进制。</summary>
internal static class CompatibilityDashboardProjection
{
    internal static string HostKey(HostCompatibilityIdentity host) =>
        string.Join("|", host.RuntimeHash, host.RuleHash, host.OperatingSystem, host.Architecture, host.Framework);

    internal static CompatibilityMatrix CreateMatrix(PluginDashboardSnapshot snapshot, IEnumerable<string> visiblePluginIds)
    {
        var ids = visiblePluginIds.ToHashSet(StringComparer.Ordinal);
        var reports = snapshot.Reports.Where(item => ids.Contains(item.Report.PluginId)).ToArray();
        var hosts = reports.Select(item => item.Report.Host).Concat(snapshot.Host is { } host ? [host] : [])
            .DistinctBy(HostKey).OrderBy(HostKey, StringComparer.Ordinal).ToArray();
        var columns = hosts.Select(host => new CompatibilityMatrixColumn(HostKey(host),
            $"Host {host.ProductVersion}\n{host.RuntimeHash[..12]}\n{host.Architecture} / {host.Framework}",
            $"{host.InformationalVersion}\n{host.OperatingSystem} / {host.Architecture}\n{host.Framework}\n规则 {host.RuleHash[..12]}" )).ToArray();
        var identities = reports.Select(item => (item.Report.PluginId, Version: item.Report.PluginVersion, Hash: item.Report.PluginHash))
            .Concat(snapshot.Artifacts.Where(item => ids.Contains(item.PluginId) && item.Artifact is not null)
                .Select(item => (item.PluginId, Version: item.Artifact!.Version, Hash: item.Artifact.Identity.Sha256)))
            .Distinct().OrderBy(item => item.PluginId, StringComparer.Ordinal).ThenBy(item => item.Hash, StringComparer.Ordinal).ToList();
        identities.AddRange(ids.Where(id => identities.All(item => item.PluginId != id)).Select(id => (id, "未知", "")));
        var rows = identities.Select(identity => new CompatibilityMatrixRow(identity.PluginId,
            $"{identity.Version} · {(identity.Hash.Length == 0 ? "未检查产物" : identity.Hash[..12])}",
            hosts.Select(host => Cell(snapshot, reports.Where(item => item.Report.PluginId == identity.PluginId &&
                item.Report.PluginHash == identity.Hash && HostKey(item.Report.Host) == HostKey(host)).ToArray())).ToArray())).ToArray();
        return new CompatibilityMatrix(columns, rows);
    }

    private static CompatibilityMatrixCell Cell(PluginDashboardSnapshot snapshot, StoredCompatibilityReport[] records)
    {
        if (records.Length == 0) return new("无报告", "未取得该产物在此 Host 构建的验收证据。");
        var ordered = records.OrderByDescending(item => item.Report.ExecutedAtUtc).ThenBy(item => item.Report.ReportId).ToArray();
        var latest = ordered[0];
        var artifact = snapshot.Artifacts.FirstOrDefault(item => item.PluginId == latest.Report.PluginId)?.Artifact;
        var match = CompatibilityMatcher.Match(latest.Report, artifact, snapshot.Host);
        var conflict = ordered.Select(item => CompatibilityMatcher.Summary(item.Report)).Distinct().Count() > 1 ||
            ordered.GroupBy(item => item.Report.ReportId).Any(group => group.Count() > 1);
        var text = CompatibilityMatcher.MatchText(match) + "\n" + CompatibilityMatcher.Summary(latest.Report).Replace("；", "\n") +
            (conflict ? "\n⚠ 历史结果不同，请查看详情" : "");
        return new(text, $"最新记录：{latest.Report.ExecutedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n共 {records.Length} 份报告；来源未认证。" );
    }

    /// <summary>保留每一次、每一项检查，不用“最新通过”抹掉失败或中断。缺失层级补成未执行。</summary>
    internal static IReadOnlyList<CompatibilityEvidenceRow> Evidence(PluginDashboardSnapshot snapshot, string id) =>
        snapshot.Reports.Where(item => item.Report.PluginId == id).OrderByDescending(item => item.Report.ExecutedAtUtc)
            .SelectMany(item => Enum.GetValues<CompatibilityLevel>().SelectMany(level =>
            {
                var checks = item.Report.Checks.Where(check => check.Level == level).ToArray();
                if (checks.Length == 0) checks = [new(level, CompatibilityOutcome.NotRun, "未提供", "报告没有此层级的执行记录。")];
                return checks.Select(check => new CompatibilityEvidenceRow(id, item.Report.PluginVersion,
                    $"{item.Report.Host.ProductVersion} / {item.Report.Host.RuntimeHash[..12]}",
                    CompatibilityMatcher.MatchText(CompatibilityMatcher.Match(item.Report,
                        snapshot.Artifacts.FirstOrDefault(artifact => artifact.PluginId == id)?.Artifact, snapshot.Host)),
                    CompatibilityMatcher.LevelText(check.Level), CompatibilityMatcher.OutcomeText(check.Outcome),
                    item.Report.ExecutedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), item.Source,
                    $"报告 {item.Report.ReportId} · 产物 {item.Report.PluginHash[..12]}\n{check.Scenario}：{check.Detail}"));
            })).ToArray();
}
