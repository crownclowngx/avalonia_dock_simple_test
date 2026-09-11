using System.Diagnostics;
using System.Text.Json;

namespace MyAvaloniaManagement.Gate;

internal static class GateApplication
{
    private const string Usage = """
        MyAvaloniaManagement Gate

          dotnet run --project tools/MyAvaloniaManagement.Gate -- verify [--scope host|all]
          dotnet run --project tools/MyAvaloniaManagement.Gate -- seal [--repeat]

        host 与 all 均验证本仓 Host + MyPlugTest。seal 额外执行覆盖率与 Windows Smoke。
        """;

    public static async Task<int> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        GateOptions options;
        try
        {
            options = GateOptions.Parse(arguments);
        }
        catch (GateUsageException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            var repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
            var configuration = GateConfiguration.Load(Path.Combine(
                repositoryRoot, "tools", "MyAvaloniaManagement.Gate", "gate.config.json"));
            var application = new GateRunner(repositoryRoot, configuration, Console.Out);
            await application.RunAsync(options, cancellationToken);
            return 0;
        }
        catch (Exception exception) when (exception is GateFailureException or IOException or JsonException)
        {
            Console.Error.WriteLine($"Gate 失败：{exception.Message}");
            if (exception is GateFailureException { Detail: { Length: > 0 } detail })
            {
                Console.Error.WriteLine(detail);
            }
            return 1;
        }
    }

    internal static string FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MyAvaloniaManagement.sln")) &&
                File.Exists(Path.Combine(current.FullName, "global.json")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new GateFailureException("无法从当前目录定位 MyAvaloniaManagement 仓库根目录。");
    }
}

internal sealed class GateRunner
{
    private readonly string repositoryRoot;
    private readonly GateConfiguration configuration;
    private readonly TextWriter output;
    private readonly ProcessRunner processes;
    private readonly GitRepository git;
    private readonly PackageBuilder packages;

    public GateRunner(string repositoryRoot, GateConfiguration configuration, TextWriter output)
    {
        this.repositoryRoot = repositoryRoot;
        this.configuration = configuration;
        this.output = output;
        processes = new(output);
        git = new(processes);
        packages = new(processes);
    }

    public async Task RunAsync(GateOptions options, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var sources = await InspectSourcesAsync(cancellationToken);
        if (options.Profile == GateProfile.Seal && !sources["main"].Clean)
        {
            throw new GateFailureException("正式 seal 要求主仓工作树干净；当前修改请先审阅并提交。使用 verify 验证脏工作树。");
        }

        await AssertSdkAsync(options, cancellationToken);
        var shortRevision = sources["main"].Revision[..Math.Min(12, sources["main"].Revision.Length)];
        var runId = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{shortRevision}";
        var runRoot = CreateUniqueRunRoot(runId);
        var summaryPath = Path.Combine(runRoot, "summary.json");
        var passResults = new List<GatePassResult>();
        Exception? failure = null;
        try
        {
            var passCount = options.Repeat ? 2 : 1;
            for (var pass = 1; pass <= passCount; pass++)
            {
                var result = await RunPassAsync(options, pass, runRoot, sources, cancellationToken);
                passResults.Add(result);
                if (!result.Passed)
                {
                    throw new GateFailureException(result.Error ?? $"第 {pass} 轮 Gate 失败。");
                }
            }

            if (options.Repeat)
            {
                AssertRepeatability(passResults[0], passResults[1]);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var passed = failure is null && passResults.Count == (options.Repeat ? 2 : 1) &&
            passResults.All(pass => pass.Passed);
        var isSeal = options.Profile == GateProfile.Seal;
        var summary = new GateSummary
        {
            RunId = runId,
            Profile = options.Profile.ToString().ToLowerInvariant(),
            Scope = options.Scope.ToString().ToLowerInvariant(),
            Passed = passed,
            StartedAtUtc = started,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            Sources = sources.ToDictionary(
                entry => entry.Key,
                entry => new SourceEvidence(entry.Value.Revision, entry.Value.Tree, entry.Value.Clean,
                    entry.Value.FileCount, entry.Value.Sha256),
                StringComparer.Ordinal),
            Host = new(isSeal && passed, isSeal && passed),
            Repeatability = new(options.Repeat, options.Repeat && passed),
            Passes = passResults.ToArray(),
            Error = failure?.Message,
        };
        EvidenceWriter.Write(summaryPath, summary);
        output.WriteLine($"Gate 证据：{summaryPath}");
        if (failure is not null)
        {
            throw failure is GateFailureException gateFailure
                ? gateFailure
                : new GateFailureException(failure.Message);
        }

        output.WriteLine($"Gate {summary.Profile} 通过：{passResults.Count} 轮，scope={summary.Scope}。");
    }

    private async Task<Dictionary<string, SourceSnapshot>> InspectSourcesAsync(CancellationToken cancellationToken) =>
        new(StringComparer.Ordinal)
        {
            ["main"] = await git.InspectAsync("main", repositoryRoot, cancellationToken),
        };

    private async Task AssertSdkAsync(GateOptions options, CancellationToken cancellationToken)
    {
        if (options.Profile == GateProfile.Seal &&
            (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture !=
                System.Runtime.InteropServices.Architecture.X64))
        {
            throw new GateFailureException("seal 只支持 Windows x64。");
        }

        using var global = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "global.json")));
        var expected = global.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        var actual = (await processes.RunCheckedAsync(
            "dotnet", ["--version"], repositoryRoot, null, null, cancellationToken)).Output.Trim();
        if (options.Profile == GateProfile.Seal && actual != expected)
        {
            throw new GateFailureException($"seal 要求 .NET SDK {expected}，当前为 {actual}。");
        }
    }

    private async Task<GatePassResult> RunPassAsync(
        GateOptions options,
        int pass,
        string runRoot,
        IReadOnlyDictionary<string, SourceSnapshot> sources,
        CancellationToken cancellationToken)
    {
        var evidenceRoot = Path.Combine(runRoot, $"pass-{pass}");
        Directory.CreateDirectory(evidenceRoot);
        var stages = new List<GateStageResult>();
        var packageEvidence = new Dictionary<string, PackageEvidence>(StringComparer.Ordinal);
        CoverageEvidence? hostCoverage = null;
        var hostCoverageFiles = new List<string>();
        OwnedDirectory? scratch = null;
        try
        {
            IReadOnlyDictionary<string, string> roots;
            if (options.Profile == GateProfile.Seal)
            {
                scratch = OwnedDirectory.Create(Path.GetTempPath(), $"MAVG-{Guid.NewGuid():N}"[..17]);
                roots = await CreateIsolatedRootsAsync(scratch.Path, sources, cancellationToken);
            }
            else
            {
                roots = sources.ToDictionary(entry => entry.Key, entry => entry.Value.Root, StringComparer.Ordinal);
            }

            var runtimeRoot = scratch?.Path ?? Path.Combine(evidenceRoot, "runtime");
            var environment = CreateEnvironment(runtimeRoot, options.Profile == GateProfile.Seal);
            var graph = GateExecutionGraph.ForProfile(options.Profile, async id =>
            {
                switch (id)
                {
                    case "restore":
                        await processes.RunCheckedAsync("dotnet", ["tool", "restore"], roots["main"], environment,
                            Path.Combine(evidenceRoot, "logs", "tool-restore.log"), cancellationToken);
                        await processes.RunCheckedAsync("dotnet",
                            ["restore", configuration.MainSolution, "--locked-mode", "--nologo"], roots["main"], environment,
                            Path.Combine(evidenceRoot, "logs", "restore-main.log"), cancellationToken);
                        break;
                    case "build":
                        await processes.RunCheckedAsync("dotnet",
                            ["build", configuration.MainSolution, "-c", "Release", "--no-restore", "--nologo", "-warnaserror", "-m:1"],
                            roots["main"], environment, Path.Combine(evidenceRoot, "logs", "build-main.log"), cancellationToken);
                        break;
                    case "tests":
                        foreach (var suite in configuration.TestSuites)
                        {
                            await RunTestSuiteAsync(options, roots["main"], suite.Id, suite.Project,
                                "Category!=PackageAcceptance", environment, evidenceRoot, hostCoverageFiles,
                                suite.CoverageGroup == "host", requireSingle: false, cancellationToken);
                        }
                        break;
                    case "contracts":
                        RunContractChecks(options, roots);
                        break;
                    case "packages":
                        foreach (var plugin in configuration.Plugins)
                        {
                            packageEvidence[plugin.Id] = await packages.BuildAsync(
                                plugin, roots["main"], Path.Combine(evidenceRoot, "packages"),
                                options.Profile == GateProfile.Seal, environment, cancellationToken);
                        }
                        break;
                    case "package-acceptance":
                        var packageRoot = PackageBuilder.Extract(packageEvidence["my-plug-test"],
                            Path.Combine(evidenceRoot, "package-acceptance", "my-plug-test"));
                        var packageEnvironment = new Dictionary<string, string?>(environment, StringComparer.Ordinal)
                        {
                            ["MYAVALONIA_MY_PLUG_TEST_PACKAGE_ROOT"] = Path.Combine(packageRoot, "Controls"),
                        };
                        await RunTestSuiteAsync(options, roots["main"], "my-plug-test-package",
                            configuration.TestSuites.Single(suite => suite.Id == "host-plugin").Project,
                            "Category=PackageAcceptance", packageEnvironment, evidenceRoot, hostCoverageFiles,
                            collectHostCoverage: true, requireSingle: true, cancellationToken);
                        break;
                    case "coverage":
                        hostCoverage = await CheckHostCoverageAsync(roots["main"], environment, evidenceRoot,
                            hostCoverageFiles, cancellationToken);
                        if (hostCoverage.Line < configuration.HostCoverage.MinimumLine ||
                            hostCoverage.Branch < configuration.HostCoverage.MinimumBranch)
                        {
                            throw new GateFailureException($"Host 覆盖率 {hostCoverage.Line}%/{hostCoverage.Branch}% 低于阈值。");
                        }
                        break;
                    case "windows-smoke":
                        await GateChecks.RunWindowsSmokeAsync(processes, roots["main"], configuration.WindowsSmokeProject,
                            Path.Combine(evidenceRoot, "windows-smoke"), environment, cancellationToken);
                        break;
                    default:
                        throw new GateFailureException($"未知 Gate 阶段：{id}。");
                }
            });

            await graph.ExecuteAsync((id, action) => StageAsync(id, stages, evidenceRoot, action));

            var result = new GatePassResult
            {
                Pass = pass,
                Passed = true,
                EvidenceRoot = evidenceRoot,
                Stages = stages.ToArray(),
                Packages = packageEvidence,
                HostCoverage = hostCoverage,
            };
            EvidenceWriter.Write(Path.Combine(evidenceRoot, "summary.json"), result);
            scratch?.Delete();
            return result;
        }
        catch (Exception exception)
        {
            var result = new GatePassResult
            {
                Pass = pass,
                Passed = false,
                Error = exception.Message,
                EvidenceRoot = evidenceRoot,
                Stages = stages.ToArray(),
                Packages = packageEvidence,
                HostCoverage = hostCoverage,
            };
            EvidenceWriter.Write(Path.Combine(evidenceRoot, "summary.json"), result);
            if (scratch is not null)
            {
                output.WriteLine($"失败隔离工作区已保留：{scratch.Path}");
            }
            return result;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> CreateIsolatedRootsAsync(
        string scratchRoot,
        IReadOnlyDictionary<string, SourceSnapshot> sources,
        CancellationToken cancellationToken)
    {
        var destination = Path.Combine(scratchRoot, "source", "main");
        await git.CloneCommitAsync(sources["main"], destination, cancellationToken);
        return new Dictionary<string, string>(StringComparer.Ordinal) { ["main"] = destination };
    }

    private async Task RunTestSuiteAsync(
        GateOptions options,
        string root,
        string id,
        string project,
        string filter,
        IReadOnlyDictionary<string, string?> environment,
        string evidenceRoot,
        ICollection<string> hostCoverageFiles,
        bool collectHostCoverage,
        bool requireSingle,
        CancellationToken cancellationToken)
    {
        var resultRoot = Path.Combine(evidenceRoot, "tests", id);
        var arguments = new List<string>
        {
            "test", project, "-c", "Release", "--no-build", "--no-restore", "-m:1",
            "--filter", filter, "--results-directory", resultRoot, "--logger", $"trx;LogFileName={id}.trx",
        };
        if (options.Profile == GateProfile.Seal && collectHostCoverage)
        {
            arguments.Add("--collect:XPlat Code Coverage");
            arguments.AddRange(["--", "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include=[MyAvaloniaManagement]*"]);
        }
        await processes.RunCheckedAsync("dotnet", arguments, root, environment,
            Path.Combine(resultRoot, "test.log"), cancellationToken);
        AssertTests(Path.Combine(resultRoot, $"{id}.trx"), id, requireSingle);
        if (options.Profile == GateProfile.Seal && collectHostCoverage)
        {
            hostCoverageFiles.Add(TestEvidenceReader.FindCoverageReport(resultRoot));
        }
    }

    private async Task<CoverageEvidence> CheckHostCoverageAsync(
        string root,
        IReadOnlyDictionary<string, string?> environment,
        string evidenceRoot,
        IReadOnlyCollection<string> hostCoverageFiles,
        CancellationToken cancellationToken)
    {
        if (hostCoverageFiles.Count != 4)
        {
            throw new GateFailureException("Host 覆盖率必须包含 Unit、Plugin、UI 和 MyPlugTest 包验收四份报告。");
        }
        var mergedRoot = Path.Combine(evidenceRoot, "coverage", "host");
        await processes.RunCheckedAsync(
            "dotnet", ["reportgenerator", $"-reports:{string.Join(';', hostCoverageFiles)}",
                $"-targetdir:{mergedRoot}", "-reporttypes:Cobertura", "-assemblyfilters:+MyAvaloniaManagement"],
            root, environment, Path.Combine(mergedRoot, "reportgenerator.log"), cancellationToken);
        var coveragePath = Path.Combine(mergedRoot, "Cobertura.xml");
        return TestEvidenceReader.ReadHostCoverage(coveragePath);
    }

    private void RunContractChecks(GateOptions options, IReadOnlyDictionary<string, string> roots)
    {
        foreach (var rule in configuration.ArchitectureRules)
        {
            GateChecks.AssertArchitectureRule(rule, roots["main"]);
        }
        GateChecks.AssertCurrentDocumentation(roots["main"]);
        if (options.Profile != GateProfile.Seal)
        {
            return;
        }

        foreach (var baseline in new[]
                 {
                     "Host/MyAvaloniaManagement.PluginSdk/ApiCompatibility/v3/PublicAPI.Unshipped.txt",
                     "Host/MyAvaloniaManagement.PluginSdk.UI/ApiCompatibility/v3/PublicAPI.Unshipped.txt",
                 })
        {
            var entries = File.ReadAllLines(Path.Combine(roots["main"], baseline))
                .Skip(1).Count(line => !string.IsNullOrWhiteSpace(line));
            if (entries != 0)
            {
                throw new GateFailureException($"API 基线仍有 {entries} 个 Unshipped 条目：{baseline}。");
            }
        }
    }

    internal static void AssertTests(string trxPath, string id, bool requireSingle = false)
    {
        var counts = TestEvidenceReader.ReadTrx(trxPath);
        if (counts.Failed != 0 || counts.Skipped != 0 || counts.Passed == 0 ||
            (requireSingle && counts.Passed != 1))
        {
            throw new GateFailureException($"测试 {id} 未通过：passed={counts.Passed}, failed={counts.Failed}, skipped={counts.Skipped}。");
        }
    }

    private async Task StageAsync(
        string id,
        ICollection<GateStageResult> stages,
        string evidenceRoot,
        Func<Task> action)
    {
        output.WriteLine($"\n[Gate] {id}");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await action();
            stopwatch.Stop();
            var result = new GateStageResult
            {
                Id = id,
                Status = "passed",
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                EvidencePath = evidenceRoot,
            };
            stages.Add(result);
            EvidenceWriter.Write(Path.Combine(evidenceRoot, "stages", $"{id}.json"), result);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var result = new GateStageResult
            {
                Id = id,
                Status = "failed",
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                EvidencePath = evidenceRoot,
                Error = exception.Message,
            };
            stages.Add(result);
            EvidenceWriter.Write(Path.Combine(evidenceRoot, "stages", $"{id}.json"), result);
            throw;
        }
    }

    private static IReadOnlyDictionary<string, string?> CreateEnvironment(string runtimeRoot, bool isolated)
    {
        Directory.CreateDirectory(runtimeRoot);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CI"] = "true",
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["MYAVALONIA_DATA_DIRECTORY"] = Path.Combine(runtimeRoot, "host-data"),
            ["MYAVALONIA_MY_PLUG_TEST_PACKAGE_ROOT"] = null,
        };
        if (isolated)
        {
            environment["DOTNET_CLI_HOME"] = Path.Combine(runtimeRoot, "dotnet-home");
            environment["NUGET_PACKAGES"] = Path.Combine(runtimeRoot, "nuget-packages");
            environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(runtimeRoot, "nuget-http-cache");
        }
        foreach (var path in environment.Values.Where(value => value is not null && Path.IsPathFullyQualified(value)))
        {
            Directory.CreateDirectory(path!);
        }
        return environment;
    }

    internal static void AssertRepeatability(GatePassResult first, GatePassResult second)
    {
        var firstPackages = first.Packages.OrderBy(entry => entry.Key)
            .Select(entry => $"{entry.Key}|{entry.Value.Sha256}|{entry.Value.ManifestSha256}|{entry.Value.Files}");
        var secondPackages = second.Packages.OrderBy(entry => entry.Key)
            .Select(entry => $"{entry.Key}|{entry.Value.Sha256}|{entry.Value.ManifestSha256}|{entry.Value.Files}");
        if (!firstPackages.SequenceEqual(secondPackages, StringComparer.Ordinal) ||
            first.HostCoverage != second.HostCoverage)
        {
            throw new GateFailureException("两轮 seal 的稳定证据不一致。");
        }
    }

    private string CreateUniqueRunRoot(string runId)
    {
        var parent = Path.Combine(repositoryRoot, "artifacts", "gate");
        Directory.CreateDirectory(parent);
        for (var suffix = 0; suffix < 100; suffix++)
        {
            var name = suffix == 0 ? runId : $"{runId}-{suffix}";
            var candidate = Path.Combine(parent, name);
            if (!Directory.Exists(candidate))
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
        }
        throw new GateFailureException("无法分配唯一 Gate 证据目录。");
    }
}
