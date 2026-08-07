using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.ViewModels.BiliDownloader;

namespace BiliDownloader.Services.ContentSources;

/// <summary>
/// 将一个或多个来源解析结果合并为现有下载配置可消费的模型。
/// 设计意图：链接解析和个人来源浏览汇入同一出口，后续提交链路无需识别来源类型。
/// </summary>
public sealed class VideoParseResultFactory
{
    private readonly IBiliMediaProbe _mediaProbe;
    private readonly IBiliCredentialProvider _credentials;

    public VideoParseResultFactory(IBiliMediaProbe mediaProbe, IBiliCredentialProvider credentials)
    {
        _mediaProbe = mediaProbe;
        _credentials = credentials;
    }

    public async Task<VideoParseResult> CreateAsync(
        IReadOnlyList<BiliVideoCollection> collections,
        string aggregateTitle,
        CancellationToken cancellationToken)
    {
        if (collections.Count == 0)
            throw new ContentSourceException(ContentSourceErrorCode.InvalidInput, "请至少选择一个内容项。");

        var seen = new HashSet<MediaUnitKey>();
        var items = collections.SelectMany(collection => collection.Items)
            .Where(item => item.Aid > 0 && item.Cid > 0)
            .Where(item => seen.Add(item.MediaUnitKey ?? new MediaUnitKey(item.Aid, item.Cid)))
            .ToList();
        if (items.Count == 0)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "所选内容没有可下载的视频单元。");

        for (var index = 0; index < items.Count; index++) items[index].Index = index + 1;
        var merged = new BiliVideoCollection
        {
            SeriesTitle = collections.Count == 1 ? collections[0].SeriesTitle : aggregateTitle,
            Cover = collections.FirstOrDefault(collection => !string.IsNullOrWhiteSpace(collection.Cover))?.Cover ?? "",
            UpName = collections.Select(collection => collection.UpName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "",
            PublishDate = collections.Select(collection => collection.PublishDate).FirstOrDefault(date => date.HasValue),
            Items = items,
        };

        var first = items[0];
        var dash = await _mediaProbe.GetDashResultAsync(
            first.Aid, first.Cid, 80, _credentials.GetCookieHeader(),
            first.MediaType, first.EpId, first.SeasonId, cancellationToken);
        var qualities = dash.AcceptQualities.ToList();
        var audio = dash.AudioStreams.GroupBy(stream => stream.Id)
            .Select(group => group.OrderByDescending(stream => stream.Bandwidth).First())
            .OrderBy(stream => stream.Bandwidth)
            .Select(stream => new BiliQualityOption
            {
                QualityId = stream.Id,
                DisplayName = FormatAudioQualityName(stream.Id, stream.Bandwidth),
            }).ToList();

        return new VideoParseResult
        {
            Collection = merged,
            VideoItems = items,
            QualityOptions = qualities,
            SelectedQuality = qualities.FirstOrDefault(),
            AudioQualityOptions = audio,
            SelectedAudioQuality = audio.LastOrDefault(),
            IsMultiVideo = items.Count > 1,
            TitlesText = string.Join(Environment.NewLine, items.Select(item => item.Title)),
        };
    }

    private static string FormatAudioQualityName(int audioId, long bandwidth)
    {
        var kbps = bandwidth / 1000;
        return audioId switch
        {
            30216 => $"{kbps}kbps (标准)",
            30232 => $"{kbps}kbps (高品质)",
            30280 => $"{kbps}kbps (无损)",
            _ when audioId >= 30250 => $"{kbps}kbps (Hi-Res)",
            _ => $"{kbps}kbps (ID:{audioId})",
        };
    }
}
