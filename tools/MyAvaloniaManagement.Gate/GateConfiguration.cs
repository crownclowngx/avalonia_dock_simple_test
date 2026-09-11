using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAvaloniaManagement.Gate;

internal sealed record GateConfiguration
{
    public required int SchemaVersion { get; init; }
    public required string MainSolution { get; init; }
    public required TestSuiteConfiguration[] TestSuites { get; init; }
    public required PluginConfiguration[] Plugins { get; init; }
    public required ArchitectureRuleConfiguration[] ArchitectureRules { get; init; }
    public required CoverageThreshold HostCoverage { get; init; }
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
        if (SchemaVersion != 2)
        {
            throw new GateFailureException($"不支持 gate.config.json schemaVersion={SchemaVersion}。");
        }

        RequireUnique(TestSuites.Select(item => item.Id), "test suite");
        RequireUnique(Plugins.Select(item => item.Id), "plugin");
        RequireUnique(ArchitectureRules.Select(item => item.Id), "architecture rule");
        var expectedSuites = new[] { "host-plugin", "host-ui", "host-unit", "my-plug-test", "sdk" };
        if (!TestSuites.Select(item => item.Id).Order(StringComparer.Ordinal).SequenceEqual(expectedSuites) ||
            TestSuites.Any(item => item.CoverageGroup != (item.Id.StartsWith("host-", StringComparison.Ordinal)
                ? "host" : item.Id)))
        {
            throw new GateFailureException("Gate 必须包含完整 SDK、Host Unit/Plugin/UI 与 MyPlugTest 测试及对应覆盖率分组。");
        }
        if (Plugins.Length != 1 || Plugins[0].Id != "my-plug-test" ||
            Plugins[0].PluginId != "myavalonia.plugin.my-plug-test")
        {
            throw new GateFailureException("Gate 只接受唯一的 MyPlugTest 插件。");
        }
        foreach (var path in TestSuites.Select(item => item.Project)
                     .Concat(Plugins.Select(item => item.Project))
                     .Concat(ArchitectureRules.SelectMany(item => item.Paths))
                     .Append(MainSolution).Append(WindowsSmokeProject))
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
                path.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal))
            {
                throw new GateFailureException($"Gate 配置路径必须位于本仓：{path}。");
            }
        }
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

internal sealed record TestSuiteConfiguration
{
    public required string Id { get; init; }
    public required string Project { get; init; }
    public required string CoverageGroup { get; init; }
}

internal sealed record PluginConfiguration
{
    public required string Id { get; init; }
    public required string PluginId { get; init; }
    public required string Project { get; init; }
    public required string DirectoryName { get; init; }
    public required string AssemblyName { get; init; }
}

internal sealed record ArchitectureRuleConfiguration
{
    public required string Id { get; init; }
    public required string[] Paths { get; init; }
    public required string Pattern { get; init; }
}

internal sealed record CoverageThreshold
{
    public required double MinimumLine { get; init; }
    public required double MinimumBranch { get; init; }
}
