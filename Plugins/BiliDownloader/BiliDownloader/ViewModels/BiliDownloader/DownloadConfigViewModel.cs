using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using BiliDownloader.Services.Naming;
using BiliDownloader.Services.Download;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 下载配置子 ViewModel：负责清晰度/音频选择、输出目录、分组文件夹、序号前缀、预设管理。
/// <para>
/// 设计思考（G5 扩展）：新增预设选择层，预设应用 = 批量设置已有属性。
/// 不改变现有属性结构，仅新增 ApplyPreset/SaveAsPreset 方法和预设列表。
/// QualityPreference 在 PopulateQualities 时延迟匹配（预设记录偏好字符串，解析后映射到实际 QualityId）。
/// </para>
/// </summary>
public partial class DownloadConfigViewModel : ObservableObject
{
    private static readonly IPluginLogger Log = PluginLog.For<DownloadConfigViewModel>();
    private readonly ISettingsRepository _settingsRepository;
    private readonly IDownloadPresetService? _presetService;
    private readonly Func<string>? _getNamingTemplate;
    private readonly object _initializationLock = new();
    private Task? _initializationTask;
    private bool _documentConfigurationApplied;
    private bool _isApplyingPreset;
    private bool _isNormalizingOutputCombination;
    private OutputContainer _lastVideoContainer = OutputContainer.Mp4;
    private int? _pendingQualityId;
    private int? _pendingAudioQualityId;
    private string _restoredPresetId = "";
    private readonly Func<CancellationToken, Task<IReadOnlyList<SubtitleLanguageAvailability>>>? _subtitleDiscovery;
    private bool _isNormalizingExtras;

    public event Action<DownloadPreset>? PresetApplied;

    public ObservableCollection<BiliQualityOption> QualityOptions { get; } = new();

    [ObservableProperty]
    private BiliQualityOption? _selectedQuality;

    public ObservableCollection<BiliQualityOption> AudioQualityOptions { get; } = new();

    [ObservableProperty]
    private BiliQualityOption? _selectedAudioQuality;

    [ObservableProperty]
    private bool _useGroupFolder;

    [ObservableProperty]
    private bool _addIndexToTitle = true;

    [ObservableProperty]
    private bool _isMultiVideo;

    [ObservableProperty]
    private string _outputDirectory = "";

    /// <summary>是否下载弹幕</summary>
    [ObservableProperty]
    private bool _downloadDanmaku;

    /// <summary>是否下载字幕</summary>
    [ObservableProperty]
    private bool _downloadSubtitle;

    /// <summary>是否下载封面图</summary>
    [ObservableProperty]
    private bool _downloadCover;

    // P1-G4 先保存完整输出意图，P1-G7～G10 再把这些属性接入实际下载执行和专用编辑 UI。
    [ObservableProperty]
    private VideoCodecPreference _videoCodecPreference = VideoCodecPreference.AutoCompatibility;

    [ObservableProperty]
    private OutputContainer _outputContainer = OutputContainer.Mp4;

    [ObservableProperty]
    private OutputMediaMode _outputMediaMode = OutputMediaMode.AudioVideo;

    /// <summary>编码下拉框使用中文展示对象，Value 才是 Document 与任务快照中的稳定值。</summary>
    public IReadOnlyList<DownloadOutputOption<VideoCodecPreference>> VideoCodecOptions { get; } =
    [
        new(VideoCodecPreference.AutoCompatibility, "自动兼容（AVC → HEVC → AV1）"),
        new(VideoCodecPreference.Avc, "AVC / H.264"),
        new(VideoCodecPreference.Hevc, "HEVC / H.265"),
        new(VideoCodecPreference.Av1, "AV1"),
    ];

    public IReadOnlyList<DownloadOutputOption<OutputMediaMode>> OutputMediaModeOptions { get; } =
    [
        new(OutputMediaMode.AudioVideo, "音视频"),
        new(OutputMediaMode.VideoOnly, "仅视频"),
        new(OutputMediaMode.AudioOnly, "仅音频"),
    ];

    public ObservableCollection<DownloadOutputOption<OutputContainer>> AllowedOutputContainerOptions { get; } =
    [
        new(OutputContainer.Mp4, "MP4"),
        new(OutputContainer.Mkv, "MKV"),
    ];

    public DownloadOutputOption<VideoCodecPreference> SelectedVideoCodecOption
    {
        get => VideoCodecOptions.First(option => option.Value == VideoCodecPreference);
        set { if (value is not null) VideoCodecPreference = value.Value; }
    }

    public DownloadOutputOption<OutputMediaMode> SelectedOutputMediaModeOption
    {
        get => OutputMediaModeOptions.First(option => option.Value == OutputMediaMode);
        set { if (value is not null) OutputMediaMode = value.Value; }
    }

    public DownloadOutputOption<OutputContainer> SelectedOutputContainerOption
    {
        // 恢复期间容器与模式会依次写入，属性通知时允许短暂不匹配但不能抛出。
        get => AllowedOutputContainerOptions.FirstOrDefault(option => option.Value == OutputContainer)
               ?? new DownloadOutputOption<OutputContainer>(OutputContainer, OutputContainer.ToString());
        set { if (value is not null) OutputContainer = value.Value; }
    }

    public bool IsVideoOutputEnabled => OutputMediaMode != OutputMediaMode.AudioOnly;
    public bool IsAudioOutputEnabled => OutputMediaMode != OutputMediaMode.VideoOnly;
    public string OutputModeHint => OutputMediaMode switch
    {
        OutputMediaMode.AudioOnly => "原生音频会按实际规格发布：AAC/Atmos 为 .m4a，Hi-Res FLAC 为 .flac。",
        OutputMediaMode.VideoOnly => "只下载视频流并以 stream copy 封装；不会创建音频临时文件。",
        _ => "自动模式优先高规格；显式规格或编码不可用时会在预检中阻止，不会静默降级。",
    };

    [ObservableProperty]
    private VideoDynamicRangePreference _videoDynamicRangePreference = VideoDynamicRangePreference.Auto;

    [ObservableProperty]
    private AudioFeaturePreference _audioFeaturePreference = AudioFeaturePreference.Auto;

    public IReadOnlyList<DownloadOutputOption<VideoDynamicRangePreference>> VideoDynamicRangeOptions { get; } =
    [
        new(VideoDynamicRangePreference.Auto, "自动（杜比视界 → HDR → 标准）"),
        new(VideoDynamicRangePreference.Standard, "标准动态范围"),
        new(VideoDynamicRangePreference.Hdr, "HDR"),
        new(VideoDynamicRangePreference.DolbyVision, "杜比视界"),
    ];

    public IReadOnlyList<DownloadOutputOption<AudioFeaturePreference>> AudioFeatureOptions { get; } =
    [
        new(AudioFeaturePreference.Auto, "自动（Atmos → Hi-Res → 标准）"),
        new(AudioFeaturePreference.Standard, "标准音频"),
        new(AudioFeaturePreference.HiRes, "Hi-Res 无损"),
        new(AudioFeaturePreference.DolbyAtmos, "杜比全景声"),
    ];

    public DownloadOutputOption<VideoDynamicRangePreference> SelectedVideoDynamicRangeOption
    {
        get => VideoDynamicRangeOptions.First(option => option.Value == VideoDynamicRangePreference);
        set { if (value is not null) VideoDynamicRangePreference = value.Value; }
    }

    public DownloadOutputOption<AudioFeaturePreference> SelectedAudioFeatureOption
    {
        get => AudioFeatureOptions.First(option => option.Value == AudioFeaturePreference);
        set { if (value is not null) AudioFeaturePreference = value.Value; }
    }

    [ObservableProperty]
    private bool _isMediaCapabilityInspecting;

    [ObservableProperty]
    private string _mediaCapabilityStatusText = "请选择媒体后探测高规格能力。";

    [ObservableProperty]
    private MediaCapabilityAvailability _hdrAvailability = MediaCapabilityAvailability.Unknown;

    [ObservableProperty]
    private MediaCapabilityAvailability _dolbyVisionAvailability = MediaCapabilityAvailability.Unknown;

    [ObservableProperty]
    private MediaCapabilityAvailability _hiResAvailability = MediaCapabilityAvailability.Unknown;

    [ObservableProperty]
    private MediaCapabilityAvailability _dolbyAtmosAvailability = MediaCapabilityAvailability.Unknown;

    /// <summary>
    /// 仅用于立即反馈；提交边界仍会重新请求 DASH 并执行权威预检，不能把 UI 缓存当作授权事实。
    /// 显式选择在状态变化后会被保留，并通过此属性标记无效，绝不自动改回 Auto。
    /// </summary>
    public bool IsHighSpecificationSelectionValid
        => (OutputMediaMode == OutputMediaMode.AudioOnly || VideoDynamicRangePreference is VideoDynamicRangePreference.Auto or VideoDynamicRangePreference.Standard
            || GetVideoAvailability(VideoDynamicRangePreference) == MediaCapabilityAvailability.Available)
           && (OutputMediaMode == OutputMediaMode.VideoOnly || AudioFeaturePreference is AudioFeaturePreference.Auto or AudioFeaturePreference.Standard
            || GetAudioAvailability(AudioFeaturePreference) == MediaCapabilityAvailability.Available);

    public void ApplyMediaCapabilities(BatchMediaCapabilitySnapshot snapshot)
    {
        HdrAvailability = snapshot.GetAvailability(MediaFeatureFlags.Hdr);
        DolbyVisionAvailability = snapshot.GetAvailability(MediaFeatureFlags.DolbyVision);
        HiResAvailability = snapshot.GetAvailability(MediaFeatureFlags.HiResAudio);
        DolbyAtmosAvailability = snapshot.GetAvailability(MediaFeatureFlags.DolbyAtmos);
        MediaCapabilityStatusText = snapshot.ItemCount == 0
            ? "请选择媒体后探测高规格能力。"
            : $"批量交集（{snapshot.ItemCount} 项）："
              + $"HDR {FormatCapability(snapshot, MediaFeatureFlags.Hdr)}，"
              + $"杜比视界 {FormatCapability(snapshot, MediaFeatureFlags.DolbyVision)}，"
              + $"Hi-Res {FormatCapability(snapshot, MediaFeatureFlags.HiResAudio)}，"
              + $"Atmos {FormatCapability(snapshot, MediaFeatureFlags.DolbyAtmos)}";
        OnPropertyChanged(nameof(IsHighSpecificationSelectionValid));
    }

    private MediaCapabilityAvailability GetVideoAvailability(VideoDynamicRangePreference preference) => preference switch
    {
        VideoDynamicRangePreference.Hdr => HdrAvailability,
        VideoDynamicRangePreference.DolbyVision => DolbyVisionAvailability,
        _ => MediaCapabilityAvailability.Available,
    };

    private MediaCapabilityAvailability GetAudioAvailability(AudioFeaturePreference preference) => preference switch
    {
        AudioFeaturePreference.HiRes => HiResAvailability,
        AudioFeaturePreference.DolbyAtmos => DolbyAtmosAvailability,
        _ => MediaCapabilityAvailability.Available,
    };

    private static string FormatCapability(BatchMediaCapabilitySnapshot snapshot, MediaFeatureFlags feature)
    {
        var count = snapshot.AvailableCounts.TryGetValue(feature, out var value) ? value : 0;
        var state = snapshot.GetAvailability(feature) switch
        {
            MediaCapabilityAvailability.Available => "全部可用",
            MediaCapabilityAvailability.RequiresPremium => "需要大会员",
            MediaCapabilityAvailability.RequiresLogin => "需要登录",
            MediaCapabilityAvailability.Unavailable => "不可用",
            _ => "未知",
        };
        return $"{state}（{count}/{snapshot.ItemCount}）";
    }

    [ObservableProperty]
    private SubtitleOptions _subtitleOptions = SubtitleOptions.None;

    [ObservableProperty]
    private DanmakuOptions _danmakuOptions = DanmakuOptions.None;

    /// <summary>当前会话手动探测到的语言；覆盖数量不进入 Document，避免恢复时联网。</summary>
    public ObservableCollection<SubtitleLanguageOptionViewModel> SubtitleLanguageOptions { get; } = new();

    public IReadOnlyList<DownloadOutputOption<SubtitleSelectionMode>> SubtitleSelectionModeOptions { get; } =
    [
        new(SubtitleSelectionMode.All, "全部可用语言"),
        new(SubtitleSelectionMode.SelectedLanguages, "指定语言"),
    ];

    public IReadOnlyList<DownloadOutputOption<SubtitleOutputFormat>> SubtitleOutputFormatOptions { get; } =
    [
        new(SubtitleOutputFormat.Srt, "SRT"),
        new(SubtitleOutputFormat.Ass, "ASS"),
        new(SubtitleOutputFormat.Vtt, "WebVTT"),
    ];

    public IReadOnlyList<DownloadOutputOption<SubtitleDeliveryMode>> SubtitleDeliveryModeOptions { get; } =
    [
        new(SubtitleDeliveryMode.External, "外置文件"),
        new(SubtitleDeliveryMode.SoftMuxed, "软字幕封装"),
        new(SubtitleDeliveryMode.ExternalAndSoftMuxed, "外置 + 软字幕"),
    ];

    public DownloadOutputOption<SubtitleSelectionMode> SelectedSubtitleSelectionModeOption
    {
        get => SubtitleSelectionModeOptions.First(option => option.Value == SelectedSubtitleSelectionMode);
        set { if (value is not null) SelectedSubtitleSelectionMode = value.Value; }
    }
    public DownloadOutputOption<SubtitleOutputFormat> SelectedSubtitleOutputFormatOption
    {
        get => SubtitleOutputFormatOptions.First(option => option.Value == SelectedSubtitleOutputFormat);
        set { if (value is not null) SelectedSubtitleOutputFormat = value.Value; }
    }
    public DownloadOutputOption<SubtitleDeliveryMode> SelectedSubtitleDeliveryModeOption
    {
        get => SubtitleDeliveryModeOptions.First(option => option.Value == SelectedSubtitleDeliveryMode);
        set { if (value is not null) SelectedSubtitleDeliveryMode = value.Value; }
    }

    [ObservableProperty] private bool _isSubtitleEnabled;
    [ObservableProperty] private SubtitleSelectionMode _selectedSubtitleSelectionMode = SubtitleSelectionMode.All;
    [ObservableProperty] private SubtitleOutputFormat _selectedSubtitleOutputFormat = SubtitleOutputFormat.Srt;
    [ObservableProperty] private SubtitleDeliveryMode _selectedSubtitleDeliveryMode = SubtitleDeliveryMode.External;
    [ObservableProperty] private bool _isSubtitleDetecting;
    [ObservableProperty] private string _subtitleDetectionStatusText = "启用字幕后，可手动检测当前勾选项的可用语言。";
    [ObservableProperty] private bool _danmakuXmlEnabled;
    [ObservableProperty] private bool _danmakuAssEnabled;
    [ObservableProperty] private bool _danmakuJsonEnabled;

    public IAsyncRelayCommand DetectSubtitlesCommand { get; }

    public bool IsSoftSubtitleCombinationValid
        => !IsSubtitleEnabled
           || SelectedSubtitleDeliveryMode == SubtitleDeliveryMode.External
           || (OutputMediaMode != OutputMediaMode.AudioOnly
               && !(OutputContainer == OutputContainer.Mkv
                    && SelectedSubtitleOutputFormat == SubtitleOutputFormat.Vtt));

    public string SoftSubtitleCompatibilityText => IsSoftSubtitleCombinationValid
        ? "MP4 软字幕使用 mov_text；MKV 保留 SRT/ASS 字幕轨。"
        : "当前容器/模式不支持所选软字幕；可改为外置字幕。";

    [ObservableProperty]
    private long _perTaskRateLimitBytesPerSecond;

    public bool IsPerTaskRateLimitEnabled
    {
        get => PerTaskRateLimitBytesPerSecond > 0;
        set
        {
            if (value == IsPerTaskRateLimitEnabled) return;
            PerTaskRateLimitBytesPerSecond = value
                ? BandwidthLimitPolicy.DefaultEditorBytesPerSecond
                : 0;
        }
    }

    public long PerTaskRateLimitKiBPerSecond
    {
        get => PerTaskRateLimitBytesPerSecond == 0
            ? BandwidthLimitPolicy.DefaultEditorBytesPerSecond / 1024
            : BandwidthLimitPolicy.ToKibibytesPerSecond(PerTaskRateLimitBytesPerSecond);
        set => PerTaskRateLimitBytesPerSecond =
            BandwidthLimitPolicy.FromKibibytesPerSecond(value);
    }

    /// <summary>供界面绑定的中文冲突策略选项；持久化始终使用对应枚举值。</summary>
    public IReadOnlyList<FileConflictPolicyOption> ConflictPolicyOptions { get; } =
        Enum.GetValues<FileConflictPolicy>()
            .Select(policy => new FileConflictPolicyOption(policy, policy.ToDisplayText()))
            .ToArray();

    [ObservableProperty]
    private FileConflictPolicyOption _selectedConflictPolicy =
        new(FileConflictPolicy.AutoNumber, FileConflictPolicy.AutoNumber.ToDisplayText());

    #region G5: 预设管理

    /// <summary>可用预设列表（内置 + 自定义）</summary>
    public ObservableCollection<DownloadPreset> Presets { get; } = new();

    /// <summary>当前选中的预设</summary>
    [ObservableProperty]
    private DownloadPreset? _selectedPreset;

    /// <summary>应用预设命令：将预设配置映射到当前属性</summary>
    public IRelayCommand ApplyPresetCommand { get; }

    /// <summary>将当前配置保存为自定义预设</summary>
    public IRelayCommand SaveAsPresetCommand { get; }

    /// <summary>当前预设的清晰度偏好（延迟匹配用）</summary>
    private string _pendingQualityPreference = "highest";

    [ObservableProperty]
    private string _customPresetName = "";

    [ObservableProperty]
    private bool _isPresetModified;

    [ObservableProperty]
    private bool _isRestoredPresetUnavailable;

    [ObservableProperty]
    private string _qualityRestoreNotice = "";

    public string PresetStatusText => IsRestoredPresetUnavailable
        ? "原预设不可用（已保留文档配置）"
        : SelectedPreset is null
        ? "未选择预设"
        : IsPresetModified ? $"{SelectedPreset.Name} · 已修改" : SelectedPreset.Name;

    public IAsyncRelayCommand DeleteSelectedPresetCommand { get; }
    public IAsyncRelayCommand RenameSelectedPresetCommand { get; }

    #endregion

    public IRelayCommand SelectFolderCommand { get; }

    public DownloadConfigViewModel(
        ISettingsRepository settingsRepository,
        IPresetRepository? presetRepository = null,
        Func<string>? getNamingTemplate = null,
        IDownloadPresetService? presetService = null,
        Func<CancellationToken, Task<IReadOnlyList<SubtitleLanguageAvailability>>>? subtitleDiscovery = null)
    {
        _settingsRepository = settingsRepository;
        _presetService = presetService ?? (presetRepository is null ? null : new DownloadPresetService(presetRepository));
        _getNamingTemplate = getNamingTemplate;
        _subtitleDiscovery = subtitleDiscovery;
        SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync);
        ApplyPresetCommand = new RelayCommand(ApplySelectedPreset);
        SaveAsPresetCommand = new AsyncRelayCommand(SaveAsPresetAsync);
        DeleteSelectedPresetCommand = new AsyncRelayCommand(DeleteSelectedPresetAsync);
        RenameSelectedPresetCommand = new AsyncRelayCommand(RenameSelectedPresetAsync);
        DetectSubtitlesCommand = new AsyncRelayCommand(DetectSubtitlesAsync, () => !IsSubtitleDetecting);
    }

    /// <summary>
    /// 初始化：加载默认目录、预设列表、恢复最后使用的预设。
    /// 设计思考：合并原来的 InitDefaultOutputDirectoryAsync 和预设加载，
    /// 避免多次异步初始化竞争。
    /// </summary>
    public Task InitializeAsync()
    {
        lock (_initializationLock)
            return _initializationTask ??= InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            await _settingsRepository.InitAsync();

            // Document 内保存的实际配置优先于全局默认目录。
            if (!_documentConfigurationApplied)
            {
                var savedDir = await _settingsRepository.GetSettingAsync("default_output_dir");
                if (!string.IsNullOrEmpty(savedDir))
                {
                    OutputDirectory = savedDir;
                }
                else
                {
                    var appDir = Path.GetDirectoryName(typeof(DownloadConfigViewModel).Assembly.Location) ?? "";
                    OutputDirectory = Path.Combine(appDir, "视频下载");
                }
            }

            // 加载预设列表
            if (_presetService != null)
            {
                var presets = await _presetService.GetAllAsync();
                Presets.Clear();
                foreach (var p in presets)
                    Presets.Add(p);

                if (_documentConfigurationApplied)
                {
                    SelectRestoredPreset(_restoredPresetId);
                }

                // 恢复最后使用的预设
                var lastPresetId = await _settingsRepository.GetSettingAsync("last_preset_id");
                if (!_documentConfigurationApplied && !string.IsNullOrEmpty(lastPresetId))
                {
                    var lastPreset = presets.FirstOrDefault(p => p.Id == lastPresetId);
                    if (lastPreset != null)
                    {
                        SelectedPreset = lastPreset;
                        ApplyPreset(lastPreset);
                    }
                }
            }
            else if (_documentConfigurationApplied)
            {
                SelectRestoredPreset(_restoredPresetId);
            }
        }
        catch (Exception ex)
        {
            Log.Error("初始化下载配置失败。", ex);
            // 回退默认值
            if (!_documentConfigurationApplied && string.IsNullOrEmpty(OutputDirectory))
            {
                var appDir = Path.GetDirectoryName(typeof(DownloadConfigViewModel).Assembly.Location) ?? "";
                OutputDirectory = Path.Combine(appDir, "视频下载");
            }
        }
    }

    /// <summary>
    /// 由主 VM 在解析成功后调用，填充清晰度选项。
    /// G5 扩展：解析完成后根据预设的 QualityPreference 延迟匹配实际清晰度。
    /// </summary>
    public void PopulateQualities(
        List<BiliQualityOption> qualities,
        BiliQualityOption? selectedQuality,
        List<BiliQualityOption> audioQualities,
        BiliQualityOption? selectedAudioQuality,
        bool isMultiVideo)
    {
        // 125/126 是 HDR/杜比视界能力流，不是普通画质档位。把它们留在画质下拉会造成
        // “选了 126 又选标准动态范围”这类矛盾状态，因此只由专用高规格偏好控制。
        var standardQualities = qualities.Where(q => q.QualityId is not 125 and not 126).ToList();
        if (selectedQuality?.QualityId is 125 or 126)
            selectedQuality = standardQualities.FirstOrDefault();
        QualityOptions.Clear();
        foreach (var q in standardQualities)
            QualityOptions.Add(q);

        AudioQualityOptions.Clear();
        foreach (var a in audioQualities)
            AudioQualityOptions.Add(a);

        IsMultiVideo = isMultiVideo;
        if (!_documentConfigurationApplied && SelectedPreset is null)
            UseGroupFolder = isMultiVideo;

        // G5: 如果有待匹配的清晰度偏好，延迟匹配到实际可用选项
        if (_pendingQualityId is int pendingQuality)
        {
            var restored = standardQualities.FirstOrDefault(q => q.QualityId == pendingQuality);
            SelectedQuality = restored
                ?? MatchQualityByPreference(standardQualities, _pendingQualityPreference)
                ?? selectedQuality;
            QualityRestoreNotice = restored is null
                ? $"原视频画质 {pendingQuality} 当前不可用，已选择 {SelectedQuality?.DisplayName ?? "可用画质"}。"
                : "";
            _pendingQualityId = null;
        }
        else if (!string.IsNullOrEmpty(_pendingQualityPreference) && QualityOptions.Count > 0)
        {
            SelectedQuality = MatchQualityByPreference(standardQualities, _pendingQualityPreference);
            _pendingQualityPreference = ""; // 匹配完成后清除
        }
        else
        {
            SelectedQuality = selectedQuality;
        }

        if (_pendingAudioQualityId is int pendingAudio)
        {
            var restoredAudio = audioQualities.FirstOrDefault(q => q.QualityId == pendingAudio);
            SelectedAudioQuality = restoredAudio ?? selectedAudioQuality;
            if (restoredAudio is null)
                QualityRestoreNotice += $" 原音频质量 {pendingAudio} 当前不可用，已使用可用选项。";
            _pendingAudioQualityId = null;
        }
        else
        {
            SelectedAudioQuality = selectedAudioQuality;
        }
    }

    /// <summary>
    /// 应用预设：将预设字段批量映射到当前配置属性。
    /// <para>
    /// 设计思考：预设应用是“批量设置已有属性”，不引入新属性层。
    /// QualityPreference 需要延迟到 PopulateQualities 时才能匹配到实际选项。
    /// </para>
    /// </summary>
    public void ApplyPreset(DownloadPreset preset)
    {
        _isApplyingPreset = true;
        UseGroupFolder = preset.UseGroupFolder;
        AddIndexToTitle = preset.AddIndexToTitle;
        DownloadDanmaku = preset.DownloadDanmaku;
        DownloadSubtitle = preset.DownloadSubtitle;
        DownloadCover = preset.DownloadCover;
        VideoCodecPreference = preset.VideoCodecPreference;
        OutputContainer = preset.OutputContainer;
        OutputMediaMode = preset.OutputMediaMode;
        VideoDynamicRangePreference = preset.VideoDynamicRangePreference;
        AudioFeaturePreference = preset.AudioFeaturePreference;
        SubtitleOptions = NormalizeSubtitleOptions(preset.SubtitleOptions, preset.DownloadSubtitle);
        DanmakuOptions = NormalizeDanmakuOptions(preset.DanmakuOptions, preset.DownloadDanmaku);
        PerTaskRateLimitBytesPerSecond = NormalizePersistedRateLimit(
            preset.PerTaskRateLimitBytesPerSecond,
            $"preset {preset.Id}");
        DownloadSubtitle = SubtitleOptions.SelectionMode != SubtitleSelectionMode.None;
        DownloadDanmaku = DanmakuOptions.Formats.Count > 0;
        SelectedConflictPolicy = ConflictPolicyOptions.First(option => option.Value == preset.ConflictPolicy);

        // 输出目录：预设指定则使用，否则保持当前默认
        if (!string.IsNullOrEmpty(preset.OutputDirectory))
            OutputDirectory = preset.OutputDirectory;

        // 清晰度偏好延迟匹配（解析完成后在 PopulateQualities 中生效）
        _pendingQualityPreference = preset.QualityPreference;
        _pendingAudioQualityId = preset.AudioQualityId;

        // 如果清晰度选项已加载，立即匹配
        if (QualityOptions.Count > 0)
        {
            SelectedQuality = MatchQualityByPreference(QualityOptions.ToList(), preset.QualityPreference);
            _pendingQualityPreference = "";
        }
        if (AudioQualityOptions.Count > 0)
            SelectedAudioQuality = AudioQualityOptions.FirstOrDefault(q => q.QualityId == preset.AudioQualityId)
                ?? AudioQualityOptions.FirstOrDefault();

        SelectedPreset = preset;
        IsRestoredPresetUnavailable = false;
        IsPresetModified = false;
        _isApplyingPreset = false;
        PresetApplied?.Invoke(preset);
        OnPropertyChanged(nameof(PresetStatusText));
    }

    /// <summary>
    /// 从当前配置属性快照生成预设对象。
    /// </summary>
    public DownloadPreset CaptureCurrentAsPreset(string id, string name)
    {
        return DownloadPreset.FromProfile(id, name, CaptureCurrentProfile());
    }

    public DownloadProfile CaptureCurrentProfile() => new(
        SelectedQuality is null ? (_pendingQualityPreference.Length == 0 ? "highest" : _pendingQualityPreference) : $"quality:{SelectedQuality.QualityId}",
        SelectedAudioQuality?.QualityId ?? _pendingAudioQualityId ?? 0,
        UseGroupFolder,
        AddIndexToTitle,
        DownloadDanmaku,
        DownloadSubtitle,
        DownloadCover,
        _getNamingTemplate?.Invoke() ?? NamingTemplateEngine.DefaultTemplate,
        OutputDirectory,
        SelectedConflictPolicy.Value,
        VideoCodecPreference,
        OutputContainer,
        OutputMediaMode,
        VideoDynamicRangePreference,
        AudioFeaturePreference,
        NormalizeSubtitleOptions(SubtitleOptions, DownloadSubtitle),
        NormalizeDanmakuOptions(DanmakuOptions, DownloadDanmaku),
        PerTaskRateLimitBytesPerSecond);

    /// <summary>
    /// 取得增量去重使用的当前输出身份。恢复 Document 后画质可能尚未填充为下拉项，
    /// 因此优先使用当前选择、其次使用 V3 保存的待匹配 ID；两者都不存在时返回 null，
    /// 防止用 QualityId=0 生成不可复现的指纹。
    /// </summary>
    public RenditionSpecification? CaptureRenditionSpecification()
    {
        var videoQualityId = SelectedQuality?.QualityId ?? _pendingQualityId ?? 0;
        if (OutputMediaMode != OutputMediaMode.AudioOnly && videoQualityId <= 0) return null;
        return new RenditionSpecification(
            videoQualityId,
            SelectedAudioQuality?.QualityId ?? _pendingAudioQualityId ?? 0,
            VideoCodecPreference,
            OutputContainer,
            OutputMediaMode,
            VideoDynamicRangePreference,
            AudioFeaturePreference).Canonicalize();
    }

    public void RestoreDocumentConfiguration(DocumentSaveDataV2 data)
    {
        _documentConfigurationApplied = true;
        _isApplyingPreset = true;
        OutputDirectory = data.OutputDirectory;
        UseGroupFolder = data.UseGroupFolder;
        AddIndexToTitle = data.AddIndexToTitle;
        DownloadDanmaku = data.DownloadDanmaku;
        DownloadSubtitle = data.DownloadSubtitle;
        DownloadCover = data.DownloadCover;
        SelectedConflictPolicy = ConflictPolicyOptions.First(option => option.Value == data.ConflictPolicy);
        _pendingQualityId = data.QualityId > 0 ? data.QualityId : null;
        _pendingAudioQualityId = data.AudioQualityId;
        _isApplyingPreset = false;
        _restoredPresetId = data.PresetId;
        if (_initializationTask?.IsCompletedSuccessfully == true)
            SelectRestoredPreset(_restoredPresetId);
    }

    /// <summary>
    /// 从 V3 快照恢复完整配置。恢复阶段只赋值本地属性，不读取预设库之外的外部状态，
    /// 也不会触发媒体解析或下载执行。
    /// </summary>
    public void RestoreDocumentConfiguration(DocumentSaveDataV3 data)
    {
        _documentConfigurationApplied = true;
        _isApplyingPreset = true;
        OutputDirectory = data.OutputDirectory;
        UseGroupFolder = data.UseGroupFolder;
        AddIndexToTitle = data.AddIndexToTitle;
        DownloadCover = data.DownloadCover;
        VideoCodecPreference = data.VideoCodecPreference;
        OutputContainer = data.OutputContainer;
        OutputMediaMode = data.OutputMediaMode;
        VideoDynamicRangePreference = data.VideoDynamicRangePreference;
        AudioFeaturePreference = data.AudioFeaturePreference;
        SubtitleOptions = NormalizeSubtitleOptions(data.SubtitleOptions, data.DownloadSubtitle);
        DanmakuOptions = NormalizeDanmakuOptions(data.DanmakuOptions, data.DownloadDanmaku);
        DownloadSubtitle = SubtitleOptions.SelectionMode != SubtitleSelectionMode.None;
        DownloadDanmaku = DanmakuOptions.Formats.Count > 0;
        PerTaskRateLimitBytesPerSecond = data.PerTaskRateLimitBytesPerSecond;
        SelectedConflictPolicy = ConflictPolicyOptions.First(option => option.Value == data.ConflictPolicy);
        _pendingQualityId = data.QualityId > 0 ? data.QualityId : null;
        _pendingAudioQualityId = data.AudioQualityId;
        _isApplyingPreset = false;
        _restoredPresetId = data.PresetId;
        if (_initializationTask?.IsCompletedSuccessfully == true)
            SelectRestoredPreset(_restoredPresetId);
    }

    /// <summary>
    /// 应用当前选中的预设。
    /// </summary>
    private void ApplySelectedPreset()
    {
        if (SelectedPreset == null) return;
        ApplyPreset(SelectedPreset);

        // 记忆最后使用的预设
        _ = RememberLastPresetAsync(SelectedPreset.Id);
    }

    /// <summary>
    /// 将当前配置保存为自定义预设。
    /// </summary>
    private async Task SaveAsPresetAsync()
    {
        if (_presetService == null) return;

        try
        {
            var name = string.IsNullOrWhiteSpace(CustomPresetName)
                ? $"自定义预设 {Presets.Count(p => !p.IsBuiltIn) + 1}"
                : CustomPresetName.Trim();
            var preset = await _presetService.SaveCopyAsync(CaptureCurrentProfile(), name);

            // 刷新预设列表
            var allPresets = await _presetService.GetAllAsync();
            Presets.Clear();
            foreach (var p in allPresets)
                Presets.Add(p);

            SelectedPreset = preset;
            IsPresetModified = false;
            CustomPresetName = "";
            await RememberLastPresetAsync(preset.Id);
        }
        catch (Exception ex)
        {
            Log.Error("保存自定义预设失败。", ex);
        }
    }

    /// <summary>
    /// 记忆最后使用的预设 ID。
    /// </summary>
    private async Task RememberLastPresetAsync(string presetId)
    {
        try
        {
            await _settingsRepository.SetSettingAsync("last_preset_id", presetId);
        }
        catch { /* 记忆失败不影响主流程 */ }
    }

    /// <summary>
    /// 根据清晰度偏好字符串匹配实际可用的清晰度选项。
    /// <para>
    /// 设计思考："highest" 选最高画质，"1080p"/"720p" 匹配最接近的选项。
    /// 如果目标清晰度不可用，回退到最高可用。
    /// </para>
    /// </summary>
    private static BiliQualityOption? MatchQualityByPreference(
        List<BiliQualityOption> qualities, string preference)
    {
        if (qualities.Count == 0) return null;

        if (preference.StartsWith("quality:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(preference[8..], out var qualityId))
            return qualities.FirstOrDefault(q => q.QualityId == qualityId)
                ?? qualities.OrderByDescending(q => q.QualityId).FirstOrDefault();

        return preference.ToLowerInvariant() switch
        {
            "720p" => qualities.FirstOrDefault(q => q.QualityId == 64)
                      ?? qualities.FirstOrDefault(q => q.QualityId == 32)
                      ?? qualities[0],
            "1080p" => qualities.FirstOrDefault(q => q.QualityId == 80)
                       ?? qualities.OrderByDescending(q => q.QualityId).FirstOrDefault(),
            _ => qualities.OrderByDescending(q => q.QualityId).FirstOrDefault() // highest
        };
    }

    private async Task SelectFolderAsync()
    {
        try
        {
            var parentWindow = GetParentWindow();
            if (parentWindow is null)
            {
                return;
            }

            var result = await parentWindow.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "选择下载输出目录",
                    AllowMultiple = false
                });
            if (result.Count > 0)
            {
                OutputDirectory = result[0].Path.LocalPath;
                await _settingsRepository.SetSettingAsync("default_output_dir", OutputDirectory);
            }
        }
        catch (Exception ex)
        {
            Log.Error("选择文件夹失败。", ex);
        }
    }

    private Window? GetParentWindow()
    {
        try
        {
            var app = Avalonia.Application.Current;
            return app?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void SelectRestoredPreset(string presetId)
    {
        SelectedPreset = Presets.FirstOrDefault(p => p.Id == presetId);
        IsRestoredPresetUnavailable = !string.IsNullOrWhiteSpace(presetId) && SelectedPreset is null;
        IsPresetModified = SelectedPreset is null;
        OnPropertyChanged(nameof(PresetStatusText));
    }

    private async Task DeleteSelectedPresetAsync()
    {
        if (_presetService is null || SelectedPreset is null || SelectedPreset.IsBuiltIn) return;
        var id = SelectedPreset.Id;
        await _presetService.DeleteAsync(id);
        var all = await _presetService.GetAllAsync();
        Presets.Clear();
        foreach (var preset in all) Presets.Add(preset);
        SelectedPreset = Presets.FirstOrDefault();
        IsPresetModified = false;
        OnPropertyChanged(nameof(PresetStatusText));
    }

    private async Task RenameSelectedPresetAsync()
    {
        if (_presetService is null || SelectedPreset is null || SelectedPreset.IsBuiltIn
            || string.IsNullOrWhiteSpace(CustomPresetName)) return;
        var oldPreset = SelectedPreset;
        var renamed = await _presetService.RenameAsync(oldPreset.Id, CustomPresetName);
        if (renamed is null) return;
        var index = Presets.IndexOf(oldPreset);
        if (index >= 0) Presets[index] = renamed;
        SelectedPreset = renamed;
        CustomPresetName = "";
        OnPropertyChanged(nameof(PresetStatusText));
    }

    private void MarkPresetModified()
    {
        if (_isApplyingPreset || SelectedPreset is null) return;
        IsPresetModified = true;
        OnPropertyChanged(nameof(PresetStatusText));
    }

    partial void OnSelectedQualityChanged(BiliQualityOption? value) => MarkPresetModified();
    partial void OnSelectedAudioQualityChanged(BiliQualityOption? value) => MarkPresetModified();
    partial void OnUseGroupFolderChanged(bool value) => MarkPresetModified();
    partial void OnAddIndexToTitleChanged(bool value) => MarkPresetModified();
    partial void OnDownloadDanmakuChanged(bool value) => MarkPresetModified();
    partial void OnDownloadSubtitleChanged(bool value) => MarkPresetModified();
    partial void OnDownloadCoverChanged(bool value) => MarkPresetModified();
    partial void OnVideoCodecPreferenceChanged(VideoCodecPreference value)
    {
        OnPropertyChanged(nameof(SelectedVideoCodecOption));
        MarkPresetModified();
    }

    partial void OnOutputContainerChanged(OutputContainer value)
    {
        if (!_isNormalizingOutputCombination && value is OutputContainer.Mp4 or OutputContainer.Mkv)
            _lastVideoContainer = value;
        OnPropertyChanged(nameof(SelectedOutputContainerOption));
        OnPropertyChanged(nameof(IsSoftSubtitleCombinationValid));
        OnPropertyChanged(nameof(SoftSubtitleCompatibilityText));
        MarkPresetModified();
    }

    partial void OnOutputMediaModeChanged(OutputMediaMode value)
    {
        _isNormalizingOutputCombination = true;
        try
        {
            AllowedOutputContainerOptions.Clear();
            if (value == OutputMediaMode.AudioOnly)
            {
                AllowedOutputContainerOptions.Add(new(OutputContainer.NativeAudio, "原生音频 (.m4a)"));
                OutputContainer = OutputContainer.NativeAudio;
            }
            else
            {
                AllowedOutputContainerOptions.Add(new(OutputContainer.Mp4, "MP4"));
                AllowedOutputContainerOptions.Add(new(OutputContainer.Mkv, "MKV"));
                OutputContainer = _lastVideoContainer;
            }
        }
        finally
        {
            _isNormalizingOutputCombination = false;
        }
        OnPropertyChanged(nameof(SelectedOutputMediaModeOption));
        OnPropertyChanged(nameof(SelectedOutputContainerOption));
        OnPropertyChanged(nameof(IsVideoOutputEnabled));
        OnPropertyChanged(nameof(IsAudioOutputEnabled));
        OnPropertyChanged(nameof(OutputModeHint));
        OnPropertyChanged(nameof(IsHighSpecificationSelectionValid));
        OnPropertyChanged(nameof(IsSoftSubtitleCombinationValid));
        OnPropertyChanged(nameof(SoftSubtitleCompatibilityText));
        MarkPresetModified();
    }
    partial void OnVideoDynamicRangePreferenceChanged(VideoDynamicRangePreference value)
    {
        OnPropertyChanged(nameof(SelectedVideoDynamicRangeOption));
        OnPropertyChanged(nameof(IsHighSpecificationSelectionValid));
        MarkPresetModified();
    }
    partial void OnAudioFeaturePreferenceChanged(AudioFeaturePreference value)
    {
        OnPropertyChanged(nameof(SelectedAudioFeatureOption));
        OnPropertyChanged(nameof(IsHighSpecificationSelectionValid));
        MarkPresetModified();
    }
    partial void OnSubtitleOptionsChanged(SubtitleOptions value)
    {
        if (!_isNormalizingExtras) ApplySubtitleOptionsToEditor(value.Canonicalize());
        MarkPresetModified();
    }
    partial void OnDanmakuOptionsChanged(DanmakuOptions value)
    {
        if (!_isNormalizingExtras) ApplyDanmakuOptionsToEditor(value.Canonicalize());
        MarkPresetModified();
    }
    partial void OnIsSubtitleEnabledChanged(bool value) => UpdateSubtitleOptionsFromEditor();
    partial void OnSelectedSubtitleSelectionModeChanged(SubtitleSelectionMode value)
    {
        OnPropertyChanged(nameof(SelectedSubtitleSelectionModeOption));
        UpdateSubtitleOptionsFromEditor();
    }
    partial void OnSelectedSubtitleOutputFormatChanged(SubtitleOutputFormat value)
    {
        OnPropertyChanged(nameof(SelectedSubtitleOutputFormatOption));
        UpdateSubtitleOptionsFromEditor();
        OnPropertyChanged(nameof(IsSoftSubtitleCombinationValid));
        OnPropertyChanged(nameof(SoftSubtitleCompatibilityText));
    }
    partial void OnSelectedSubtitleDeliveryModeChanged(SubtitleDeliveryMode value)
    {
        OnPropertyChanged(nameof(SelectedSubtitleDeliveryModeOption));
        UpdateSubtitleOptionsFromEditor();
        OnPropertyChanged(nameof(IsSoftSubtitleCombinationValid));
        OnPropertyChanged(nameof(SoftSubtitleCompatibilityText));
    }
    partial void OnIsSubtitleDetectingChanged(bool value) => DetectSubtitlesCommand.NotifyCanExecuteChanged();
    partial void OnDanmakuXmlEnabledChanged(bool value) => UpdateDanmakuOptionsFromEditor();
    partial void OnDanmakuAssEnabledChanged(bool value) => UpdateDanmakuOptionsFromEditor();
    partial void OnDanmakuJsonEnabledChanged(bool value) => UpdateDanmakuOptionsFromEditor();
    partial void OnPerTaskRateLimitBytesPerSecondChanging(long value)
    {
        BandwidthLimitPolicy.Validate(value, nameof(PerTaskRateLimitBytesPerSecond));
    }
    partial void OnPerTaskRateLimitBytesPerSecondChanged(long value)
    {
        OnPropertyChanged(nameof(IsPerTaskRateLimitEnabled));
        OnPropertyChanged(nameof(PerTaskRateLimitKiBPerSecond));
        MarkPresetModified();
    }
    partial void OnOutputDirectoryChanged(string value) => MarkPresetModified();
    partial void OnSelectedConflictPolicyChanged(FileConflictPolicyOption value) => MarkPresetModified();
    partial void OnIsPresetModifiedChanged(bool value) => OnPropertyChanged(nameof(PresetStatusText));
    partial void OnIsRestoredPresetUnavailableChanged(bool value) => OnPropertyChanged(nameof(PresetStatusText));

    private static SubtitleOptions NormalizeSubtitleOptions(SubtitleOptions? value, bool legacyEnabled) =>
        value is not null && value.SelectionMode != SubtitleSelectionMode.None
            ? value
            : legacyEnabled ? global::BiliDownloader.Models.SubtitleOptions.LegacyEnabled : global::BiliDownloader.Models.SubtitleOptions.None;

    private static DanmakuOptions NormalizeDanmakuOptions(DanmakuOptions? value, bool legacyEnabled) =>
        value is not null && value.Formats.Count > 0
            ? value
            : legacyEnabled ? global::BiliDownloader.Models.DanmakuOptions.LegacyEnabled : global::BiliDownloader.Models.DanmakuOptions.None;

    private static long NormalizePersistedRateLimit(long value, string source)
    {
        try
        {
            return BandwidthLimitPolicy.Validate(value);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Log.Warn($"忽略无效的单任务限速配置；来源={source}，原值={value} B/s，回退为不限速。"
                + $" 原因={ex.Message}");
            return 0;
        }
    }

    private async Task DetectSubtitlesAsync()
    {
        if (_subtitleDiscovery is null)
        {
            SubtitleDetectionStatusText = "当前构造路径没有字幕探测服务。";
            return;
        }
        IsSubtitleDetecting = true;
        try
        {
            var discovered = await _subtitleDiscovery(CancellationToken.None);
            var selected = SubtitleOptions.LanguageKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            SubtitleLanguageOptions.Clear();
            foreach (var language in discovered)
                SubtitleLanguageOptions.Add(new SubtitleLanguageOptionViewModel(
                    language, selected.Contains(language.StableLanguageKey), OnSubtitleLanguageSelectionChanged));
            SubtitleDetectionStatusText = discovered.Count == 0
                ? "所选媒体没有可用字幕。"
                : $"已发现 {discovered.Count} 种语言；覆盖数量基于当前所选媒体。";
        }
        catch (OperationCanceledException)
        {
            SubtitleDetectionStatusText = "字幕检测已取消，保留上一次成功结果。";
        }
        catch (Exception ex)
        {
            SubtitleDetectionStatusText = "字幕检测失败：" + SensitiveDataSanitizer.Sanitize(ex.Message);
        }
        finally { IsSubtitleDetecting = false; }
    }

    private void OnSubtitleLanguageSelectionChanged() => UpdateSubtitleOptionsFromEditor();

    private void UpdateSubtitleOptionsFromEditor()
    {
        if (_isNormalizingExtras) return;
        _isNormalizingExtras = true;
        try
        {
            SubtitleOptions = !IsSubtitleEnabled
                ? global::BiliDownloader.Models.SubtitleOptions.None
                : new SubtitleOptions
                {
                    SelectionMode = SelectedSubtitleSelectionMode,
                    OutputFormat = SelectedSubtitleOutputFormat,
                    DeliveryMode = SelectedSubtitleDeliveryMode,
                    LanguageKeys = SubtitleLanguageOptions.Where(static item => item.IsSelected)
                        .Select(static item => item.StableLanguageKey).ToArray(),
                }.Canonicalize();
            DownloadSubtitle = SubtitleOptions.SelectionMode != SubtitleSelectionMode.None;
        }
        finally { _isNormalizingExtras = false; }
        MarkPresetModified();
    }

    private void ApplySubtitleOptionsToEditor(SubtitleOptions value)
    {
        _isNormalizingExtras = true;
        try
        {
            IsSubtitleEnabled = value.SelectionMode != SubtitleSelectionMode.None;
            SelectedSubtitleSelectionMode = value.SelectionMode == SubtitleSelectionMode.None
                ? SubtitleSelectionMode.All : value.SelectionMode;
            SelectedSubtitleOutputFormat = value.OutputFormat;
            SelectedSubtitleDeliveryMode = value.DeliveryMode;
            var selected = value.LanguageKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var language in SubtitleLanguageOptions)
                language.SetSelectedWithoutCallback(selected.Contains(language.StableLanguageKey));
        }
        finally { _isNormalizingExtras = false; }
    }

    private void UpdateDanmakuOptionsFromEditor()
    {
        if (_isNormalizingExtras) return;
        var formats = new List<DanmakuOutputFormat>();
        if (DanmakuXmlEnabled) formats.Add(DanmakuOutputFormat.Xml);
        if (DanmakuAssEnabled) formats.Add(DanmakuOutputFormat.Ass);
        if (DanmakuJsonEnabled) formats.Add(DanmakuOutputFormat.Json);
        _isNormalizingExtras = true;
        try
        {
            DanmakuOptions = new DanmakuOptions { Formats = formats, AssStyleId = "default" };
            DownloadDanmaku = formats.Count > 0;
        }
        finally { _isNormalizingExtras = false; }
        MarkPresetModified();
    }

    private void ApplyDanmakuOptionsToEditor(DanmakuOptions value)
    {
        _isNormalizingExtras = true;
        try
        {
            DanmakuXmlEnabled = value.Formats.Contains(DanmakuOutputFormat.Xml);
            DanmakuAssEnabled = value.Formats.Contains(DanmakuOutputFormat.Ass);
            DanmakuJsonEnabled = value.Formats.Contains(DanmakuOutputFormat.Json);
        }
        finally { _isNormalizingExtras = false; }
    }
}

/// <summary>字幕语言多选项；回调只通知父 VM 重建不可变 SubtitleOptions。</summary>
public sealed partial class SubtitleLanguageOptionViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _suppressSelectionChanged;

    public SubtitleLanguageOptionViewModel(
        SubtitleLanguageAvailability availability, bool selected, Action selectionChanged)
    {
        StableLanguageKey = availability.StableLanguageKey;
        DisplayName = availability.DisplayName;
        SourceType = availability.SourceType;
        AvailableItemCount = availability.AvailableItemCount;
        TotalItemCount = availability.TotalItemCount;
        _selectionChanged = selectionChanged;
        _suppressSelectionChanged = true;
        IsSelected = selected;
        _suppressSelectionChanged = false;
    }

    public string StableLanguageKey { get; }
    public string DisplayName { get; }
    public SubtitleSourceType SourceType { get; }
    public int AvailableItemCount { get; }
    public int TotalItemCount { get; }
    public string DisplayText => $"{DisplayName}（{SourceType}，{AvailableItemCount}/{TotalItemCount}）";

    [ObservableProperty] private bool _isSelected;
    partial void OnIsSelectedChanged(bool value)
    {
        if (!_suppressSelectionChanged) _selectionChanged();
    }
    public void SetSelectedWithoutCallback(bool value)
    {
        _suppressSelectionChanged = true;
        IsSelected = value;
        _suppressSelectionChanged = false;
    }
}

/// <summary>文件冲突策略的界面选项；显示文案与持久化值明确分离。</summary>
public sealed record FileConflictPolicyOption(FileConflictPolicy Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>通用下载输出下拉选项；显示文本不参与持久化和业务判断。</summary>
public sealed record DownloadOutputOption<T>(T Value, string DisplayName)
    where T : struct, Enum
{
    public override string ToString() => DisplayName;
}
