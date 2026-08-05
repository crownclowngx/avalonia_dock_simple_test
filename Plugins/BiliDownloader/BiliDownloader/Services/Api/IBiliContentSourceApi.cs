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
