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
}

internal sealed record PackageEvidence(
    string PluginId,
    string ArchivePath,
    string Sha256,
    int Files,
    string ManifestSha256,
    bool Deterministic);

internal sealed record CoverageEvidence(double Line, double Branch);

internal sealed record GateSummary
{
    public int SchemaVersion { get; init; } = 1;
    public required string RunId { get; init; }
    public required string Profile { get; init; }
    public required string Scope { get; init; }
    public required bool Passed { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
    public required Dictionary<string, SourceEvidence> Sources { get; init; }
    public required HostEvidence Host { get; init; }
    public required IntegrationEvidence Integration { get; init; }
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

internal sealed record IntegrationEvidence(
    bool WorkspaceSnapshotVerified,
    bool ExternalInputsClean,
    bool Publishable);

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
