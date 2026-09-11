using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MyAvaloniaManagement.Gate.Tests;

public sealed class LocalGateTests
{
    private static string ConfigurationPath => Path.Combine(
        GateApplication.FindRepositoryRoot(AppContext.BaseDirectory), "tools", "MyAvaloniaManagement.Gate", "gate.config.json");

    [Fact]
    public void ConfigurationContainsAllFiveSuitesAndOnlyMyPlugTest()
    {
        var configuration = GateConfiguration.Load(ConfigurationPath);
        Assert.Equal(2, configuration.SchemaVersion);
        Assert.Equal(5, configuration.TestSuites.Length);
        Assert.Equal("myavalonia.plugin.my-plug-test", Assert.Single(configuration.Plugins).PluginId);
        Assert.Equal(new CoverageThreshold { MinimumLine = 84.39, MinimumBranch = 70.58 }, configuration.HostCoverage);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("external")]
    [InlineData("missing-suite")]
    [InlineData("duplicate-suite")]
    [InlineData("external-path")]
    [InlineData("wrong-plugin")]
    public void ConfigurationRejectsIncompleteOrExternalInputs(string problem)
    {
        using var temporary = new TemporaryDirectory();
        var config = JsonNode.Parse(File.ReadAllText(ConfigurationPath))!;
        switch (problem)
        {
            case "schema": config["schemaVersion"] = 1; break;
            case "external": config["repositories"] = new JsonArray(); break;
            case "missing-suite": config["testSuites"]!.AsArray().RemoveAt(0); break;
            case "duplicate-suite": config["testSuites"]![0]!["id"] = "host-unit"; break;
            case "external-path": config["plugins"]![0]!["project"] = "../external/plugin.csproj"; break;
            case "wrong-plugin": config["plugins"]![0]!["pluginId"] = "other.plugin"; break;
        }
        var path = Path.Combine(temporary.Path, "gate.config.json");
        File.WriteAllText(path, config.ToJsonString());
        var exception = Record.Exception(() => GateConfiguration.Load(path));
        Assert.True(exception is GateFailureException or JsonException, exception?.ToString() ?? "配置错误未被拒绝");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfileRunsLocalStagesAndCoverageFollowsPackageAcceptance(bool seal)
    {
        var executed = new List<string>();
        var graph = GateExecutionGraph.ForProfile(seal ? GateProfile.Seal : GateProfile.Verify,
            id => { executed.Add(id); return Task.CompletedTask; });
        await graph.ExecuteAsync((_, action) => action());
        var expected = new List<string> { "restore", "build", "tests", "contracts", "packages", "package-acceptance" };
        if (seal) expected.AddRange(["coverage", "windows-smoke"]);
        Assert.Equal(expected, executed);
    }

    [Fact]
    public async Task FailedPackageAcceptancePreventsCoverageAndSmoke()
    {
        var executed = new List<string>();
        var graph = GateExecutionGraph.ForProfile(GateProfile.Seal, id =>
        {
            executed.Add(id);
            return id == "package-acceptance" ? Task.FromException(new GateFailureException("broken package")) : Task.CompletedTask;
        });
        await Assert.ThrowsAsync<GateFailureException>(() => graph.ExecuteAsync((_, action) => action()));
        Assert.DoesNotContain("coverage", executed);
        Assert.DoesNotContain("windows-smoke", executed);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 0)]
    public void PackageAcceptanceRequiresExactlyOnePassedTest(int passed, int failed, int skipped)
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "test.trx");
        File.WriteAllText(path, $"<TestRun><Counters passed=\"{passed}\" failed=\"{failed}\" notExecuted=\"{skipped}\" /></TestRun>");
        Assert.Throws<GateFailureException>(() => GateRunner.AssertTests(path, "package", requireSingle: true));
    }

    [Fact]
    public void CoverageAttachmentCopiesAreDeduplicatedButDifferentReportsFail()
    {
        using var temporary = new TemporaryDirectory();
        Assert.Throws<GateFailureException>(() => TestEvidenceReader.FindCoverageReport(temporary.Path));
        var collector = Path.Combine(temporary.Path, "collector");
        var attachment = Path.Combine(temporary.Path, "In", "machine");
        Directory.CreateDirectory(collector);
        Directory.CreateDirectory(attachment);
        File.WriteAllText(Path.Combine(collector, "coverage.cobertura.xml"), "<coverage />");
        var copy = Path.Combine(attachment, "coverage.cobertura.xml");
        File.WriteAllText(copy, "<coverage />");
        Assert.True(File.Exists(TestEvidenceReader.FindCoverageReport(temporary.Path)));
        File.WriteAllText(copy, "<coverage line-rate=\"0.5\" />");
        Assert.Throws<GateFailureException>(() => TestEvidenceReader.FindCoverageReport(temporary.Path));
    }

    [Fact]
    public void HostCoverageRejectsOtherAssembliesAndEmptyReports()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "coverage.xml");
        foreach (var packages in new[] { "", "<package name=\"MyAvaloniaManagement.PluginSdk\"><line /></package>",
                     "<package name=\"MyAvaloniaManagement\" />",
                     "<package name=\"MyAvaloniaManagement\"><line /></package><package name=\"MyPlugTest\"><line /></package>" })
        {
            File.WriteAllText(path, $"<coverage line-rate=\"1\" branch-rate=\"1\"><packages>{packages}</packages></coverage>");
            Assert.Throws<GateFailureException>(() => TestEvidenceReader.ReadHostCoverage(path));
        }
        File.WriteAllText(path, "<coverage line-rate=\"0.85\" branch-rate=\"0.71\"><packages><package name=\"MyAvaloniaManagement\"><line /></package></packages></coverage>");
        Assert.Equal(new CoverageEvidence(85, 71), TestEvidenceReader.ReadHostCoverage(path));
    }

    [Fact]
    public void DocumentationChecksLocalLinksWithoutRequiringNeighborRepositories()
    {
        using var temporary = new TemporaryDirectory();
        var root = Path.Combine(temporary.Path, "host");
        Directory.CreateDirectory(root);
        var readme = Path.Combine(root, "README.md");
        File.WriteAllText(readme, "[external](../external/README.md)\n[self](README.md)");
        GateChecks.AssertCurrentDocumentation(root);
        File.AppendAllText(readme, "\n[broken](missing.md)");
        Assert.Throws<GateFailureException>(() => GateChecks.AssertCurrentDocumentation(root));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"schemaVersion\":2,\"pluginId\":\"wrong.plugin\",\"pluginVersion\":\"1.0.0\",\"entryPoint\":{\"assembly\":\"MyPlugTest.dll\",\"type\":\"Test.Module\"},\"sdk\":{\"minInclusive\":\"3.0.0\",\"maxExclusive\":\"4.0.0\"}}")]
    public void PackageRejectsMalformedManifestOrWrongIdentity(string manifest)
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "test.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var file in new[] { "plugin.manifest.json", "MyPlugTest.dll", "MyPlugTest.deps.json" })
            {
                using var writer = new StreamWriter(archive.CreateEntry("Controls/MyPlugTest/" + file).Open());
                writer.Write(file.EndsWith("manifest.json", StringComparison.Ordinal) ? manifest : "{}");
            }
        }
        var plugin = GateConfiguration.Load(ConfigurationPath).Plugins[0];
        var exception = Record.Exception(() => PackageBuilder.ValidatePackage(plugin, path, deterministic: false));
        Assert.True(exception is GateFailureException or JsonException, exception?.ToString() ?? "无效包未被拒绝");
    }

    [Fact]
    public void SummaryV2ContainsOnlyLocalSourcesAndNoIntegrationClaim()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "summary.json");
        EvidenceWriter.Write(path, new GateSummary
        {
            RunId = "test", Profile = "verify", Scope = "all", Passed = true,
            StartedAtUtc = DateTimeOffset.UtcNow, FinishedAtUtc = DateTimeOffset.UtcNow,
            Sources = new() { ["main"] = new("revision", "tree", false, 1, "hash") },
            Host = new(false, false), Repeatability = new(false, false), Passes = [],
        });
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("main", Assert.Single(json.RootElement.GetProperty("sources").EnumerateObject()).Name);
        Assert.False(json.RootElement.TryGetProperty("integration", out _));
        Assert.False(json.RootElement.GetProperty("host").GetProperty("publishable").GetBoolean());
    }

    [Fact]
    public async Task FailedRunPreservesFailedPassAndNeverGrantsReleaseEligibility()
    {
        using var temporary = new TemporaryDirectory();
        var processes = new ProcessRunner(TextWriter.Null);
        await processes.RunCheckedAsync("git", ["init", "--quiet"], temporary.Path, null, null, CancellationToken.None);
        await processes.RunCheckedAsync("git", ["-c", "user.name=Gate Test", "-c", "user.email=gate@localhost",
            "commit", "--allow-empty", "--quiet", "-m", "fixture"], temporary.Path, null, null, CancellationToken.None);
        var repository = GateApplication.FindRepositoryRoot(AppContext.BaseDirectory);
        File.Copy(Path.Combine(repository, "global.json"), Path.Combine(temporary.Path, "global.json"));
        var runner = new GateRunner(temporary.Path, GateConfiguration.Load(ConfigurationPath), TextWriter.Null);
        await Assert.ThrowsAsync<GateFailureException>(() => runner.RunAsync(GateOptions.Parse(["verify"]), CancellationToken.None));
        var summary = Directory.GetFiles(Path.Combine(temporary.Path, "artifacts", "gate"), "summary.json", SearchOption.AllDirectories)
            .Single(path => !Path.GetDirectoryName(path)!.EndsWith("pass-1", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(File.ReadAllText(summary));
        Assert.False(json.RootElement.GetProperty("passed").GetBoolean());
        var pass = Assert.Single(json.RootElement.GetProperty("passes").EnumerateArray());
        Assert.False(pass.GetProperty("passed").GetBoolean());
        Assert.Equal("failed", Assert.Single(pass.GetProperty("stages").EnumerateArray()).GetProperty("status").GetString());
        Assert.False(json.RootElement.GetProperty("host").GetProperty("publishable").GetBoolean());
        Assert.False(json.RootElement.GetProperty("host").GetProperty("releaseEligible").GetBoolean());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MAVG-local-tests-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
    }
}
