using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAvaloniaManagement.Gate;

internal sealed record GateStageResult
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required long DurationMilliseconds { get; init; }
    public string? EvidencePath { get; init; }
    public string? Error { get; init; }
}

internal sealed record GatePassResult
{
    public required int Pass { get; init; }
    public required bool Passed { get; init; }
    public required string EvidenceRoot { get; init; }
    public required GateStageResult[] Stages { get; init; }
    public required Dictionary<string, PackageEvidence> Packages { get; init; }
    public CoverageEvidence? HostCoverage { get; init; }
    public string? Error { get; init; }
}

internal sealed record PackageEvidence(
    string PluginId,
    string ArchivePath,
    string Sha256,
    int Files,
    string ManifestSha256,
    bool Deterministic);

internal sealed record CoverageEvidence(double Line, double Branch);

/// <summary>套件证据单独保存，不改变既有 GateSummary schema 2。未运行时计数与退出码为空。</summary>
internal sealed record TestSuiteEvidence(string Id, string Status = "not-run")
{
    public string[]? Command { get; init; }
    public string? Filter { get; init; }
    public int? ExitCode { get; init; }
    public double? WallMilliseconds { get; init; }
    public string? TrxPath { get; init; }
    public string? AttachmentsPath { get; init; }
    public TestRunEvidence? Result { get; init; }
    public string? Error { get; init; }
}

internal sealed record TestSuiteSummary(string RunId, int Pass, SourceEvidence Source, List<TestSuiteEvidence> Suites);

internal sealed record GateSummary
{
    public int SchemaVersion { get; init; } = 2;
    public required string RunId { get; init; }
    public required string Profile { get; init; }
    public required string Scope { get; init; }
    public required bool Passed { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
    public required Dictionary<string, SourceEvidence> Sources { get; init; }
    public required HostEvidence Host { get; init; }
    public required RepeatabilityEvidence Repeatability { get; init; }
    public required GatePassResult[] Passes { get; init; }
    public string? Error { get; init; }
}

internal sealed record SourceEvidence(
    string Revision,
    string Tree,
    bool Clean,
    int Files,
    string Sha256);

internal sealed record HostEvidence(bool ReleaseEligible, bool Publishable);

internal sealed record RepeatabilityEvidence(bool Requested, bool Verified);

internal static class EvidenceWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options) + Environment.NewLine,
            new System.Text.UTF8Encoding(false));
    }
}
