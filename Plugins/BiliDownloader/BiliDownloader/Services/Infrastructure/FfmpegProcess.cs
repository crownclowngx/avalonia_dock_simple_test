using System.Diagnostics;

namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// 可替换的外部进程边界，供 ffmpeg 参数、取消和清理逻辑离线测试。
/// </summary>
public interface IFfmpegProcessFactory
{
    IFfmpegProcess Start(ProcessStartInfo startInfo);
}

public interface IFfmpegProcess : IDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
    Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken);
    Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken);
    void Kill(bool entireProcessTree);
}

public sealed class FfmpegProcessFactory : IFfmpegProcessFactory
{
    public IFfmpegProcess Start(ProcessStartInfo startInfo)
        => new SystemFfmpegProcess(
            Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 ffmpeg 进程"));

    private sealed class SystemFfmpegProcess : IFfmpegProcess
    {
        private readonly Process _process;

        public SystemFfmpegProcess(Process process) => _process = process;

        public bool HasExited => _process.HasExited;
        public int ExitCode => _process.ExitCode;

        public Task WaitForExitAsync(CancellationToken cancellationToken)
            => _process.WaitForExitAsync(cancellationToken);

        public Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken)
            => _process.StandardOutput.ReadToEndAsync(cancellationToken);

        public Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken)
            => _process.StandardError.ReadToEndAsync(cancellationToken);

        public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);

        public void Dispose() => _process.Dispose();
    }
}
