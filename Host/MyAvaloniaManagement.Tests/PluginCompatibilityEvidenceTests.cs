using System.Text.Json;
using MyAvaloniaManagement.Business.Compatibility;
using MyAvaloniaManagement.Compatibility;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证证据语义与文件边界，尤其保护“未知不能变为绿色”的产品承诺。</summary>
public sealed class PluginCompatibilityEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MyAvaloniaV10Tests", Guid.NewGuid().ToString("N"));
    public PluginCompatibilityEvidenceTests() => Directory.CreateDirectory(_root);

    internal static HostCompatibilityIdentity Host()
    {
        var files = new[] { new ArtifactFile("Host.dll", 10, new string('A', 64)) };
        return new("3.0.0", "3.0.0+test", ArtifactFingerprint.Hash(files), RuntimeProfile.Current.Hash,
            "Test OS", "X64", ".NET 10.0", "3.4.1", "12.1.2", "12.1.0.6", files);
    }
    internal static PluginCompatibilityReport Report() => new(1, Guid.NewGuid(), DateTimeOffset.UtcNow,
        "myavalonia.plugin.test", "1.0.0", new string('B', 64), Host(),
        [new(CompatibilityLevel.Static, CompatibilityOutcome.Passed, "manifest", "清单通过"),
         new(CompatibilityLevel.Workspace, CompatibilityOutcome.NotRun, "workspace", "未选择")]);
    private static PluginArtifact Plugin() => new("myavalonia.plugin.test", "1.0.0", "[3.4.0,4.0.0)",
        "Test.dll", new(new string('B', 64), []), null, []);

    [Fact]
    public void 相同版本的不同文件不能复用通过结论()
    {
        var report = Report();
        Assert.Equal(EvidenceMatch.Matches, CompatibilityMatcher.Match(report, Plugin(), Host()));
        Assert.Equal(EvidenceMatch.DifferentArtifact, CompatibilityMatcher.Match(report,
            Plugin() with { Identity = new(new string('C', 64), []) }, Host()));
        Assert.Equal(EvidenceMatch.DifferentHost, CompatibilityMatcher.Match(report, Plugin(), Host() with { RuntimeHash = new string('C', 64) }));
        Assert.Equal(EvidenceMatch.DifferentRules, CompatibilityMatcher.Match(report, Plugin(), Host() with { RuleHash = new string('C', 64) }));
        Assert.Equal(EvidenceMatch.DifferentEnvironment, CompatibilityMatcher.Match(report, Plugin(), Host() with { Framework = ".NET 11" }));
        Assert.Equal(EvidenceMatch.Unknown, CompatibilityMatcher.Match(report, null, Host()));
        Assert.Equal(EvidenceMatch.Unknown, CompatibilityMatcher.Match(report, Plugin(), null));
        Assert.Contains("Workspace：未执行", CompatibilityMatcher.Summary(report));
    }

    [Fact]
    public void 报告往返保留分项并拒绝不合法输入()
    {
        var report = Report();
        var json = CompatibilityReportJson.Serialize(report);
        var read = CompatibilityReportJson.Parse(json);
        Assert.Equal(report.ReportId, read.ReportId);
        Assert.Equal(report.Checks, read.Checks);
        Assert.Throws<InvalidDataException>(() => CompatibilityReportJson.Parse(json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1")));
        Assert.Throws<InvalidDataException>(() => CompatibilityReportJson.Serialize(report with { SchemaVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => CompatibilityReportJson.Serialize(report with { Checks = [] }));
        Assert.Throws<InvalidDataException>(() => CompatibilityReportJson.Serialize(report with { Host = Host() with { RuntimeHash = new string('F', 64) } }));
        Assert.Throws<InvalidDataException>(() => CompatibilityReportJson.Serialize(report with { Checks = [report.Checks[0], report.Checks[0]] }));
        Assert.Throws<JsonException>(() => CompatibilityReportJson.Parse(json.Replace("\"Passed\"", "\"PretendPassed\"")));
    }

    [Fact]
    public async Task 指纹不依赖枚举顺序且修改任一资产即失效()
    {
        var first = Path.Combine(_root, "a.dll");
        var second = Path.Combine(_root, "b.native");
        await File.WriteAllTextAsync(first, "first");
        await File.WriteAllTextAsync(second, "second");
        var before = await ArtifactFingerprint.CaptureDirectoryAsync(_root);
        var reverse = await ArtifactFingerprint.CaptureFilesAsync([("b.native", second), ("a.dll", first)]);
        Assert.Equal(before.Sha256, reverse.Sha256);
        await File.WriteAllTextAsync(second, "changed");
        Assert.NotEqual(before.Sha256, (await ArtifactFingerprint.CaptureDirectoryAsync(_root)).Sha256);
        await Assert.ThrowsAsync<InvalidDataException>(() => ArtifactFingerprint.CaptureFilesAsync([("A.dll", first), ("a.dll", second)]));
        await Assert.ThrowsAsync<InvalidDataException>(() => ArtifactFingerprint.CaptureFilesAsync([("../a.dll", first)]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArtifactFingerprint.CaptureDirectoryAsync(_root, new CancellationToken(true)));
    }

    [Fact]
    public async Task 导入去重保留冲突与坏文件且取消不产生新报告()
    {
        var store = new CompatibilityReportStore(Path.Combine(_root, "reports"));
        var report = Report();
        var input = Path.Combine(_root, "input.json");
        await File.WriteAllTextAsync(input, CompatibilityReportJson.Serialize(report));
        await store.ImportAsync(input, default);
        await store.ImportAsync(input, default);
        Assert.Single((await store.ReadAsync(default)).Reports);
        await File.WriteAllTextAsync(input, CompatibilityReportJson.Serialize(report with { PluginVersion = "2.0.0" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(input, default));
        await File.WriteAllTextAsync(Path.Combine(store.DirectoryPath, "bad.json"), "broken");
        var snapshot = await store.ReadAsync(default);
        Assert.Single(snapshot.Reports);
        Assert.Equal(1, snapshot.Invalid);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ImportAsync(input, new CancellationToken(true)));
        Assert.Empty(Directory.GetFiles(store.DirectoryPath, "*.tmp"));
    }

    [Theory]
    [InlineData("native/win-x64/Dock.Avalonia.dll", true)]
    [InlineData("MyAvaloniaManagement.PluginSdk.dll", true)]
    [InlineData("Newtonsoft.Json.dll", true)]
    [InlineData("Microsoft.Extensions.Unprovided.dll", true)]
    [InlineData("MyAvaloniaManagement.Icons.dll", false)]
    [InlineData("Private.Business.dll", false)]
    public void 禁带规则不等于运行时根(string file, bool forbidden)
    {
        Assert.Equal(forbidden, RuntimeProfile.Current.IsForbiddenAsset(file));
        Assert.DoesNotContain("Newtonsoft.Json", RuntimeProfile.Current.SharedRoots);
        Assert.DoesNotContain("MyAvaloniaManagement.Icons", RuntimeProfile.Current.SharedRoots);
        Assert.True(RuntimeProfile.Current.IsForbiddenReference("Dock.Model"));
        Assert.False(RuntimeProfile.Current.IsForbiddenReference("MyAvaloniaManagement.PluginSdk"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
