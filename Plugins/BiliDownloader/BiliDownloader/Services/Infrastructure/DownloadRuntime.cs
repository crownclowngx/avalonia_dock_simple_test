namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// 下载速度计时与重试等待边界。
/// </summary>
public interface IDownloadRuntime
{
    DateTimeOffset UtcNow { get; }
    Task DelayForRetryAsync(int failedAttempt, CancellationToken cancellationToken);
}

public sealed class SystemDownloadRuntime : IDownloadRuntime
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayForRetryAsync(int failedAttempt, CancellationToken cancellationToken)
    {
        var exponent = Math.Clamp(failedAttempt, 0, 8);
        var delayMs = (int)Math.Pow(2, exponent) * 1000 + Random.Shared.Next(0, 500);
        return Task.Delay(delayMs, cancellationToken);
    }
}
