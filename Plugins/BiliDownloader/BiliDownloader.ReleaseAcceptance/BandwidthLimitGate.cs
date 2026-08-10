using System.Diagnostics;
using BiliDownloader.Services.Download;

namespace BiliDownloader.ReleaseAcceptance;

/// <summary>
/// P1-G10 的本机计时门禁。它不访问公网，只验证生产令牌桶在真实单调时钟上的吞吐、
/// 动态解除限制和取消响应；精确公平顺序由使用手动时钟的单元测试负责。
/// </summary>
internal sealed class BandwidthLimitGate : IReleaseGate
{
    public string Name => "p1-bandwidth-limit";

    public async Task<ReleaseGateResult> ExecuteAsync(
        ReleaseGateContext context,
        CancellationToken cancellationToken)
    {
        const long limit = 256 * 1024;
        using var limiter = new GlobalBandwidthLimiter(new SystemBandwidthClock());
        limiter.UpdateLimit(limit, "release acceptance");

        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 17; index++)
            await limiter.AcquireAsync(8192, $"task-{index % 2}", cancellationToken);
        stopwatch.Stop();

        // 令牌桶初始允许 8 KiB；剩余 128 KiB 在 256 KiB/s 下约需 0.5 秒。
        // 宽容上界只吸收共享 CI 的调度抖动，下界负责发现 limiter 被意外旁路。
        var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        var timingPassed = elapsedMs is >= 350 and <= 3000;

        var pending = limiter.AcquireAsync(8192, "hot-update", cancellationToken).AsTask();
        limiter.UpdateLimit(0, "release acceptance restore unlimited");
        await pending.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);

        using var cancelCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limiter.UpdateLimit(64 * 1024, "release acceptance cancellation");
        await limiter.AcquireAsync(8192, "cancel", cancellationToken);
        var cancelledWait = limiter.AcquireAsync(8192, "cancel", cancelCts.Token).AsTask();
        cancelCts.Cancel();
        var cancelled = false;
        try { await cancelledWait.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken); }
        catch (OperationCanceledException) { cancelled = true; }

        var metrics = new Dictionary<string, object?>
        {
            ["configuredBytesPerSecond"] = limit,
            ["measuredMilliseconds"] = Math.Round(elapsedMs, 2),
            ["measuredBytes"] = 17 * 8192,
            ["hotUpdateReleasedWaiter"] = pending.IsCompletedSuccessfully,
            ["cancellationObserved"] = cancelled,
        };
        return timingPassed && pending.IsCompletedSuccessfully && cancelled
            ? ReleaseGateResult.Pass(Name, "真实单调时钟限速、热更新与取消门禁通过。", metrics)
            : ReleaseGateResult.Fail(Name, "限速计时、热更新或取消未满足门限。", metrics);
    }
}
