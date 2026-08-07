using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 单项媒体预检分析边界。实现必须只返回不含鉴权信息的选择结果；DASH URL 只能停留在方法内部。
/// </summary>
public interface IMediaPreflightAnalyzer
{
    /// <summary>
    /// 对单个提交项完成一次 DASH 探测、流选择和空间估算。返回值不得携带签名 URL、Cookie
    /// 或请求头，以便 Coordinator 可以安全地构造预检指纹与任务快照。
    /// </summary>
    Task<MediaPreflightResult> AnalyzeAsync(
        DownloadSubmissionItem item,
        DownloadProfileSnapshot profile,
        CancellationToken cancellationToken);
}

/// <summary>根据已选择流和时长计算峰值空间，不负责网络访问或流选择。</summary>
public interface IMediaSizeCalculator
{
    /// <summary>
    /// 计算下载临时流与发布 staging 同时存在时的峰值字节数；信息不足时返回 null，
    /// 由上层要求用户确认，不能把未知空间伪装为零。
    /// </summary>
    long? EstimatePeakBytes(MediaOutputPlan plan, int durationSeconds);
}

/// <summary>
/// 下载临时流与同卷 staging 在发布前会同时存在，因此按有效流总码率的两倍再加 10% 余量估算。
/// 未知时长或带宽返回 null，交由预检转换成需确认警告，不能伪造为零空间需求。
/// </summary>
public sealed class MediaSizeCalculator : IMediaSizeCalculator
{
    /// <inheritdoc />
    public long? EstimatePeakBytes(MediaOutputPlan plan, int durationSeconds)
    {
        if (durationSeconds <= 0) return null;
        var bandwidth = checked(plan.VideoBandwidth + plan.AudioBandwidth);
        if (bandwidth <= 0) return null;
        var streamBytes = checked(bandwidth * (long)durationSeconds / 8L);
        return checked(streamBytes * 22L / 10L);
    }
}

/// <summary>
/// 生产媒体预检分析器。一次调用只请求一次 DASH，并立即丢弃选择结果中的临时 URL；
/// 返回给调用方的 OutputPlan 只含编码、带宽、模式和扩展名等稳定公开元数据。
/// </summary>
public sealed class DashMediaPreflightAnalyzer(
    IBiliMediaProbe mediaProbe,
    IBiliCredentialProvider credentials,
    IMediaStreamSelectionPolicy selectionPolicy,
    IMediaSizeCalculator sizeCalculator) : IMediaPreflightAnalyzer
{
    /// <inheritdoc />
    public async Task<MediaPreflightResult> AnalyzeAsync(
        DownloadSubmissionItem item,
        DownloadProfileSnapshot profile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dash = await mediaProbe.GetDashResultAsync(
            item.Aid,
            item.Cid,
            profile.VideoQualityId,
            credentials.GetCookieHeader(),
            item.MediaType,
            item.EpId,
            item.SeasonId,
            cancellationToken);
        var selection = selectionPolicy.Select(dash, new MediaSelectionRequest(
            profile.VideoQualityId,
            profile.AudioQualityId,
            profile.VideoCodecPreference,
            profile.OutputContainer,
            profile.OutputMediaMode));
        return new MediaPreflightResult(
            selection,
            selection.OutputPlan is null ? null : sizeCalculator.EstimatePeakBytes(selection.OutputPlan, item.Duration));
    }
}
