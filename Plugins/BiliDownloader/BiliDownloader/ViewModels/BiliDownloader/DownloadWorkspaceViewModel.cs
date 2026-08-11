using System.ComponentModel;
using BiliDownloader.Models;
using BiliDownloader.Services.Naming;
using BiliDownloader.Services.Download;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 下载工作区的组合 ViewModel，只协调配置、命名和视频列表三个子 ViewModel。
/// 设计意图：解析后的展示逻辑不再堆积到 Document ViewModel，Document 只负责生命周期与消息路由。
/// </summary>
public sealed class DownloadWorkspaceViewModel : ObservableObject, IDisposable
{
    private BiliVideoCollection? _videoCollection;
    private bool _isParsed;
    private bool _isDownloadSettingsExpanded;
    private readonly IMediaCapabilityInspectionService? _capabilityInspector;
    private CancellationTokenSource? _capabilityRefreshCts;
    private long _capabilityRefreshVersion;
    private readonly CancellationToken _documentToken;
    private int _disposed;

    public DownloadWorkspaceViewModel(
        DownloadConfigViewModel downloadConfig,
        NamingTemplateViewModel namingTemplate,
        VideoListViewModel videoList,
        IMediaCapabilityInspectionService? capabilityInspector = null,
        CancellationToken documentToken = default)
    {
        DownloadConfig = downloadConfig;
        NamingTemplate = namingTemplate;
        VideoList = videoList;
        _capabilityInspector = capabilityInspector;
        _documentToken = documentToken;

        DownloadConfig.PropertyChanged += OnDownloadConfigPropertyChanged;
        NamingTemplate.PropertyChanged += OnNamingTemplatePropertyChanged;
        VideoList.SelectionOrTitleChanged += RefreshNamingPreview;
        VideoList.SelectionOrTitleChanged += ScheduleCapabilityRefresh;
    }

    public DownloadConfigViewModel DownloadConfig { get; }
    public NamingTemplateViewModel NamingTemplate { get; }
    public VideoListViewModel VideoList { get; }

    public BiliVideoCollection? VideoCollection
    {
        get => _videoCollection;
        private set => SetProperty(ref _videoCollection, value);
    }

    public bool IsParsed
    {
        get => _isParsed;
        set => SetProperty(ref _isParsed, value);
    }

    public bool IsDownloadSettingsExpanded
    {
        get => _isDownloadSettingsExpanded;
        set => SetProperty(ref _isDownloadSettingsExpanded, value);
    }

    /// <summary>折叠状态下只展示足以确认方案的摘要，详细编辑交给三个子 ViewModel。</summary>
    public string DownloadSettingsSummary
    {
        get
        {
            var preset = DownloadConfig.PresetStatusText;
            var videoQuality = DownloadConfig.SelectedQuality?.DisplayName ?? "视频质量待定";
            var audioQuality = DownloadConfig.SelectedAudioQuality?.DisplayName ?? "音频自动";
            var extrasCount = (DownloadConfig.DownloadDanmaku ? 1 : 0)
                + (DownloadConfig.DownloadSubtitle ? 1 : 0)
                + (DownloadConfig.DownloadCover ? 1 : 0);
            var extras = extrasCount == 0 ? "无附加资源" : $"{extrasCount} 项附加资源";
            var naming = NamingTemplate.IsValid ? "命名正常" : "命名需修正";
            var conflict = DownloadConfig.SelectedConflictPolicy.DisplayName;
            var output = GetOutputDirectoryLabel(DownloadConfig.OutputDirectory);
            return $"{preset} · {videoQuality} · {audioQuality} · {extras} · {naming} · {conflict} · {output}";
        }
    }

    public void ApplyParseResult(VideoParseResult result)
    {
        _capabilityInspector?.Clear();
        VideoCollection = result.Collection;
        VideoList.SetItems(result.VideoItems);
        DownloadConfig.PopulateQualities(
            result.QualityOptions,
            result.SelectedQuality,
            result.AudioQualityOptions,
            result.SelectedAudioQuality,
            result.IsMultiVideo);
        RefreshNamingPreview();
        IsParsed = true;
        ScheduleCapabilityRefresh();
    }

    public void RefreshNamingPreview()
    {
        var contexts = VideoList.VideoItems
            .Where(item => item.IsSelected)
            .Select(item => new NamingContext
            {
                Title = item.Title,
                Index = item.Index,
                Bvid = item.Bvid,
                UpName = VideoCollection?.UpName ?? "",
                PublishDate = VideoCollection?.PublishDate,
                SeriesTitle = VideoCollection?.SeriesTitle ?? "",
            })
            .ToList();
        NamingTemplate.UpdatePreview(contexts);
    }

    public void ExpandSettings() => IsDownloadSettingsExpanded = true;

    /// <summary>
    /// 登录态变化会改变会员和登录限制事实，因此必须丢弃当前会话缓存并重新探测已选项目。
    /// </summary>
    public void InvalidateMediaCapabilities()
    {
        _capabilityInspector?.Clear();
        ScheduleCapabilityRefresh();
    }

    private void ScheduleCapabilityRefresh()
    {
        if (IsDisposed) return;
        _capabilityRefreshCts?.Cancel();
        _capabilityRefreshCts?.Dispose();
        _capabilityRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(_documentToken);
        var version = Interlocked.Increment(ref _capabilityRefreshVersion);
        _ = RefreshCapabilitiesAsync(version, _capabilityRefreshCts.Token);
    }

    private async Task RefreshCapabilitiesAsync(long version, CancellationToken cancellationToken)
    {
        if (_capabilityInspector is null) return;
        var selected = VideoList.VideoItems.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            DownloadConfig.ApplyMediaCapabilities(new BatchMediaCapabilitySnapshot(
                0,
                new Dictionary<MediaFeatureFlags, MediaCapabilityAvailability>(),
                new Dictionary<MediaFeatureFlags, int>()));
            return;
        }

        DownloadConfig.IsMediaCapabilityInspecting = true;
        DownloadConfig.MediaCapabilityStatusText = $"正在探测 {selected.Length} 项高规格能力…";
        try
        {
            // 250 ms 防抖让连续勾选只触发一次批量探测；服务内部再以四路并发和会话缓存控制负载。
            await Task.Delay(250, cancellationToken);
            var snapshot = await _capabilityInspector.InspectAsync(
                selected,
                DownloadConfig.SelectedQuality?.QualityId ?? 80,
                cancellationToken);
            if (!IsDisposed && version == Volatile.Read(ref _capabilityRefreshVersion) && !cancellationToken.IsCancellationRequested)
                DownloadConfig.ApplyMediaCapabilities(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 新选择会取代旧结果；取消是预期控制流，不显示为错误。
        }
        catch
        {
            if (!IsDisposed && version == Volatile.Read(ref _capabilityRefreshVersion))
                DownloadConfig.MediaCapabilityStatusText = "高规格能力探测失败；提交时仍会重新预检。";
        }
        finally
        {
            if (!IsDisposed && version == Volatile.Read(ref _capabilityRefreshVersion))
                DownloadConfig.IsMediaCapabilityInspecting = false;
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0 || _documentToken.IsCancellationRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // 工作区通过属性事件把配置、命名和视频列表连接成一张 UI 投影图。必须先拆除这些边，
        // 再取消能力探测并释放子对象，否则子对象的取消收尾仍可能触发命名预览或媒体探测。
        // 版本号递增与令牌取消共同失效已排队的探测结果，确保它们不能写回关闭后的配置状态。
        DownloadConfig.PropertyChanged -= OnDownloadConfigPropertyChanged;
        NamingTemplate.PropertyChanged -= OnNamingTemplatePropertyChanged;
        VideoList.SelectionOrTitleChanged -= RefreshNamingPreview;
        VideoList.SelectionOrTitleChanged -= ScheduleCapabilityRefresh;
        _capabilityRefreshCts?.Cancel();
        _capabilityRefreshCts?.Dispose();
        (VideoList as IDisposable)?.Dispose();
        DownloadConfig.Dispose();
    }

    private void OnDownloadConfigPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(DownloadSettingsSummary));
        if (DownloadConfig.IsRestoredPresetUnavailable
            || !string.IsNullOrWhiteSpace(DownloadConfig.QualityRestoreNotice))
        {
            ExpandSettings();
        }
    }

    private void OnNamingTemplatePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(DownloadSettingsSummary));
        if (!NamingTemplate.IsValid)
            ExpandSettings();
    }

    private static string GetOutputDirectoryLabel(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) return "默认目录";
        var trimmed = Path.TrimEndingDirectorySeparator(outputDirectory);
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? trimmed : leaf;
    }
}
