using System.Globalization;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 全局主媒体限速的应用服务。它负责设置持久化和运行时策略切换，UI 不直接操作令牌桶。
/// </summary>
public interface IGlobalBandwidthLimitService
{
    long CurrentBytesPerSecond { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(long bytesPerSecond, string reason, CancellationToken cancellationToken = default);
}

public sealed class GlobalBandwidthLimitService : IGlobalBandwidthLimitService
{
    public const string SettingKey = "global_media_rate_limit_bytes_per_second";

    private readonly ISettingsRepository _settings;
    private readonly IGlobalBandwidthLimitController _controller;
    private readonly IPluginLogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;
    private long _currentBytesPerSecond;

    public GlobalBandwidthLimitService(
        ISettingsRepository settings,
        IGlobalBandwidthLimitController controller,
        IPluginLogger? logger = null)
    {
        _settings = settings;
        _controller = controller;
        _log = logger ?? PluginLog.For<GlobalBandwidthLimitService>();
    }

    public long CurrentBytesPerSecond => Interlocked.Read(ref _currentBytesPerSecond);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            await _settings.InitAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await _settings.GetSettingAsync(SettingKey).ConfigureAwait(false);
            var value = ParsePersistedValue(raw);
            _controller.UpdateLimit(value, "plugin initialization");
            Interlocked.Exchange(ref _currentBytesPerSecond, value);
            _initialized = true;
            _log.Info($"全局主媒体限速初始化完成；持久化原值='{raw ?? "<missing>"}'，生效值={value} B/s。"
                + " 0 表示不限速；该策略只约束视频/音频网络读取，不包含 API 与附加资源请求。");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(
        long bytesPerSecond,
        string reason,
        CancellationToken cancellationToken = default)
    {
        BandwidthLimitPolicy.Validate(bytesPerSecond);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = CurrentBytesPerSecond;
            // 先落盘再切换运行时策略：持久化失败时，用户看到的设置和实际下载行为都保持旧值。
            await _settings.SetSettingAsync(
                SettingKey,
                bytesPerSecond.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            _controller.UpdateLimit(bytesPerSecond, reason);
            Interlocked.Exchange(ref _currentBytesPerSecond, bytesPerSecond);
            _initialized = true;
            _log.Info($"全局主媒体限速设置已提交；原因={reason}，旧值={previous} B/s，新值={bytesPerSecond} B/s。"
                + " 已持久化并热应用，活动任务无需重启且断点字节不变。");
        }
        finally
        {
            _gate.Release();
        }
    }

    private long ParsePersistedValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        if (long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            try
            {
                return BandwidthLimitPolicy.Validate(value);
            }
            catch (ArgumentOutOfRangeException)
            {
                // 下方统一记录兼容降级原因。
            }
        }

        _log.Warn($"全局主媒体限速设置无效，已安全降级为不限速；原值='{raw}'。"
            + $" 合法值为 0 或不小于 {BandwidthLimitPolicy.MinimumNonZeroBytesPerSecond} B/s 的整数。"
            + " 本次不会覆盖原值，便于诊断数据来源。");
        return 0;
    }
}
