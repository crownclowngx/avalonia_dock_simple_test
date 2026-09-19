using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Enablement;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.Workflow;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>复用测试程序集作为子进程入口，避免给生产 Host 增加测试开关或另建一套加载器。</summary>
public sealed class PluginEnablementRestartTests
{
    private const string RootVariable = "MYAVALONIA_ENABLEMENT_TEST_ROOT";
    private const string PhaseVariable = "MYAVALONIA_ENABLEMENT_TEST_PHASE";

    [Fact]
    public async Task 三次新进程证明禁用与重新启用且所有贡献随重启变化()
    {
        using var files = new EnablementPluginFiles();
        PrepareProbe(files.ManifestPath);
        var pids = new HashSet<int>();
        for (var phase = 0; phase < 3; phase++)
        {
            var start = new ProcessStartInfo("dotnet")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(PluginEnablementRestartTests).Assembly.Location);
            start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName=MyAvaloniaManagement.PluginTests.PluginEnablementRestartTests.独立进程验收入口");
            start.ArgumentList.Add("--ResultsDirectory:" + Path.Combine(files.DirectoryPath, "test-results", phase.ToString()));
            start.Environment[RootVariable] = files.DirectoryPath;
            start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            start.Environment[PhaseVariable] = phase.ToString();
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            Assert.True(process.ExitCode == 0, await output + Environment.NewLine + await error);
            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(files.DirectoryPath, $"phase-{phase}.json")));
            Assert.True(pids.Add(result.RootElement.GetProperty("pid").GetInt32()));
            Assert.Equal(phase != 1, result.RootElement.GetProperty("loaded").GetBoolean());
            var phases = result.RootElement.GetProperty("trace").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.Equal(phase == 1 ? [] : new[] { "module", "configure", "lifecycle-constructor", "initialize", "shutdown", "dispose" }, phases);
            // 收据保存在测试输出目录，父进程随后可以完整清理已退出进程曾加载的临时 DLL。
            var evidence = Path.Combine(AppContext.BaseDirectory, "TestResults", "v13-restart");
            Directory.CreateDirectory(evidence);
            File.Copy(Path.Combine(files.DirectoryPath, $"phase-{phase}.json"), Path.Combine(evidence, $"phase-{phase}.json"), true);
        }
        Assert.Equal(3, pids.Count);
    }

    [Fact]
    public async Task 独立进程验收入口()
    {
        var root = Environment.GetEnvironmentVariable(RootVariable);
        if (root is null)
        {
            // 普通测试发现也实际执行一次完整组合，不用“直接返回”制造一个虚假的通过案例。
            using var files = new EnablementPluginFiles();
            PrepareProbe(files.ManifestPath);
            await RunStage(files.DirectoryPath, 0);
        }
        else await RunStage(root, int.Parse(Environment.GetEnvironmentVariable(PhaseVariable)!));
    }

    private static async Task RunStage(string root, int phase)
    {
        var dataRoot = Path.Combine(root, "Data");
        var trace = Path.Combine(root, $"trace-{phase}.txt");
        var previousTrace = Environment.GetEnvironmentVariable("MYAVALONIA_ENABLEMENT_TEST_TRACE");
        Environment.SetEnvironmentVariable("MYAVALONIA_ENABLEMENT_TEST_TRACE", trace);
        try
        {
            var store = new PluginEnablementSettingsStore(Path.Combine(dataRoot, PluginEnablementSettingsStore.FileName));
            var initial = store.Load();
            var discovery = AssemblyLoaderHelper.Discover(Path.Combine(root, "Controls"), initial, dataRoot);
            Assert.Empty(discovery.Diagnostics);
            Assert.Equal(phase != 1, discovery.Assemblies.Count == 1);
            if (phase == 1) PluginEnablementLoadingTests.AssertNoLoadedFiles(Path.Combine(root, "Controls"));
            using var diagnostics = HostDiagnosticSession.Start(dataRoot);
            var builder = new PluginRegistryBuilder();
            using var owners = new PluginProviderOwner();
            var scopes = new DocumentScopeRegistry();
            var catalog = PluginModuleCatalog.Discover(discovery);
            var services = new ServiceCollection();
            services.AddApplicationServices(builder, owners, scopes);
            services.AddSingleton(catalog);
            services.AddSingleton<IHostDiagnosticSink>(diagnostics);
            using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            var captured = new ProbeDiagnostics(diagnostics);
            owners.Compose(catalog, host, builder, scopes, captured);
            var registry = host.GetRequiredService<PluginRegistry>();
            Assert.True(!diagnostics.Snapshot.Any(item => item.Severity >= HostDiagnosticSeverity.Error),
                string.Join(Environment.NewLine, captured.Drafts.Select(item => item.Code + ": " + item.Exception)));
            var expected = phase == 1 ? 0 : 1;
            Assert.Equal(expected, owners.AvailablePluginIds.Count);
            Assert.Equal(expected, registry.Documents.Count);
            Assert.Equal(expected, registry.Tools.Count);
            Assert.Equal(expected, registry.WorkbenchCommands.Count);
            Assert.Equal(expected, registry.MenuCommandContributions.Count);
            Assert.Equal(expected, registry.Icons.Count);
            Assert.Equal(expected, registry.WorkflowActions.Count);
            var lifecycle = host.GetRequiredService<PluginLifecycleCoordinator>();
            await lifecycle.InitializeAllAsync();
            host.GetRequiredService<WorkflowActionCatalogStore>().Commit(registry, host.GetRequiredService<PluginAvailabilityReadModel>());
            var runs = host.GetRequiredService<WorkflowActionRunManager>();
            Assert.Equal(expected, runs.GetAvailableActions().Count);
            if (phase == 1)
            {
                // 工作流定义只引用稳定 ID；提供方被禁用时复用原失败协议，不能偷偷启用插件。
                await using var run = runs.CreateRun(new PluginId("myavalonia.plugin.consumer"));
                var missing = await run.InvokeAsync(new WorkflowActionInvocationRequest(
                    new WorkflowActionId(PluginEnablementLoadingTests.Owner.Value + ".workflow.probe"),
                    JsonSerializer.SerializeToElement(new { })), null, CancellationToken.None);
                Assert.Equal("WORKFLOW_ACTION_NOT_FOUND", missing.Failure!.Code);
                Assert.False(initial.Settings!.IsEnabled(PluginEnablementLoadingTests.Owner));
            }
            var service = new PluginEnablementService(store, initial, discovery.Candidates.Select(item => item.Manifest.PluginId));
            if (phase < 2) Assert.True((await service.SetEnabledAsync(PluginEnablementLoadingTests.Owner, phase == 1)).Success);
            Assert.Equal(phase != 1, discovery.StartupSettings.Settings!.IsEnabled(PluginEnablementLoadingTests.Owner));
            Assert.Same(discovery, AssemblyLoaderHelper.Discover(Path.Combine(root, "Controls"), store.Load(), dataRoot));
            var stopped = await lifecycle.ShutdownAllAsync();
            Assert.Empty(stopped.Retentions);
            scopes.CloseAll();
            owners.Dispose();
            var phases = File.Exists(trace) ? File.ReadAllLines(trace) : [];
            Assert.Equal(phase == 1 ? 0 : 6, phases.Length);
            File.WriteAllText(Path.Combine(root, $"phase-{phase}.json"), JsonSerializer.Serialize(new
            { pid = Environment.ProcessId, loaded = discovery.Assemblies.Count == 1, trace = phases, contributionsPerKind = expected }));
        }
        finally { Environment.SetEnvironmentVariable("MYAVALONIA_ENABLEMENT_TEST_TRACE", previousTrace); }
    }

    private static void PrepareProbe(string path)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
        manifest["entryPoint"]!["type"] = "PluginIsolation.Plugin.EnablementProbeModule";
        File.WriteAllText(path, manifest.ToJsonString());
    }

    /// <summary>仅保留受控测试插件的组合异常，生产诊断仍由原会话脱敏；便于定位夹具错误。</summary>
    private sealed class ProbeDiagnostics(HostDiagnosticSession session) : IHostDiagnosticSink
    {
        internal List<HostDiagnosticDraft> Drafts { get; } = [];
        public HostDiagnosticRecord Report(HostDiagnosticDraft draft) { Drafts.Add(draft); return session.Report(draft); }
    }
}
