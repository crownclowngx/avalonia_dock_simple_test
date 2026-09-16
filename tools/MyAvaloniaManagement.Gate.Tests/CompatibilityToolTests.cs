using MyAvaloniaManagement.CompatibilityTool;

namespace MyAvaloniaManagement.Gate.Tests;

public sealed class CompatibilityToolTests
{
    [Fact]
    public void 参数拒绝隐式业务范围及重复输入()
    {
        Assert.Throws<ArgumentException>(() => CompatibilityOptions.Parse(["--input", "."]));
        Assert.Throws<ArgumentException>(() => CompatibilityOptions.Parse(["--input", ".", "--input", ".."]));
        Assert.Throws<ArgumentException>(() => CompatibilityOptions.Parse(["--workspace", "p"]));
        var result = CompatibilityOptions.Parse(["--input", ".", "--host", ".", "--output", "out"]);
        Assert.Null(result.UiTests);
        Assert.Empty(result.WorkspaceIds);
        Assert.Equal(120, result.TimeoutSeconds);
    }

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(1, 0, 0, false)]
    [InlineData(2, 2, 0, false)]
    [InlineData(1, 1, 0, true)]
    [InlineData(1, 0, 1, true)]
    public void TRX必须恰好执行一个用例(int total, int passed, int failed, bool valid)
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, $"<TestRun><ResultSummary><Counters total='{total}' passed='{passed}' failed='{failed}'/></ResultSummary></TestRun>");
            if (valid) Assert.Equal((passed, failed), CompatibilityProcess.ReadTrx(file));
            else Assert.Throws<InvalidDataException>(() => CompatibilityProcess.ReadTrx(file));
        }
        finally { File.Delete(file); }
    }
}
