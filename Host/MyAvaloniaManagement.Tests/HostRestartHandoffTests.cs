using MyAvaloniaManagement.Business.Restart;

namespace MyAvaloniaManagement.Tests;

/// <summary>退出事实与副作用分离后，确定性验证错误、超时和有限启动，不依赖固定延时。</summary>
public sealed class HostRestartHandoffTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(99)]
    public async Task 缺少最终成功许可不等待或启动(int message)
    {
        using var stream = new MemoryStream();
        if (message >= 0) stream.WriteByte((byte)message);
        stream.Position = 0;
        var waited = false; var started = false;
        var completion = RestartHelperRunner.CompleteHandoffAsync(stream,
            _ => { waited = true; return Task.FromResult(0); }, () => started = true, CancellationToken.None);
        if (message < 0) await completion;
        else if (message == 3) await Assert.ThrowsAsync<InvalidOperationException>(() => completion);
        else await Assert.ThrowsAsync<InvalidDataException>(() => completion);
        Assert.False(waited); Assert.False(started);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 许可后必须等到成功退出才启动一次(int exitCode)
    {
        using var stream = new MemoryStream(); stream.WriteByte(RestartProtocol.CleanExit); stream.Position = 0;
        var parent = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launches = 0;
        var completion = RestartHelperRunner.CompleteHandoffAsync(stream, _ => parent.Task,
            () => launches++, CancellationToken.None);
        Assert.Equal(0, launches); Assert.False(completion.IsCompleted);
        Assert.Equal(new byte[] { 2, 2 }, stream.ToArray()); // 先确认信号，不等待新 Host 创建来结束旧进程。
        parent.SetResult(exitCode);
        if (exitCode == 0) { await completion; Assert.Equal(1, launches); }
        else { await Assert.ThrowsAsync<InvalidOperationException>(() => completion); Assert.Equal(0, launches); }
    }

    [Fact]
    public async Task 父进程等待取消不启动也不强杀()
    {
        using var stream = new MemoryStream(); stream.WriteByte(RestartProtocol.CleanExit); stream.Position = 0;
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = false;
        var completion = RestartHelperRunner.CompleteHandoffAsync(stream, async token =>
        { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return 0; }, () => started = true, cancel.Token);
        await entered.Task; cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
        Assert.False(started);
    }

    [Fact]
    public async Task 新进程创建失败不重试()
    {
        using var stream = new MemoryStream(); stream.WriteByte(RestartProtocol.CleanExit); stream.Position = 0;
        var attempts = 0;
        await Assert.ThrowsAsync<IOException>(() => RestartHelperRunner.CompleteHandoffAsync(stream,
            _ => Task.FromResult(0), () => { attempts++; throw new IOException("fixture"); }, CancellationToken.None));
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData("--host-restart-helper-v2")]
    [InlineData("--host-restart-helper-v1")]
    public async Task 不完整或未知助手命令不能落回普通Host(string flag)
    {
        Assert.True(RestartHelperRunner.IsHelper([flag]));
        await Assert.ThrowsAsync<InvalidDataException>(() => RestartHelperRunner.RunAsync([flag]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 父进程身份不符或指向自身时在连接前拒绝(long tickOffset)
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var token = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<InvalidDataException>(() => RestartHelperRunner.RunAsync([
            RestartHelperRunner.Switch, "myavalonia-restart-" + token, token,
            current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (current.StartTime.ToUniversalTime().Ticks + tickOffset).ToString(System.Globalization.CultureInfo.InvariantCulture), "--"]));
    }

    [Fact]
    public async Task 重复最终许可不会造成二次启动()
    {
        using var stream = new MemoryStream();
        stream.Write([RestartProtocol.CleanExit, RestartProtocol.CleanExit]); stream.Position = 0;
        var launches = 0;
        await RestartHelperRunner.CompleteHandoffAsync(stream, _ => Task.FromResult(0),
            () => launches++, CancellationToken.None);
        Assert.Equal(1, launches); // 单次会话只消费一份结果，没有持续读取并再次启动的循环。
    }
}
