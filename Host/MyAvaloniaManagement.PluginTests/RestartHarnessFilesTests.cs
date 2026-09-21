namespace MyAvaloniaManagement.PluginTests;

public sealed class RestartHarnessFilesTests
{
    [Fact]
    public void 实际构建副本保留运行资产与托管符号且排除原生调试符号()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "RestartHarness");
        Assert.True(File.Exists(Path.Combine(root, "MyAvaloniaManagement.RestartHarness.pdb")));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "runtimes"), "*.pdb", SearchOption.AllDirectories));
        Assert.DoesNotContain(Directory.GetFiles(root, "*", SearchOption.AllDirectories),
            file => Path.GetRelativePath(root, file).StartsWith("TestResults" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (OperatingSystem.IsWindows())
        {
            Assert.True(File.Exists(Path.Combine(root, "runtimes", "win-x64", "native", "libSkiaSharp.dll")));
            Assert.All(Directory.GetFiles(Path.Combine(root, "runtimes"), "*", SearchOption.AllDirectories), file =>
                Assert.Contains(Path.GetRelativePath(Path.Combine(root, "runtimes"), file).Split(Path.DirectorySeparatorChar)[0], new[] { "win", "win-x64" }));
        }
    }

    [Fact]
    public void 缺少入口在创建副本前失败()
    {
        var root = Path.Combine(Path.GetTempPath(), "restart-missing-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "copy");
        Assert.Throws<FileNotFoundException>(() => RestartHarnessFiles.Copy(root, target));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void 两个副本文件可独立修改且收据对应实际字节()
    {
        var root = Path.Combine(Path.GetTempPath(), "restart-copies-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(AppContext.BaseDirectory, "RestartHarness");
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            var receipt = RestartHarnessFiles.Copy(source, first);
            RestartHarnessFiles.Copy(source, second);
            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            Assert.Equal(files.Length, receipt.Files);
            Assert.Equal(files.Sum(file => new FileInfo(file).Length), receipt.Bytes);
            const string name = "MyAvaloniaManagement.RestartHarness.runtimeconfig.json";
            var original = File.ReadAllBytes(Path.Combine(source, name));
            File.WriteAllText(Path.Combine(first, name), "故障注入");
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(source, name)));
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(second, name)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
