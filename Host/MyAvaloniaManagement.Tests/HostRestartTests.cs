using System.Text;
using MyAvaloniaManagement.Business.Restart;
using MyAvaloniaManagement.Business.Plugins.Enablement;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证重启的许可、参数和设置提交边界；不启动用户 Host，也不使用固定延时推断完成。</summary>
public sealed class HostRestartTests
{
    [Fact]
    public async Task 重复请求只准备一次_取消恢复设置入口且下一次仍可关闭()
    {
        var handoff = new Handoff();
        var settings = new PluginEnablementOperationGate();
        var closes = 0;
        using var restart = new HostRestartCoordinator(handoff, settings, _ => { });
        restart.Attach(() => closes++, () => true);
        restart.Request(); restart.Request();
        Assert.Equal(1, closes);
        Assert.False(restart.CanRequest);
        Assert.True(await restart.BeginPreparationAsync());
        Assert.False(settings.TryEnter());
        Assert.True(await restart.PrepareHandoffAsync());
        restart.Cancel();
        Assert.True(restart.CanRequest);
        Assert.True(settings.TryEnter()); settings.Exit(true);
        Assert.False(handoff.Closed);
        restart.Request();
        Assert.True(await restart.BeginPreparationAsync());
        Assert.True(await restart.PrepareHandoffAsync());
        restart.WindowClosed(); restart.Cancel(); restart.Dispose();
        Assert.True(handoff.Closed);
        Assert.False(settings.TryEnter());
        Assert.Equal(2, handoff.Prepared);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 助手验证或准备失败不授予最终关闭且释放冻结(bool validation)
    {
        var handoff = new Handoff { FailValidation = validation, FailPrepare = !validation };
        var settings = new PluginEnablementOperationGate();
        var errors = new List<string>();
        using var restart = new HostRestartCoordinator(handoff, settings, errors.Add);
        restart.Attach(() => { }, () => true);
        restart.Request();
        if (!validation)
        {
            Assert.True(await restart.BeginPreparationAsync());
            Assert.False(await restart.PrepareHandoffAsync());
        }
        Assert.False(restart.IsRequested);
        Assert.False(handoff.Closed);
        Assert.Single(errors);
        Assert.True(settings.TryEnter()); settings.Exit(true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 冻结等待所有已接受操作且失败不能解释为保存完成(bool success)
    {
        var settings = new PluginEnablementOperationGate();
        Assert.True(settings.TryEnter()); Assert.True(settings.TryEnter());
        var pause = settings.PauseForRestartAsync(CancellationToken.None);
        Assert.False(pause.IsCompleted);
        Assert.False(settings.TryEnter());
        settings.Exit(success);
        Assert.False(pause.IsCompleted);
        settings.Exit(true);
        if (success)
        {
            using var lease = await pause;
            Assert.False(settings.TryEnter());
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(() => pause);
        Assert.True(settings.TryEnter()); settings.Exit(true);
    }

    [Fact]
    public async Task 冻结取消不取消已经接受的提交且双重冻结被拒绝()
    {
        var settings = new PluginEnablementOperationGate();
        settings.TryEnter();
        using var cancellation = new CancellationTokenSource();
        var pause = settings.PauseForRestartAsync(cancellation.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.PauseForRestartAsync(CancellationToken.None));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pause);
        settings.Exit(true);
        using var lease = await settings.PauseForRestartAsync(CancellationToken.None);
        Assert.False(settings.TryEnter());
    }

    [Fact]
    public async Task 普通关闭不创建助手且准备取消后不残留请求()
    {
        var handoff = new Handoff();
        using var restart = new HostRestartCoordinator(handoff, null, _ => { });
        restart.Attach(() => { }, () => true);
        Assert.True(await restart.BeginPreparationAsync());
        Assert.True(await restart.PrepareHandoffAsync());
        restart.WindowClosed();
        Assert.Equal(0, handoff.Prepared);
        Assert.False(handoff.Closed);
        Assert.False(restart.CanRequest);
    }

    [Fact]
    public async Task 设置提交失败让请求可重试而不是丢失已保存意图()
    {
        var settings = new PluginEnablementOperationGate();
        settings.TryEnter();
        var messages = new List<string>();
        using var restart = new HostRestartCoordinator(new Handoff(), settings, messages.Add);
        restart.Attach(() => { }, () => true);
        restart.Request();
        var prepare = restart.BeginPreparationAsync();
        settings.Exit(false);
        Assert.False(await prepare);
        Assert.False(restart.IsRequested);
        Assert.Contains("设置", Assert.Single(messages));
        Assert.True(restart.CanRequest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 启动参数逐项保留_助手参数不污染普通启动(bool dotnet)
    {
        var directory = Path.Combine(Path.GetTempPath(), "HostRestart 中文 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var exe = Path.Combine(directory, dotnet ? "dotnet.exe" : "Host.exe");
            var dll = Path.Combine(directory, "Host.dll");
            File.WriteAllText(exe, "fixture"); File.WriteAllText(dll, "fixture");
            var args = new[] { "含 空格", "引号\"和&$(data)", "--user-setting" };
            var spec = new HostLaunchSpecification(exe, dotnet ? dll : null, args, directory, directory);
            args[0] = "mutated";
            var normal = spec.CreateStartInfo();
            Assert.Equal(dotnet ? new[] { dll, "含 空格", "引号\"和&$(data)", "--user-setting" } :
                new[] { "含 空格", "引号\"和&$(data)", "--user-setting" }, normal.ArgumentList);
            Assert.False(normal.UseShellExecute); Assert.True(normal.CreateNoWindow);
            Assert.Equal(directory, normal.Environment["MYAVALONIA_DATA_DIRECTORY"]);
            Assert.Contains(RestartHelperRunner.Switch, spec.CreateStartInfo([RestartHelperRunner.Switch]).ArgumentList);
            Assert.DoesNotContain(RestartHelperRunner.Switch, spec.CreateStartInfo().ArgumentList);
            Assert.Throws<InvalidOperationException>(() => new HostLaunchSpecification(exe, null,
                [RestartHelperRunner.Switch], directory, directory));
            Assert.Throws<InvalidOperationException>(() => new HostLaunchSpecification(exe + ".missing", null, [], directory, directory));
            Assert.Throws<InvalidOperationException>(() => new HostLaunchSpecification(exe, dll + ".missing", [], directory, directory));
            Assert.Throws<InvalidOperationException>(() => new HostLaunchSpecification(exe, null, [], directory + ".missing", directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(32)]
    public async Task 截断或不匹配身份不能完成握手(int length)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(new string('a', length)));
        if (length == 32)
            await Assert.ThrowsAsync<InvalidDataException>(() => RestartProtocol.AuthenticateAsync(stream, new string('b', 32), CancellationToken.None));
        else
            await Assert.ThrowsAsync<EndOfStreamException>(() => RestartProtocol.AuthenticateAsync(stream, new string('b', 32), CancellationToken.None));
    }

    [Fact]
    public async Task 握手身份通过后只读取一个固定字节()
    {
        using var stream = new MemoryStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(new string('a', 32)));
        await RestartProtocol.SendAsync(stream, RestartProtocol.CleanExit, CancellationToken.None);
        stream.Position = 0;
        await RestartProtocol.AuthenticateAsync(stream, new string('a', 32), CancellationToken.None);
        Assert.Equal(RestartProtocol.CleanExit, await RestartProtocol.ReadAsync(stream, CancellationToken.None));
        await Assert.ThrowsAsync<EndOfStreamException>(() => RestartProtocol.ReadAsync(stream, CancellationToken.None));
    }

    private sealed class Handoff : IHostRestartHandoff
    {
        public bool FailValidation, FailPrepare, Closed;
        public int Prepared;
        public void Validate() { if (FailValidation) throw new InvalidOperationException(); }
        public Task PrepareAsync() { Prepared++; return FailPrepare ? Task.FromException(new IOException()) : Task.CompletedTask; }
        public void ConfirmWindowClosed() => Closed = true;
        public void Abort() => Closed = false;
    }
}
