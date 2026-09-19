using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Restart;

/// <summary>无插件的接力入口。只等待已验证父进程并启动同一 Host，不承担更新或常驻监控。</summary>
internal static class RestartHelperRunner
{
    internal const string Switch = "--host-restart-helper-v1";
    internal static bool IsHelper(string[] args) => args.Any(value => value.StartsWith("--host-restart-helper", StringComparison.Ordinal));

    internal static async Task RunAsync(string[] args)
    {
        if (args.Length < 6 || args[0] != Switch || args[5] != "--" ||
            !Guid.TryParseExact(args[2], "N", out _) || args[1] != "myavalonia-restart-" + args[2] ||
            !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ||
            !long.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            throw new InvalidDataException("重启助手参数无效。");

        using var parent = Process.GetProcessById(pid);
        // 在 Ready 前取得并保留实际系统句柄，后续等待不会因 PID 被复用而指向另一个进程。
        _ = parent.Handle;
        if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != ticks || pid == Environment.ProcessId)
            throw new InvalidDataException("原 Host 身份无法确认。");
        var launch = HostLaunchSpecification.Capture(args[6..]);
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using (var timeout = new CancellationTokenSource(RestartProtocol.HandshakeTimeout))
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await pipe.WriteAsync(Encoding.ASCII.GetBytes(args[2]), timeout.Token).ConfigureAwait(false);
            if (await RestartProtocol.ReadAsync(pipe, timeout.Token).ConfigureAwait(false) != RestartProtocol.Ready)
                throw new InvalidDataException("原 Host 未确认会话。");
            await RestartProtocol.SendAsync(pipe, RestartProtocol.Ready, timeout.Token).ConfigureAwait(false);
        }

        using var exitTimeout = new CancellationTokenSource(RestartProtocol.ExitTimeout);
        await CompleteHandoffAsync(pipe, async token =>
        {
            await parent.WaitForExitAsync(token).ConfigureAwait(false);
            return parent.ExitCode;
        }, () =>
        {
            using var next = Process.Start(launch.CreateStartInfo()) ?? throw new InvalidOperationException("无法启动新的 Host。");
        }, exitTimeout.Token).ConfigureAwait(false);
    }

    /// <summary>协议只组合许可与退出事实，系统等待和启动副作用通过窄委托传入；便于验证超时而不真实等待 90 秒。</summary>
    internal static async Task CompleteHandoffAsync(Stream pipe, Func<CancellationToken, Task<int>> waitForExit,
        Action launch, CancellationToken cancellationToken)
    {
        byte result;
        try { result = await RestartProtocol.ReadAsync(pipe, cancellationToken).ConfigureAwait(false); }
        catch (EndOfStreamException) { return; } // 准备撤销或无许可退出，默认不启动。
        if (result != RestartProtocol.CleanExit && result != RestartProtocol.Failed)
            throw new InvalidDataException("重启退出结果无效。");
        await RestartProtocol.SendAsync(pipe, result, cancellationToken).ConfigureAwait(false);
        if (result == RestartProtocol.Failed) throw new InvalidOperationException("原 Host 未完成资源清理，自动重启已取消。");
        if (await waitForExit(cancellationToken).ConfigureAwait(false) != 0)
            throw new InvalidOperationException("原 Host 未正常退出，自动重启已取消。");
        cancellationToken.ThrowIfCancellationRequested();
        launch();
    }
}
