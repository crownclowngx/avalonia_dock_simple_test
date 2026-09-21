using System.Security.Cryptography;
using System.Text.Json;

namespace MyAvaloniaManagement.Gate.Tests;

public sealed class AvaloniaLayoutPatchTests
{
    [Fact]
    public async Task 布局补丁失败阻断全部后续阶段()
    {
        var executed = new List<string>();
        var graph = GateExecutionPlan.ForProfile(GateProfile.Verify, id =>
        {
            executed.Add(id);
            return Task.FromException(new GateFailureException("布局补丁失败"));
        });
        await Assert.ThrowsAsync<GateFailureException>(() => graph.ExecuteAsync((_, action) => action()));
        Assert.Equal(["avalonia-layout-patch"], executed);
    }

    [Theory]
    [InlineData("matching")]
    [InlineData("missing-dll")]
    [InlineData("old-dll")]
    [InlineData("missing-receipt")]
    [InlineData("old-cache")]
    [InlineData("zero-tests")]
    [InlineData("failed-tests")]
    [InlineData("skipped-tests")]
    public void 固定摘要和通过收据共同约束实际运行文件(string mode)
    {
        using var temporary = new TemporaryDirectory();
        var metadata = Path.Combine(temporary.Path, "patches", "avalonia-cross-window-layout");
        var runtime = Path.Combine(temporary.Path, "artifacts", "avalonia-cross-window-layout", "runtime");
        Directory.CreateDirectory(metadata); Directory.CreateDirectory(runtime);
        byte[] bytes = [1, 2, 3];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        File.WriteAllText(Path.Combine(metadata, "baseline.json"), JsonSerializer.Serialize(new { identity = "layout.1", dllSha256 = hash, minimumTests = 7 }));
        if (mode != "missing-receipt")
            File.WriteAllText(Path.Combine(runtime, "receipt.json"), JsonSerializer.Serialize(new
            {
                identity = mode == "old-cache" ? "layout.0" : "layout.1", dllSha256 = hash,
                passed = mode == "zero-tests" ? 0 : 7, failed = mode == "failed-tests" ? 1 : 0,
                skipped = mode == "skipped-tests" ? 1 : 0,
            }));
        File.WriteAllBytes(Path.Combine(runtime, "Avalonia.Base.dll"), bytes);
        var actual = Path.Combine(temporary.Path, "Avalonia.Base.dll");
        if (mode != "missing-dll") File.WriteAllBytes(actual, mode == "old-dll" ? [3, 2, 1] : bytes);
        if (mode == "matching") AvaloniaLayoutPatchIdentity.Verify(temporary.Path, [actual]);
        else Assert.Throws<GateFailureException>(() => { AvaloniaLayoutPatchIdentity.Verify(temporary.Path, [actual]); });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("MAVG-layout-tests-");
        internal string Path => _directory.FullName;
        public void Dispose() => _directory.Delete(recursive: true);
    }
}
