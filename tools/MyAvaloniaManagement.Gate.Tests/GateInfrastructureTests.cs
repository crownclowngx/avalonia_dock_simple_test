using System.IO.Compression;
using System.Text;

namespace MyAvaloniaManagement.Gate.Tests;

public sealed class GateInfrastructureTests
{
    [Fact]
    public async Task MainRepositoryStillRequiresCommitForGateEvidence()
    {
        using var temporary = new TemporaryDirectory();
        var processes = new ProcessRunner(TextWriter.Null);
        await processes.RunCheckedAsync("git", ["init", "--quiet"], temporary.Path, null, null, CancellationToken.None);
        await Assert.ThrowsAsync<GateFailureException>(() =>
            new GitRepository(processes).InspectAsync("main", temporary.Path, CancellationToken.None));
    }

    [Fact]
    public async Task 顺序计划每个阶段只执行一次且保留阶段包装器()
    {
        var executed = new List<string>();
        var graph = GateExecutionPlan.ForProfile(GateProfile.Verify,
            id => { executed.Add(id + "-action"); return Task.CompletedTask; });

        await graph.ExecuteAsync(async (id, action) =>
        {
            executed.Add(id);
            await action();
        });

        Assert.Equal(graph.Stages.Count, graph.Stages.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(graph.Stages.SelectMany(id => new[] { id, id + "-action" }), executed);
    }

    [Theory]
    [InlineData("avalonia-layout-patch")]
    [InlineData("dock-patch")]
    [InlineData("restore")]
    [InlineData("build")]
    [InlineData("contracts")]
    [InlineData("tests")]
    [InlineData("packages")]
    [InlineData("package-acceptance")]
    public async Task 每一开发阶段失败均立即停止(string failure)
    {
        var executed = new List<string>();
        var graph = GateExecutionPlan.ForProfile(GateProfile.Verify, id =>
        {
            executed.Add(id);
            return id == failure ? Task.FromException(new GateFailureException("failed")) : Task.CompletedTask;
        });

        await Assert.ThrowsAsync<GateFailureException>(() =>
            graph.ExecuteAsync((_, action) => action()));

        Assert.Equal(graph.Stages.TakeWhile(id => id != failure).Append(failure), executed);
        Assert.DoesNotContain("coverage", executed);
        Assert.DoesNotContain("windows-smoke", executed);
    }

    [Fact]
    public void FingerprintChangesWithPathOrContent()
    {
        using var temporary = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temporary.Path, "a.txt"), "one");
        var first = GitRepository.ComputeFingerprint(temporary.Path, ["a.txt"]);
        File.WriteAllText(Path.Combine(temporary.Path, "a.txt"), "two");
        var second = GitRepository.ComputeFingerprint(temporary.Path, ["a.txt"]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void OwnedDirectoryRejectsParentAndSibling()
    {
        using var temporary = new TemporaryDirectory();
        Assert.Throws<GateFailureException>(() =>
            OwnedDirectory.AssertChild(temporary.Path, temporary.Path));
        Assert.Throws<GateFailureException>(() =>
            OwnedDirectory.AssertChild(Path.Combine(Path.GetDirectoryName(temporary.Path)!, "sibling"), temporary.Path));
    }

    [Fact]
    public void TrxAndCoberturaReadersUseMachineReadableCounters()
    {
        using var temporary = new TemporaryDirectory();
        var trx = Path.Combine(temporary.Path, "result.trx");
        TrxFixture.Create(passed: 3).Save(trx);
        var coverage = Path.Combine(temporary.Path, "coverage.xml");
        File.WriteAllText(coverage, "<coverage line-rate=\"0.85\" branch-rate=\"0.75\" />");

        Assert.Equal(new TestCounts(3, 3, 3, 0, 0, 0), TestEvidenceReader.ReadTrx(trx).Counts);
        Assert.Equal(new CoverageEvidence(85, 75), TestEvidenceReader.ReadCoverage(coverage));
    }

    [Fact]
    public void TrxReaderKeepsSkippedTestsVisible()
    {
        using var temporary = new TemporaryDirectory();
        var trx = Path.Combine(temporary.Path, "result.trx");
        TrxFixture.Create(passed: 2, skipped: 1).Save(trx);

        Assert.Equal(new TestCounts(3, 2, 2, 0, 1, 0), TestEvidenceReader.ReadTrx(trx).Counts);
    }

    [Fact]
    public async Task ProcessRunnerPropagatesNonZeroExitCode()
    {
        using var temporary = new TemporaryDirectory();
        var runner = new ProcessRunner(TextWriter.Null);
        var exception = await Assert.ThrowsAsync<GateFailureException>(() =>
            runner.RunCheckedAsync("dotnet", ["definitely-not-a-command"], temporary.Path,
                null, null, CancellationToken.None));

        Assert.Contains("exit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackageValidatorAcceptsStrictManifestAndRequiredPayload()
    {
        using var temporary = new TemporaryDirectory();
        var zip = CreatePackage(temporary.Path, includeAssembly: true, schemaVersion: 2);

        var evidence = PackageBuilder.ValidatePackage(TestPlugin(), zip, deterministic: false);

        Assert.Equal("test.plugin", evidence.PluginId);
        Assert.Equal(3, evidence.Files);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public void PackageValidatorRejectsMissingPayloadOrInvalidManifest(bool includeAssembly, int schemaVersion)
    {
        using var temporary = new TemporaryDirectory();
        var zip = CreatePackage(temporary.Path, includeAssembly, schemaVersion);

        Assert.Throws<GateFailureException>(() =>
            PackageBuilder.ValidatePackage(TestPlugin(), zip, deterministic: false));
    }

    [Fact]
    public void RepeatabilityComparisonIgnoresPathsButRejectsArtifactDrift()
    {
        var first = Pass("C:/first", "ABC");
        var stableSecond = Pass("D:/isolated", "ABC");
        var driftingSecond = Pass("D:/isolated", "DEF");

        GateRunner.AssertRepeatability(first, stableSecond);
        Assert.Throws<GateFailureException>(() => GateRunner.AssertRepeatability(first, driftingSecond));
    }

    [Fact]
    public void EvidenceUsesSingleCamelCaseSchema()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "summary.json");
        EvidenceWriter.Write(path, new HostEvidence(true, false));
        var json = File.ReadAllText(path);

        Assert.Contains("\"releaseEligible\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Published", json, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MAVG-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static PluginConfiguration TestPlugin() => new()
    {
        Id = "test",
        PluginId = "test.plugin",
        Project = "test.csproj",
        DirectoryName = "Test",
        AssemblyName = "Test.Plugin",
    };

    private static GatePassResult Pass(string evidenceRoot, string packageHash) => new()
    {
        Pass = 1,
        Passed = true,
        EvidenceRoot = evidenceRoot,
        Stages = [],
        Packages = new Dictionary<string, PackageEvidence>(StringComparer.Ordinal)
        {
            ["test"] = new("test.plugin", Path.Combine(evidenceRoot, "test.zip"), packageHash, 3, "MANIFEST", true),
        },
        HostCoverage = new(90, 80),
    };

    private static string CreatePackage(string root, bool includeAssembly, int schemaVersion)
    {
        var path = Path.Combine(root, "test.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write("Controls/Test/plugin.manifest.json",
            $"{{\"schemaVersion\":{schemaVersion},\"pluginId\":\"test.plugin\",\"pluginVersion\":\"1.0.0\",\"entryPoint\":{{\"assembly\":\"Test.Plugin.dll\",\"type\":\"Test.Plugin.Module\"}},\"sdk\":{{\"minInclusive\":\"3.0.0\",\"maxExclusive\":\"4.0.0\"}}}}");
        Write("Controls/Test/Test.Plugin.deps.json", "{}");
        if (includeAssembly)
        {
            Write("Controls/Test/Test.Plugin.dll", "assembly");
        }
        return path;

        void Write(string name, string value)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
            writer.Write(value);
        }
    }
}
