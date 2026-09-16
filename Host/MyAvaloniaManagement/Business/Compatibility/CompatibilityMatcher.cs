using System;
using System.Linq;

namespace MyAvaloniaManagement.Business.Compatibility;

/// <summary>纯证据匹配，不改变插件准入或运行状态；未知身份绝不能退化为版本字符串比较。</summary>
internal static class CompatibilityMatcher
{
    internal static EvidenceMatch Match(PluginCompatibilityReport report, PluginArtifact? plugin, HostCompatibilityIdentity? host)
    {
        if (plugin is null || host is null) return EvidenceMatch.Unknown;
        if (report.PluginId != plugin.PluginId || report.PluginVersion != plugin.Version || report.PluginHash != plugin.Identity.Sha256)
            return EvidenceMatch.DifferentArtifact;
        if (report.Host.RuleHash != host.RuleHash) return EvidenceMatch.DifferentRules;
        if (report.Host.RuntimeHash != host.RuntimeHash) return EvidenceMatch.DifferentHost;
        if (report.Host.OperatingSystem != host.OperatingSystem || report.Host.Architecture != host.Architecture || report.Host.Framework != host.Framework)
            return EvidenceMatch.DifferentEnvironment;
        return EvidenceMatch.Matches;
    }

    internal static string MatchText(EvidenceMatch match) => match switch
    {
        EvidenceMatch.Matches => "匹配当前产物与环境", EvidenceMatch.DifferentArtifact => "其他插件产物",
        EvidenceMatch.DifferentHost => "其他 Host 构建", EvidenceMatch.DifferentRules => "规则已变化",
        EvidenceMatch.DifferentEnvironment => "其他运行环境", _ => "待检查产物身份"
    };
    internal static string LevelText(CompatibilityLevel level) => level switch
    {
        CompatibilityLevel.Static => "静态", CompatibilityLevel.Composition => "加载与组合",
        CompatibilityLevel.Workspace => "Workspace", _ => "业务与真机"
    };
    internal static string OutcomeText(CompatibilityOutcome outcome) => outcome switch
    {
        CompatibilityOutcome.Passed => "通过", CompatibilityOutcome.Failed => "失败",
        CompatibilityOutcome.NotRun => "未执行", CompatibilityOutcome.MissingInput => "输入缺失", _ => "执行中断"
    };

    internal static string Summary(PluginCompatibilityReport report) => string.Join("；",
        report.Checks.GroupBy(c => c.Level).Select(group => $"{LevelText(group.Key)}：" +
            (group.All(c => c.Outcome == CompatibilityOutcome.Passed) ? "通过" :
                string.Join(" / ", group.Select(c => OutcomeText(c.Outcome)).Distinct()))));
}
