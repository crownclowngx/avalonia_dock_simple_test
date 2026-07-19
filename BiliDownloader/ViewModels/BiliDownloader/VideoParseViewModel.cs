using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 解析结果数据，通过回调传递给主 VM
/// </summary>
public class VideoParseResult
{
    public BiliVideoCollection Collection { get; set; } = null!;
    public List<BiliVideoItem> VideoItems { get; set; } = new();
    public List<BiliQualityOption> QualityOptions { get; set; } = new();
    public BiliQualityOption? SelectedQuality { get; set; }
    public List<BiliQualityOption> AudioQualityOptions { get; set; } = new();
    public BiliQualityOption? SelectedAudioQuality { get; set; }
    public bool IsMultiVideo { get; set; }
    public string TitlesText { get; set; } = "";
}

/// <summary>
/// URL 解析子 ViewModel：负责 URL 输入、解析按钮、解析状态
/// </summary>
public partial class VideoParseViewModel : ObservableObject
{
    private readonly BiliApiService _apiService = new();
    private readonly Action<VideoParseResult>? _onParsed;
    private readonly Func<bool> _isLoggedInCheck;

    [ObservableProperty]
    private string _url = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isParsed;

    [ObservableProperty]
    private string _downloadInfo = "";

    public BiliVideoCollection? VideoCollection { get; private set; }

    public IAsyncRelayCommand ParseCommand { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="onParsed">解析成功后的回调，将结果传回主 VM</param>
    /// <param name="isLoggedInCheck">检查当前是否已登录的函数</param>
    public VideoParseViewModel(Action<VideoParseResult>? onParsed, Func<bool> isLoggedInCheck)
    {
        _onParsed = onParsed;
        _isLoggedInCheck = isLoggedInCheck;
        ParseCommand = new AsyncRelayCommand(ParseAsync);
    }

    private async Task ParseAsync()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            DownloadInfo = "请输入有效的B站视频链接";
            return;
        }

        if (!_isLoggedInCheck())
        {
            DownloadInfo = "请先登录后再解析";
            return;
        }

        var parsed = BiliApiService.ParseVideoId(Url);
        var bangumi = parsed == null ? BiliApiService.ParseBangumiId(Url) : null;
        if (parsed == null && bangumi == null)
        {
            DownloadInfo = "无法解析链接，请输入有效的B站视频或番剧链接";
            return;
        }

        try
        {
            IsLoading = true;
            DownloadInfo = "正在解析视频信息...";

            var cookie = BiliLoginStateService.Instance.CookieHeader;

            // 获取视频集合（根据类型路由）
            BiliVideoCollection collection;
            if (parsed != null)
            {
                collection = await _apiService.GetVideoCollectionAsync(
                    parsed.Value.Id, parsed.Value.IsBvid, cookie);
            }
            else
            {
                collection = await _apiService.GetBangumiCollectionAsync(
                    bangumi!.Value.Id, bangumi.Value.IsSeasonId, cookie);
            }
            VideoCollection = collection;

            // 构建视频列表
            var videoItems = new List<BiliVideoItem>();
            int idx = 1;
            foreach (var item in collection.Items)
            {
                item.Index = idx++;
                item.OriginalTitle = item.Title;
                videoItems.Add(item);
            }

            // 生成重命名面板初始文本
            var titlesLines = string.Join(Environment.NewLine, collection.Items.Select(i => i.Title));

            // 获取可用清晰度和音频流
            var qualityOptions = new List<BiliQualityOption>();
            BiliQualityOption? selectedQuality = null;
            var audioQualityOptions = new List<BiliQualityOption>();
            BiliQualityOption? selectedAudioQuality = null;

            if (collection.Items.Count > 0)
            {
                var first = collection.Items[0];
                var dashResult = await _apiService.GetDashResultAsync(
                    first.Aid, first.Cid, 80, cookie,
                    first.MediaType, first.EpId, first.SeasonId);

                // 视频清晰度
                foreach (var q in dashResult.AcceptQualities)
                    qualityOptions.Add(q);
                selectedQuality = qualityOptions.FirstOrDefault();

                // 音频清晰度
                var audioGroups = dashResult.AudioStreams
                    .GroupBy(a => a.Id)
                    .Select(g => g.OrderByDescending(a => a.Bandwidth).First())
                    .OrderBy(a => a.Bandwidth)
                    .ToList();
                foreach (var a in audioGroups)
                {
                    audioQualityOptions.Add(new BiliQualityOption
                    {
                        QualityId = a.Id,
                        DisplayName = FormatAudioQualityName(a.Id, a.Bandwidth)
                    });
                }
                selectedAudioQuality = audioQualityOptions.LastOrDefault();
            }

            var isMultiVideo = collection.Items.Count > 1;

            IsParsed = true;
            DownloadInfo = $"解析成功: {collection.SeriesTitle} ({collection.Items.Count} 个视频)";

            // 通过回调传递结果
            _onParsed?.Invoke(new VideoParseResult
            {
                Collection = collection,
                VideoItems = videoItems,
                QualityOptions = qualityOptions,
                SelectedQuality = selectedQuality,
                AudioQualityOptions = audioQualityOptions,
                SelectedAudioQuality = selectedAudioQuality,
                IsMultiVideo = isMultiVideo,
                TitlesText = titlesLines,
            });
        }
        catch (Exception ex)
        {
            DownloadInfo = $"解析异常: {ex.Message}";
            IsParsed = false;
        }
        finally
        {
            IsLoading = false;
        }
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
            _ => $"{kbps}kbps (ID:{audioId})"
        };
    }
}
