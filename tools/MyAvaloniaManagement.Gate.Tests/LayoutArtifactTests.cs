namespace MyAvaloniaManagement.Gate.Tests;

/// <summary>只操作隔离产物目录，直接调用真实发布入口复用的检查；不启动发布或桌面进程。</summary>
public sealed class LayoutArtifactTests
{
    private const string CurrentLayout = """
        {"schemaVersion":3,"mainWindow":{"id":"main","bounds":{"x":80,"y":80,"width":1000,"height":700,"maximized":false,"screen":null},
        "root":{"kind":"documents","id":"Documents","proportion":1,"orientation":null,"children":[],"toolIds":[],"activeToolId":null}},
        "floatingWindows":[],"tools":[]}
        """;

    [Fact]
    public void 当前V3产物通过且文件不会被修改()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "layout-v3.json");
        File.WriteAllText(path, CurrentLayout);
        GateChecks.AssertWindowsSmokeLayoutArtifact(directory.Path);
        Assert.Equal(CurrentLayout, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("layout-v1.json")]
    [InlineData("layout-v2.json")]
    public void 缺少当前产物时不能以旧文件代替(string? oldName)
    {
        using var directory = new TemporaryDirectory();
        if (oldName is not null) File.WriteAllText(Path.Combine(directory.Path, oldName), "{\"schemaVersion\":2}");
        var failure = Assert.Throws<GateFailureException>(() => GateChecks.AssertWindowsSmokeLayoutArtifact(directory.Path));
        Assert.Contains("未生成 layout-v3.json", failure.Message);
    }

    [Theory]
    [InlineData("{broken:private-input}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":4}")]
    [InlineData("{\"schemaVersion\":\"3\"}")]
    [InlineData("{\"schemaVersion\":3.5}")]
    [InlineData("{\"schemaVersion\":2147483648}")]
    public void 损坏内容和错误版本均明确拒绝(string json)
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "layout-v3.json"), json);
        var failure = Assert.Throws<GateFailureException>(() => GateChecks.AssertWindowsSmokeLayoutArtifact(directory.Path));
        Assert.Contains("layout-v3.json", failure.Message);
        Assert.DoesNotContain("private-input", failure.Message);
    }

    [Theory]
    [InlineData("layout-v1.json")]
    [InlineData("layout-v2.json")]
    public void 新建Smoke目录不能同时产生已退役文件(string oldName)
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "layout-v3.json"), CurrentLayout);
        File.WriteAllText(Path.Combine(directory.Path, oldName), "legacy");
        Assert.Throws<GateFailureException>(() => GateChecks.AssertWindowsSmokeLayoutArtifact(directory.Path));
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(directory.Path, oldName)));
    }

    /// <summary>只拥有本次测试创建的临时目录，清理目标不接受外部路径。</summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("MAVG-layout-artifact-tests-");
        internal string Path => _directory.FullName;
        public void Dispose() => _directory.Delete(recursive: true);
    }
}
