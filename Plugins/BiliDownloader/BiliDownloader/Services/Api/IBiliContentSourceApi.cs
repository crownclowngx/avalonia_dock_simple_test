using BiliDownloader.Models;

namespace BiliDownloader.Services.Api;

/// <summary>
/// 内容源 Provider 所需的最小 B 站目录 API，隔离其与下载、字幕和弹幕能力。
/// </summary>
public interface IBiliContentSourceApi
{
    Task<string> ResolveShortLinkAsync(string shortLink, CancellationToken cancellationToken);

    Task<BiliVideoCollection> GetVideoCollectionAsync(
        string videoId,
        bool isBvid,
        string cookie,
        CancellationToken cancellationToken = default);

    Task<BiliVideoCollection> GetBangumiCollectionAsync(
        string id,
        bool isSeasonId,
        string cookie,
        CancellationToken cancellationToken = default);
}

/// <summary>解析界面探测可用音视频质量所需的最小 API。</summary>
public interface IBiliMediaProbe
{
    Task<BiliDashResult> GetDashResultAsync(
        long aid,
        long cid,
        int qualityId,
        string cookie,
        BiliMediaType mediaType = BiliMediaType.Video,
        long epId = 0,
        long seasonId = 0,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 字幕附加资源所需的最小平台边界。下载地址只在实现内部和运行时描述中流转，
/// 消费者不能通过该接口取得原始响应或 Cookie，从而避免持久化层依赖临时鉴权数据。
/// </summary>
public interface IBiliSubtitleApi
{
    Task<IReadOnlyList<SubtitleTrackDescriptor>> GetSubtitleTracksAsync(
        long aid, long cid, string cookie, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubtitleCue>> GetSubtitleCuesAsync(
        string subtitleUrl, string cookie, CancellationToken cancellationToken = default);
}

/// <summary>弹幕分段获取所需的最小平台边界；Protobuf 解码仍位于下载插件内部。</summary>
public interface IBiliDanmakuApi
{
    Task<byte[]> GetDanmakuSegmentAsync(
        long oid, int segmentIndex, long aid, string cookie,
        CancellationToken cancellationToken = default);
}
