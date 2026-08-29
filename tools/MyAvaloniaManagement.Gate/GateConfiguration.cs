using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAvaloniaManagement.Gate;

internal sealed record GateConfiguration
{
    public required int SchemaVersion { get; init; }
    public required string MainSolution { get; init; }
    public required RepositoryConfiguration[] Repositories { get; init; }
    public required TestSuiteConfiguration[] TestSuites { get; init; }
    public required PluginConfiguration[] Plugins { get; init; }
    public required ArchitectureRuleConfiguration[] ArchitectureRules { get; init; }
    public required CoverageThreshold HostCoverage { get; init; }
    public required HarnessConfiguration Harness { get; init; }
    public required string WindowsSmokeProject { get; init; }

    public static GateConfiguration Load(string path)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var configuration = JsonSerializer.Deserialize<GateConfiguration>(File.ReadAllText(path), options) ??
            throw new GateFailureException("Gate 配置为空。");
        configuration.Validate();
        return configuration;
    }

    private void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new GateFailureException($"不支持 gate.config.json schemaVersion={SchemaVersion}。");
        }

        RequireUnique(Repositories.Select(item => item.Id), "repository");
        RequireUnique(TestSuites.Select(item => item.Id), "test suite");
        RequireUnique(Plugins.Select(item => item.Id), "plugin");
        RequireUnique(ArchitectureRules.Select(item => item.Id), "architecture rule");
        if (string.IsNullOrWhiteSpace(MainSolution) || string.IsNullOrWhiteSpace(WindowsSmokeProject))
        {
            throw new GateFailureException("Gate 配置缺少主解决方案或 Smoke 项目。");
        }
    }

    private static void RequireUnique(IEnumerable<string> values, string description)
    {
        var entries = values.ToArray();
        if (entries.Any(string.IsNullOrWhiteSpace) || entries.Distinct(StringComparer.Ordinal).Count() != entries.Length)
        {
            throw new GateFailureException($"{description} 标识必须非空且唯一。");
        }
    }
}

internal sealed record RepositoryConfiguration
{
    public required string Id { get; init; }
    public required string Scope { get; init; }
    public required string DefaultPath { get; init; }
    public required string Solution { get; init; }
    public required string TestProject { get; init; }
    public required string StandaloneProject { get; init; }
    public required string[] SelfTestArguments { get; init; }
    public required string SelfTestSuccessText { get; init; }
    public required double MinimumLineCoverage { get; init; }
    public required double MinimumBranchCoverage { get; init; }
}

internal sealed record TestSuiteConfiguration
{
    public required string Id { get; init; }
    public required string Scope { get; init; }
    public required string Project { get; init; }
    public required string CoverageGroup { get; init; }
}

internal sealed record PluginConfiguration
{
    public required string Id { get; init; }
    public required string Scope { get; init; }
    public required string Repository { get; init; }
    public required string Project { get; init; }
    public required string DirectoryName { get; init; }
    public required string AssemblyName { get; init; }
}

internal sealed record ArchitectureRuleConfiguration
{
    public required string Id { get; init; }
    public required string Scope { get; init; }
    public string Repository { get; init; } = "main";
    public required string[] Paths { get; init; }
    public required string Pattern { get; init; }
}

internal sealed record CoverageThreshold
{
    public required double MinimumLine { get; init; }
    public required double MinimumBranch { get; init; }
}

internal sealed record HarnessConfiguration
{
    public required string Project { get; init; }
    public required string ReportRelativePath { get; init; }
}
