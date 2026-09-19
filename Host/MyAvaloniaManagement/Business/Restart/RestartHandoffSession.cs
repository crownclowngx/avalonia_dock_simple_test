using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Restart;

/// <summary>UI 只负责准备或撤销交接；最终许可由 Program 在所有清理结束后发送。</summary>
internal interface IHostRestartHandoff
{
    void Validate();
    Task PrepareAsync();
    void ConfirmWindowClosed();
    void Abort();
}

/// <summary>进程入口拥有的单次重启交接，寿命长于 DI 容器；不持有任何插件或窗口。</summary>
/// <remarks>管道断开默认撤销。助手必须同时收到最终许可并观察旧进程成功退出，不能仅依赖 PID。</remarks>
internal sealed class RestartHandoffSession(Func<HostLaunchSpecification> capture) : IHostRestartHandoff, IDisposable
{
    private HostLaunchSpecification? _launch;
    private NamedPipeServerStream? _pipe;
    private Process? _helper;
    private bool _closed;

    public void Validate() => _launch = capture();

    public async Task PrepareAsync()
    {
        if (_pipe is not null) throw new InvalidOperationException("重启交接已经存在。");
        var launch = _launch ?? throw new InvalidOperationException("尚未验证启动信息。");
        var identity = Guid.NewGuid().ToString("N");
        var pipeName = "myavalonia-restart-" + identity;
        using var current = Process.GetCurrentProcess();
        _pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            _helper = Process.Start(launch.CreateStartInfo([RestartHelperRunner.Switch, pipeName, identity,
                current.Id.ToString(CultureInfo.InvariantCulture),
                current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture), "--"]))
                ?? throw new InvalidOperationException("无法创建重启助手。");
            using var timeout = new CancellationTokenSource(RestartProtocol.HandshakeTimeout);
            await _pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            await RestartProtocol.AuthenticateAsync(_pipe, identity, timeout.Token).ConfigureAwait(false);
            await RestartProtocol.SendAsync(_pipe, RestartProtocol.Ready, timeout.Token).ConfigureAwait(false);
            if (await RestartProtocol.ReadAsync(_pipe, timeout.Token).ConfigureAwait(false) != RestartProtocol.Ready)
                throw new InvalidOperationException("重启助手未确认就绪。");
        }
        catch { Abort(); throw; }
    }

    public void ConfirmWindowClosed() => _closed = _pipe is not null;

    /// <summary>只在正常退出、Runtime 和诊断清理全部成功后调用；失败信号让助手承担独立错误反馈。</summary>
    internal async Task CompleteAsync(bool success)
    {
        if (_pipe is null || !_closed) { Abort(); return; }
        try
        {
            using var timeout = new CancellationTokenSource(RestartProtocol.HandshakeTimeout);
            var result = success ? RestartProtocol.CleanExit : RestartProtocol.Failed;
            await RestartProtocol.SendAsync(_pipe, result, timeout.Token).ConfigureAwait(false);
            if (await RestartProtocol.ReadAsync(_pipe, timeout.Token).ConfigureAwait(false) != result)
                throw new InvalidOperationException("重启交接未确认。");
        }
        finally { Abort(); }
    }

    /// <summary>关闭管道即撤销；不杀进程。助手有连接/读取时限，失去所属会话会自行结束。</summary>
    public void Abort()
    {
        _closed = false;
        _pipe?.Dispose();
        _pipe = null;
        _helper?.Dispose();
        _helper = null;
        _launch = null;
    }

    public void Dispose() => Abort();
}
