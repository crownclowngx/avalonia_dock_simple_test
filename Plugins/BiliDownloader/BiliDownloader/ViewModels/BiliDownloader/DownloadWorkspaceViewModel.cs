using System.ComponentModel;
using BiliDownloader.Models;
using BiliDownloader.Services.Naming;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 下载工作区的组合 ViewModel，只协调配置、命名和视频列表三个子 ViewModel。
/// 设计意图：解析后的展示逻辑不再堆积到 Document ViewModel，Document 只负责生命周期与消息路由。
/// </summary>
public sealed class DownloadWorkspaceViewModel : ObservableObject
{
    private BiliVideoCollection? _videoCollection;
    private bool _isParsed;
    private bool _isDownloadSettingsExpanded;

    public DownloadWorkspaceViewModel(
        DownloadConfigViewModel downloadConfig,
        NamingTemplateViewModel namingTemplate,
        VideoListViewModel videoList)
    {
        DownloadConfig = downloadConfig;
        NamingTemplate = namingTemplate;
        VideoList = videoList;

        DownloadConfig.PropertyChanged += OnDownloadConfigPropertyChanged;
        NamingTemplate.PropertyChanged += OnNamingTemplatePropertyChanged;
        VideoList.SelectionOrTitleChanged += RefreshNamingPreview;
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
