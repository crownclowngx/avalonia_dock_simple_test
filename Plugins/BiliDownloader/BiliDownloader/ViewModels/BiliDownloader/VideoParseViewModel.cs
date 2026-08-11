using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.Services.Download;

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
public partial class VideoParseViewModel : ObservableObject, IDisposable
{
    private readonly IContentSourceProviderRegistry _providerRegistry;
    private readonly IBiliMediaProbe _mediaProbe;
    private readonly IBiliCredentialProvider _credentialProvider;
    private readonly Action<VideoParseResult>? _onParsed;
    private bool _restoringSource;
    // URL 解析、媒体能力探测和字幕发现都产生只服务于当前页面的临时结果，必须绑定 Document。
    // 父级令牌负责宿主关闭，本地 CTS 负责子对象独立释放；成功回调前还会检查关闭状态，防止
    // 已经完成底层网络调用的迟到结果重新填充 Workspace。
    private readonly CancellationToken _documentToken;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    /// <summary>
    /// 当前已规范化的直接链接来源。仅在成功解析或离线恢复时存在；
    /// 用户重新编辑输入后立即清空，避免把旧来源身份保存到新的 URL 旁边。
    /// </summary>
    public ContentSourceDescriptor? CurrentSourceDescriptor { get; private set; }

    /// <summary>规范化来源身份变化事件，不代表已经访问远端或创建下载任务。</summary>
    public event Action? PersistentSourceChanged;

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
    public VideoParseViewModel(
        IContentSourceProviderRegistry providerRegistry,
        IBiliMediaProbe mediaProbe,
        IBiliCredentialProvider credentialProvider,
        Action<VideoParseResult>? onParsed,
        Func<bool> isLoggedInCheck,
        CancellationToken documentToken = default)
    {
        _providerRegistry = providerRegistry;
        _mediaProbe = mediaProbe;
        _credentialProvider = credentialProvider;
        _onParsed = onParsed;
        _documentToken = documentToken;
        // 保留该参数以兼容已有调用方；鉴权现在以远端实际响应为准，不再依赖本地预判。
        _ = isLoggedInCheck;
        ParseCommand = new AsyncRelayCommand(ParseAsync);
    }

    /// <summary>
    /// 保留 P0 的构造入口，但内部同样通过统一 Provider 路径解析，避免兼容代码形成第二条业务链路。
    /// </summary>
    public VideoParseViewModel(
        BiliApiService apiService,
        IBiliCredentialProvider credentialProvider,
        Action<VideoParseResult>? onParsed,
        Func<bool> isLoggedInCheck,
        CancellationToken documentToken = default)
        : this(
            new ContentSourceProviderRegistry(
                [new DirectLinkProvider(apiService, credentialProvider)]),
            apiService,
            credentialProvider,
            onParsed,
            isLoggedInCheck,
            documentToken)
    {
    }

    private async Task ParseAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _documentToken,
            _disposeCts.Token);
        cancellationToken = linked.Token;
        if (IsDisposed) return;
        if (string.IsNullOrWhiteSpace(Url))
        {
            DownloadInfo = "请输入有效的B站视频链接";
            return;
        }

        try
        {
            var provider = _providerRegistry.GetRequired(ContentSourceKind.DirectLink);
            IsLoading = true;
            DownloadInfo = "正在解析视频信息...";

            var descriptor = await provider.NormalizeAsync(Url, cancellationToken);
            var request = new ContentPageRequest();
            var page = await provider.GetPageAsync(descriptor, request, cancellationToken);
            var accumulator = new ContentPageAccumulator();
            var rootItems = accumulator.Append(provider, request, page);
            if (rootItems.Count != 1)
                throw new ContentSourceException(
                    ContentSourceErrorCode.ProtocolViolation,
                    "直接链接来源必须返回唯一根项目。");

            var resolver = _providerRegistry.GetRequiredResolutionProvider(ContentSourceKind.DirectLink);
            var collection = await resolver.ResolveItemAsync(
                descriptor,
                rootItems[0],
                cancellationToken);
            var videoItems = collection.Items.ToList();

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
                var dashResult = await _mediaProbe.GetDashResultAsync(
                    first.Aid,
                    first.Cid,
                    80,
                    _credentialProvider.GetCookieHeader(),
                    first.MediaType,
                    first.EpId,
                    first.SeasonId,
                    cancellationToken);

                // 视频清晰度
                foreach (var q in dashResult.AcceptQualities)
                    qualityOptions.Add(q);
                selectedQuality = qualityOptions.FirstOrDefault();

                // 音频清晰度
                var audioGroups = dashResult.AudioStreams
                    .Where(a => a.AudioFeature == BiliAudioFeature.Standard)
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

            // 所有远端调用完成后才提交状态，失败或取消不会留下半成品结果。
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDisposed) return;
            VideoCollection = collection;
            Url = descriptor.DisplayName;
            CurrentSourceDescriptor = descriptor;
            PersistentSourceChanged?.Invoke();
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
        catch (OperationCanceledException)
        {
            if (IsDisposed) return;
            DownloadInfo = "已取消解析，上一次成功结果保持不变";
        }
        catch (ContentSourceException ex)
        {
            DownloadInfo = ex.Code switch
            {
                ContentSourceErrorCode.InvalidInput => "无法解析链接，请输入有效的B站视频或番剧链接",
                ContentSourceErrorCode.LoginRequired => "请先登录后再解析",
                ContentSourceErrorCode.RemoteFailure => ex.Message,
                _ => "内容源响应不符合协议，请稍后重试",
            };
        }
        catch (MediaAuthorizationException)
        {
            DownloadInfo = "此内容或清晰度需要登录，请登录后重试";
        }
        catch
        {
            DownloadInfo = "解析异常，请稍后重试";
        }
        finally
        {
            if (!IsDisposed) IsLoading = false;
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0 || _documentToken.IsCancellationRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        ParseCommand.Cancel();
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    /// <summary>
    /// 只使用 Document 内的稳定描述符恢复输入，不调用 Normalize、GetPage 或 Resolve。
    /// </summary>
    public void RestoreSource(ContentSourceDescriptor descriptor, string? legacyUrl)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _restoringSource = true;
        Url = string.IsNullOrWhiteSpace(legacyUrl) ? descriptor.DisplayName : legacyUrl;
        CurrentSourceDescriptor = descriptor;
        _restoringSource = false;
    }

    partial void OnUrlChanged(string value)
    {
        if (_restoringSource) return;
        if (CurrentSourceDescriptor is null) return;
        CurrentSourceDescriptor = null;
        PersistentSourceChanged?.Invoke();
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
