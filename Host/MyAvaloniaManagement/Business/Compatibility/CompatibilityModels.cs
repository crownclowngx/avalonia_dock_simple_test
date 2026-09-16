using System;
using System.Collections.Generic;

namespace MyAvaloniaManagement.Business.Compatibility;

/// <summary>验收层级互相独立。高层通过不补造低层记录，更不表示未执行的业务已验证。</summary>
internal enum CompatibilityLevel { Static, Composition, Workspace, Business }
internal enum CompatibilityOutcome { Passed, Failed, NotRun, MissingInput, Interrupted }
internal enum EvidenceMatch { Matches, DifferentArtifact, DifferentHost, DifferentRules, DifferentEnvironment, Unknown }

/// <summary>纯文件事实；路径相对于产物根，报告不持有个人安装路径。</summary>
internal sealed record ArtifactFile(string Path, long Length, string Sha256);
internal sealed record ArtifactIdentity(string Sha256, IReadOnlyList<ArtifactFile> Files);
internal sealed record BuildDependency(string Id, string Version);

/// <summary>可选构建旁路信息。包依赖版本与程序集引用版本明确分离，不从一方推断另一方。</summary>
internal sealed record PluginBuildInfo(int SchemaVersion, string TargetFramework, string BuildVersion,
    IReadOnlyList<BuildDependency> Packages);
internal sealed record PluginArtifact(string PluginId, string Version, string SdkRange, string EntryAssembly,
    ArtifactIdentity Identity, PluginBuildInfo? Build, IReadOnlyList<BuildDependency> AssemblyReferences);

/// <summary>实际运行文件的身份；产品版本仅用于显示，匹配必须使用文件摘要和环境。</summary>
internal sealed record HostCompatibilityIdentity(string ProductVersion, string InformationalVersion,
    string RuntimeHash, string RuleHash, string OperatingSystem, string Architecture,
    string Framework, string SdkVersion, string AvaloniaVersion, string DockVersion,
    IReadOnlyList<ArtifactFile> Files);

/// <summary>场景采用稳定名称和受控摘要，不把异常正文、命令或凭据写入可导入报告。</summary>
internal sealed record CompatibilityCheck(CompatibilityLevel Level, CompatibilityOutcome Outcome,
    string Scenario, string Detail);

/// <summary>报告仅表达一次执行的数据，不承诺来源可信，不拥有安装、加载或执行权限。</summary>
internal sealed record PluginCompatibilityReport(int SchemaVersion, Guid ReportId, DateTimeOffset ExecutedAtUtc,
    string PluginId, string PluginVersion, string PluginHash, HostCompatibilityIdentity Host,
    IReadOnlyList<CompatibilityCheck> Checks);

internal sealed record StoredCompatibilityReport(PluginCompatibilityReport Report, string Source);
internal sealed record CompatibilityEvidenceRow(string PluginId, string PluginVersion, string HostBuild,
    string MatchText, string LevelText, string ResultText, string TimeText, string Source, string Detail);
