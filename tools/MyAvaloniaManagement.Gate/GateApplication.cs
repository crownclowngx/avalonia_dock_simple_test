using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace MyAvaloniaManagement.Gate;

internal static class GateApplication
{
    private const string Usage = """
        MyAvaloniaManagement Gate

          dotnet run --project tools/MyAvaloniaManagement.Gate -- verify [--scope host|workflow|workbench|all]
          dotnet run --project tools/MyAvaloniaManagement.Gate -- seal [--repeat]

        可选外部仓库覆盖：
          --workflow-studio <path>  --classic-game <path>
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
        var sources = await InspectSourcesAsync(options, cancellationToken);
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
                passResults.Add(await RunPassAsync(options, pass, runRoot, sources, cancellationToken));
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

        var externalSources = sources.Values.Where(source => source.Id != "main").ToArray();
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
            Integration = new(passed && externalSources.Length > 0,
                externalSources.All(source => source.Clean), false),
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

    private async Task<Dictionary<string, SourceSnapshot>> InspectSourcesAsync(
        GateOptions options,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, SourceSnapshot>(StringComparer.Ordinal)
        {
            ["main"] = await git.InspectAsync("main", repositoryRoot, cancellationToken),
        };
        foreach (var repository in configuration.Repositories.Where(item => options.Includes(item.Scope)))
        {
            var overridePath = repository.Id switch
            {
                "workflow-studio" => options.WorkflowStudioRoot,
                "classic-game" => options.ClassicGameRoot,
                _ => null,
            };
            var root = Path.GetFullPath(overridePath ?? Path.Combine(repositoryRoot, repository.DefaultPath));
            result[repository.Id] = await git.InspectAsync(repository.Id, root, cancellationToken);
        }
        return result;
    }

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
            var graph = new GateExecutionGraph()
                .Add("restore", async () =>
            {
                await processes.RunCheckedAsync("dotnet", ["tool", "restore"], roots["main"], environment,
                    Path.Combine(evidenceRoot, "logs", "tool-restore.log"), cancellationToken);
                await RestoreRepositoriesAsync(options, roots, environment, evidenceRoot, cancellationToken);
            })
                .Add("build", () =>
                    BuildRepositoriesAsync(options, roots, environment, evidenceRoot, cancellationToken))
                .Add("tests", async () =>
            {
                hostCoverage = await RunTestsAsync(
                    options, roots, environment, evidenceRoot, cancellationToken);
            })
                .Add("contracts", () =>
            {
                RunContractChecks(options, roots);
                return Task.CompletedTask;
            })
                .Add("packages", async () =>
            {
                foreach (var plugin in configuration.Plugins.Where(item => options.Includes(item.Scope)))
                {
                    packageEvidence[plugin.Id] = await packages.BuildAsync(
                        plugin, roots[plugin.Repository], Path.Combine(evidenceRoot, "packages"),
                        options.Profile == GateProfile.Seal, environment, cancellationToken);
                }
            });
            if (options.Scope == GateScope.All || options.Scope is GateScope.Workflow or GateScope.Workbench)
            {
                graph.Add("cross-repository", () =>
                    RunCrossRepositoryTestsAsync(options, roots, packageEvidence, environment, evidenceRoot,
                        cancellationToken));
            }
            if (options.Scope == GateScope.All || options.Scope == GateScope.Workflow)
            {
                graph.Add("resource-harness", () =>
                    RunHarnessAsync(options, roots[configuration.Harness.Repository], roots["main"], environment, evidenceRoot, cancellationToken));
            }
            if (options.Profile == GateProfile.Seal)
            {
                graph.Add("windows-smoke", () =>
                    GateChecks.RunWindowsSmokeAsync(processes, roots["main"], configuration.WindowsSmokeProject,
                        Path.Combine(evidenceRoot, "windows-smoke"), environment, cancellationToken));
            }

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
            throw exception is GateFailureException ? exception : new GateFailureException(exception.Message);
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> CreateIsolatedRootsAsync(
        string scratchRoot,
        IReadOnlyDictionary<string, SourceSnapshot> sources,
        CancellationToken cancellationToken)
    {
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sources.Values)
        {
            var destination = Path.Combine(scratchRoot, "source", source.Id);
            if (source.Id == "main")
            {
                await git.CloneCommitAsync(source, destination, cancellationToken);
            }
            else
            {
                await git.CopyWorkspaceAsync(source, destination, cancellationToken);
            }
            roots[source.Id] = destination;
        }
        return roots;
    }

    private async Task RestoreRepositoriesAsync(
        GateOptions options,
        IReadOnlyDictionary<string, string> roots,
        IReadOnlyDictionary<string, string?> environment,
        string evidenceRoot,
        CancellationToken cancellationToken)
    {
        await DotnetForRepository("main", configuration.MainSolution);
        foreach (var repository in configuration.Repositories.Where(item => roots.ContainsKey(item.Id)))
        {
            await DotnetForRepository(repository.Id, repository.Solution);
        }

        async Task DotnetForRepository(string id, string solution)
        {
            await processes.RunCheckedAsync("dotnet", ["restore", solution, "--locked-mode", "--nologo"],
                roots[id], environment, Path.Combine(evidenceRoot, "logs", $"restore-{id}.log"), cancellationToken);
        }
    }

    private async Task BuildRepositoriesAsync(
        GateOptions options,
        IReadOnlyDictionary<string, string> roots,
        IReadOnlyDictionary<string, string?> environment,
        string evidenceRoot,
        CancellationToken cancellationToken)
    {
        await Build("main", configuration.MainSolution);
        foreach (var repository in configuration.Repositories.Where(item => roots.ContainsKey(item.Id)))
        {
            await Build(repository.Id, repository.Solution);
        }

        async Task Build(string id, string solution)
        {
            await processes.RunCheckedAsync(
                "dotnet", ["build", solution, "-c", "Release", "--no-restore", "--nologo", "-warnaserror", "-m:1"],
                roots[id], environment, Path.Combine(evidenceRoot, "logs", $"build-{id}.log"), cancellationToken);
        }
    }

    private async Task<CoverageEvidence?> RunTestsAsync(
        GateOptions options,
        IReadOnlyDictionary<string, string> roots,
        IReadOnlyDictionary<string, string?> environment,
        string evidenceRoot,
        CancellationToken cancellationToken)
    {
        var hostCoverageFiles = new List<string>();
        foreach (var suite in configuration.TestSuites.Where(item => options.Includes(item.Scope)))
        {
            var resultRoot = Path.Combine(evidenceRoot, "tests", suite.Id);
            var arguments = new List<string>
            {
                "test", suite.Project, "-c", "Release", "--no-build", "--no-restore", "-m:1",
                "--results-directory", resultRoot, "--logger", $"trx;LogFileName={suite.Id}.trx",
            };
            if (options.Profile == GateProfile.Seal)
            {
                arguments.Add("--collect:XPlat Code Coverage");
            }
            await processes.RunCheckedAsync("dotnet", arguments, roots["main"], environment,
                Path.Combine(resultRoot, "test.log"), cancellationToken);
            AssertTests(Path.Combine(resultRoot, $"{suite.Id}.trx"), suite.Id);
            if (options.Profile == GateProfile.Seal && suite.CoverageGroup == "host")
            {
                hostCoverageFiles.AddRange(Directory.GetFiles(resultRoot, "coverage.cobertura.xml", SearchOption.AllDirectories));
            }
        }

        foreach (var repository in configuration.Repositories.Where(item => roots.ContainsKey(item.Id)))
        {
            var resultRoot = Path.Combine(evidenceRoot, "tests", repository.Id);
            var arguments = new List<string>
            {
                "test", repository.TestProject, "-c", "Release", "--no-build", "--no-restore", "-m:1",
                "--results-directory", resultRoot, "--logger", $"trx;LogFileName={repository.Id}.trx",
            };
            if (options.Profile == GateProfile.Seal)
            {
                arguments.Add("--collect:XPlat Code Coverage");
            }
            await processes.RunCheckedAsync("dotnet", arguments, roots[repository.Id], environment,
                Path.Combine(resultRoot, "test.log"), cancellationToken);
            AssertTests(Path.Combine(resultRoot, $"{repository.Id}.trx"), repository.Id);
            if (options.Profile == GateProfile.Seal)
            {
                string coveragePath;
                if (!string.IsNullOrWhiteSpace(repository.CoverageScript))
                {
                    var aggregateRoot = Path.Combine(resultRoot, "aggregate");
                    await processes.RunCheckedAsync(
                        "pwsh", ["-NoProfile", "-File", repository.CoverageScript,
                            "-HostRepositoryRoot", roots["main"], "-OutputRoot", aggregateRoot],
                        roots[repository.Id], environment, Path.Combine(resultRoot, "aggregate.log"), cancellationToken);
                    coveragePath = Path.Combine(aggregateRoot, "merged", "Cobertura.xml");
                }
                else
                {
                    coveragePath = Directory.GetFiles(resultRoot, "coverage.cobertura.xml", SearchOption.AllDirectories).Single();
                }
                var coverage = TestEvidenceReader.ReadCoverage(coveragePath);
                if (coverage.Line < repository.MinimumLineCoverage || coverage.Branch < repository.MinimumBranchCoverage)
                {
                    throw new GateFailureException($"{repository.Id} 覆盖率 {coverage.Line}%/{coverage.Branch}% 低于阈值。");
                }
            }
            if (repository.SelfTestArguments.Length > 0)
            {
                var selfTestArguments = new List<string>
                {
                    "run", "--project", repository.StandaloneProject, "-c", "Release", "--no-build", "--no-restore",
                };
                selfTestArguments.Add("--");
                selfTestArguments.AddRange(repository.SelfTestArguments);
                var selfTest = await processes.RunCheckedAsync(
                    "dotnet", selfTestArguments,
                    roots[repository.Id], environment, Path.Combine(resultRoot, "self-test.log"), cancellationToken);
                if (!selfTest.Output.Contains(repository.SelfTestSuccessText, StringComparison.Ordinal))
                {
                    throw new GateFailureException($"{repository.Id} Standalone 自检没有输出预期结果。");
                }
            }
        }

        if (options.Profile != GateProfile.Seal || hostCoverageFiles.Count == 0)
        {
            return null;
        }
        var mergedRoot = Path.Combine(evidenceRoot, "coverage", "host");
        await processes.RunCheckedAsync(
            "dotnet", ["reportgenerator", $"-reports:{string.Join(';', hostCoverageFiles)}",
                $"-targetdir:{mergedRoot}", "-reporttypes:Cobertura"],
            roots["main"], environment, Path.Combine(mergedRoot, "reportgenerator.log"), cancellationToken);
        var hostCoverage = TestEvidenceReader.ReadCoverage(Path.Combine(mergedRoot, "Cobertura.xml"));
        if (hostCoverage.Line < configuration.HostCoverage.MinimumLine ||
            hostCoverage.Branch < configuration.HostCoverage.MinimumBranch)
        {
            throw new GateFailureException($"Host 覆盖率 {hostCoverage.Line}%/{hostCoverage.Branch}% 低于阈值。");
        }
        return hostCoverage;
    }

    private void RunContractChecks(GateOptions options, IReadOnlyDictionary<string, string> roots)
    {
        foreach (var rule in configuration.ArchitectureRules.Where(item => options.Includes(item.Scope)))
        {
            GateChecks.AssertArchitectureRule(rule, roots[rule.Repository]);
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

    private async Task RunCrossRepositoryTestsAsync(
        GateOptions options,
        IReadOnlyDictionary<string, string> roots,
        IReadOnlyDictionary<string, PackageEvidence> packageEvidence,
        IReadOnlyDictionary<string, string?> environment,
        string evidenceRoot,
        CancellationToken cancellationToken)
    {
        var hostRoot = roots["main"];
        if (packageEvidence.TryGetValue("workflow-studio", out var workflow) &&
            packageEvidence.TryGetValue("classic-game", out var classic))
        {
            var commandRoot = Path.Combine(evidenceRoot, "integration", "workbench");
            PackageBuilder.Extract(workflow, commandRoot);
            PackageBuilder.Extract(classic, commandRoot);
            var commandEnvironment = new Dictionary<string, string?>(environment, StringComparer.Ordinal)
            {
                ["MYAVALONIA_WORKBENCH_COMMAND_G10_EXTERNAL_PLUGIN_ROOT"] = Path.Combine(commandRoot, "Controls"),
            };
            await RunFiltered("workbench-plugin", "Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj",
                "FullyQualifiedName~WorkbenchCommandG10CrossRepositoryPackageTests", commandEnvironment);
            await RunFiltered("workbench-ui", "Host/MyAvaloniaManagement.UiTests/MyAvaloniaManagement.UiTests.csproj",
                "FullyQualifiedName~WorkbenchCommandG10CrossRepositoryUiTests", commandEnvironment);
        }

        if (packageEvidence.TryGetValue("workflow-studio", out workflow) &&
            packageEvidence.TryGetValue("video-security-player", out var videoPlayer))
        {
            var workflowRoot = Path.Combine(evidenceRoot, "integration", "workflow");
            PackageBuilder.Extract(workflow, workflowRoot);
            PackageBuilder.Extract(videoPlayer, workflowRoot);
            var workflowEnvironment = new Dictionary<string, string?>(environment, StringComparer.Ordinal)
            {
                ["MYAVALONIA_WORKFLOW_G4_PLUGIN_ROOT"] = Path.Combine(workflowRoot, "Controls"),
                ["MYAVALONIA_WORKFLOW_G4_MEDIA_PATH"] = Path.Combine(roots["video-security-player"],
                    "tests", "VideoSecurityPlayer.Tests", "TestAssets", "RealMedia", "synthetic-av-short.mp4"),
            };
            await RunFiltered("workflow-action", "tests/VideoSecurityPlayer.HostTests/VideoSecurityPlayer.HostTests.csproj",
                "FullyQualifiedName~WorkflowActionG4IntegrationTests", workflowEnvironment, "video-security-player");
        }

        async Task RunFiltered(string id, string project, string filter,
            IReadOnlyDictionary<string, string?> processEnvironment, string repository = "main")
        {
            var resultRoot = Path.Combine(evidenceRoot, "tests", id);
            var arguments = new List<string>
            {
                "test", project, "-c", "Release", "--no-build", "--no-restore", "-m:1",
                "--filter", filter, "--results-directory", resultRoot,
                "--logger", $"trx;LogFileName={id}.trx",
            };
            if (repository != "main")
            {
                arguments.Remove("--no-build");
                arguments.Remove("--no-restore");
                arguments.Add($"-p:HostRepositoryRoot={hostRoot}");
                arguments.Add("-p:SkipPluginDeploy=true");
            }
            await processes.RunCheckedAsync(
                "dotnet", arguments,
                roots[repository], processEnvironment, Path.Combine(resultRoot, "test.log"), cancellationToken);
            AssertTests(Path.Combine(resultRoot, $"{id}.trx"), id, requireSingle: true);
        }
    }

    private async Task RunHarnessAsync(
        GateOptions options,
        string root,
        string hostRoot,
        IReadOnlyDictionary<string, string?> environment,
        string evidenceRoot,
        CancellationToken cancellationToken)
    {
        var report = Path.Combine(evidenceRoot, "harness", "report.json");
        var cycles = options.Profile == GateProfile.Seal ? 20 : 1;
        var arguments = new List<string>
        {
            "run", "--project", configuration.Harness.Project, "-c", "Release",
            $"-p:HostRepositoryRoot={hostRoot}", "-p:SkipPluginDeploy=true",
        };
        arguments.AddRange(["--", "--suite", "g3", "--cycles", cycles.ToString(), "--report", report]);
        await processes.RunCheckedAsync(
            "dotnet", arguments,
            root, environment, Path.Combine(evidenceRoot, "harness", "harness.log"), cancellationToken);
        using var result = JsonDocument.Parse(File.ReadAllText(report));
        if (!result.RootElement.GetProperty("Success").GetBoolean() ||
            result.RootElement.GetProperty("Cycles").GetInt32() != cycles)
        {
            throw new GateFailureException("VideoSecurityPlayer 资源 Harness 报告未通过。");
        }
    }

    private static void AssertTests(string trxPath, string id, bool requireSingle = false)
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
