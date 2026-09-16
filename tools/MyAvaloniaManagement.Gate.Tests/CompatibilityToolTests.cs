using MyAvaloniaManagement.CompatibilityTool;

namespace MyAvaloniaManagement.Gate.Tests;

public sealed class CompatibilityToolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 取消和超时终止子进程并保留日志(bool cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), "CompatibilityProcessTests", Guid.NewGuid().ToString("N"));
        var info = new System.Diagnostics.ProcessStartInfo("pwsh")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 30" }) info.ArgumentList.Add(arg);
        using var token = new CancellationTokenSource();
        if (cancel) token.CancelAfter(500);
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CompatibilityProcess.ExecuteAsync(info, directory,
                TimeSpan.FromMilliseconds(cancel ? 10000 : 500), token.Token));
            Assert.True(File.Exists(Path.Combine(directory, "process.log")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task 进程非零退出不被转换为成功()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CompatibilityProcessTests", Guid.NewGuid().ToString("N"));
        var info = new System.Diagnostics.ProcessStartInfo("dotnet")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("--invalid-compatibility-test-option");
        try
        {
            Assert.NotEqual(0, await CompatibilityProcess.ExecuteAsync(info, directory, TimeSpan.FromSeconds(20), default));
            Assert.NotEmpty(await File.ReadAllTextAsync(Path.Combine(directory, "process.log")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

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
