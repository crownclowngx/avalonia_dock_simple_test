using MyAvaloniaManagement.Business.Compatibility;
using MyAvaloniaManagement.Business.PluginStatus;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Models.Plugins;
using MyAvaloniaManagement.ViewModels.PluginStatus;

namespace MyAvaloniaManagement.Tests;

/// <summary>关注用户能判断的事实和任务所有权，不断言 XAML 的内部排布。</summary>
public sealed class PluginDashboardTests
{
    private static PluginDashboardSnapshot Snapshot(params PluginCompatibilityReport[] reports) =>
        new(PluginCompatibilityEvidenceTests.Host(), [], reports.Select(report => new StoredCompatibilityReport(report, "测试输入")).ToArray(), 0,
            DateTimeOffset.UtcNow, "本次检查");

    [Fact]
    public void 矩阵按二进制及环境拆分并保留失败和未执行()
    {
        var first = PluginCompatibilityEvidenceTests.Report();
        var fail = first with { ReportId = Guid.NewGuid(), ExecutedAtUtc = first.ExecutedAtUtc.AddSeconds(1),
            Checks = [new(CompatibilityLevel.Static, CompatibilityOutcome.Failed, "manifest", "失败")] };
        var otherArtifact = first with { ReportId = Guid.NewGuid(), PluginHash = new string('C', 64) };
        var otherEnvironment = first with { ReportId = Guid.NewGuid(), Host = first.Host with { Framework = ".NET other" } };
        var snapshot = Snapshot(first, fail, otherArtifact, otherEnvironment);
        var matrix = CompatibilityDashboardProjection.CreateMatrix(snapshot, [first.PluginId, "myavalonia.plugin.empty"]);
        Assert.Equal(2, matrix.Columns.Count);
        Assert.Equal(3, matrix.Rows.Count);
        var failed = Assert.Single(matrix.Rows.SelectMany(row => row.Cells), cell => cell.Text.Contains("静态：失败"));
        Assert.Contains("历史结果不同", failed.Text);
        Assert.Contains("业务与真机：未执行", failed.Text);
        Assert.Contains("待检查", failed.Text);
        Assert.All(matrix.Rows.Single(row => row.PluginId.EndsWith("empty")).Cells, cell => Assert.Equal("无报告", cell.Text));
        Assert.Equal(16, CompatibilityDashboardProjection.Evidence(snapshot, first.PluginId).Count);
    }

    [Fact]
    public async Task 迟到结果不能覆盖新检查且关闭取消窗口任务()
    {
        var fake = new DelayedEvidence();
        using var model = new PluginStatusWindowViewModel(new Query(), TimeProvider.System, fake);
        model.Refresh();
        var first = model.LoadEvidenceAsync();
        var second = model.CheckArtifactsAsync();
        Assert.True(fake.Calls[0].Token.IsCancellationRequested);
        fake.Calls[1].Completion.SetResult(Snapshot() with { Notice = "新检查" });
        await second;
        fake.Calls[0].Completion.SetResult(Snapshot() with { Notice = "迟到检查" });
        await first;
        Assert.Equal("新检查", model.EvidenceNotice);
        var closing = model.CheckArtifactsAsync();
        model.Dispose();
        Assert.True(fake.Calls[2].Token.IsCancellationRequested);
        fake.Calls[2].Completion.SetResult(Snapshot() with { Notice = "窗口已关闭" });
        await closing;
        Assert.Equal("新检查", model.EvidenceNotice);
        Assert.False(model.CanInspect);
    }

    [Fact]
    public async Task 导入失败保留快照且任意报告正文不进入复制摘要()
    {
        var fake = new DelayedEvidence();
        using var model = new PluginStatusWindowViewModel(new Query(), TimeProvider.System, fake);
        model.Refresh();
        var report = PluginCompatibilityEvidenceTests.Report() with
        { Checks = [new(CompatibilityLevel.Static, CompatibilityOutcome.Passed, "check", "secret-user-path")] };
        var reading = model.CheckArtifactsAsync();
        fake.Calls[0].Completion.SetResult(Snapshot(report));
        await reading;
        Assert.Equal(4, model.EvidenceRows.Count);
        Assert.DoesNotContain("secret-user-path", model.CreateDiagnosticText());
        var before = model.EvidenceTimeText;
        await model.ImportReportAsync("bad.json");
        Assert.True(model.HasError);
        Assert.Equal(before, model.EvidenceTimeText);
        Assert.Equal(4, model.EvidenceRows.Count);
        Assert.False(model.IsInspecting);
    }

    [Fact]
    public async Task 文件查询不执行模块且文件变化不会替换会话身份()
    {
        var root = Path.Combine(Path.GetTempPath(), "DashboardEvidenceTests", Guid.NewGuid().ToString("N"));
        var plugin = Path.Combine(root, "plugin");
        Directory.CreateDirectory(plugin);
        try
        {
            var entry = Path.Combine(plugin, "Probe.dll");
            File.Copy(typeof(App).Assembly.Location, entry);
            File.WriteAllText(Path.Combine(plugin, "plugin.manifest.json"), """
                {"schemaVersion":2,"pluginId":"myavalonia.plugin.test","pluginVersion":"1.0.0",
                "entryPoint":{"assembly":"Probe.dll","type":"Never.Execute"},"sdk":{"minInclusive":"3.4.0","maxExclusive":"4.0.0"}}
                """);
            Assert.True(PluginManifestReader.TryRead(plugin, out var manifest, out _, out _));
            var registry = new PluginRegistry([], []);
            var evidence = new PluginDashboardEvidence(registry, new(Path.Combine(root, "reports")), TimeProvider.System);
            var initial = await evidence.ReadAsync(false, default);
            Assert.Null(initial.Host);
            Assert.Empty(initial.Artifacts);
            Assert.False(Directory.Exists(Path.Combine(root, "reports")));
            var loaded = new[] { (entry, typeof(App).Assembly.ManifestModule.ModuleVersionId) };
            var checkedSnapshot = await evidence.InspectAsync(manifest!.PluginId.Value, plugin, loaded, default);
            Assert.NotNull(checkedSnapshot.Artifact);
            File.WriteAllText(Path.Combine(plugin, "private.native"), "changed");
            var changed = await evidence.InspectAsync(manifest.PluginId.Value, plugin, loaded, default);
            Assert.Null(changed.Artifact);
            Assert.Contains("安装内容已变化", changed.State);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class Query : IPluginStatusQuery
    {
        public IReadOnlyList<PluginStatusItem> Capture() =>
            [new("myavalonia.plugin.test", "test", "可用", "0", "可用", "测试") { IsAvailable = true }];
    }
    private sealed class DelayedEvidence : IPluginDashboardEvidence
    {
        public List<(TaskCompletionSource<PluginDashboardSnapshot> Completion, CancellationToken Token)> Calls { get; } = [];
        public Task<PluginDashboardSnapshot> ReadAsync(bool inspectArtifacts, CancellationToken token)
        {
            var completion = new TaskCompletionSource<PluginDashboardSnapshot>();
            Calls.Add((completion, token));
            return completion.Task;
        }
        public Task ImportAsync(string path, CancellationToken token) => throw new InvalidDataException("错误报告");
    }
}
