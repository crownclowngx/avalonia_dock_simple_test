using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using BiliDownloader.Services.Naming;

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
    private int? _pendingQualityId;
    private int? _pendingAudioQualityId;
    private string _restoredPresetId = "";

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

    [ObservableProperty]
    private VideoDynamicRangePreference _videoDynamicRangePreference = VideoDynamicRangePreference.Auto;

    [ObservableProperty]
    private AudioFeaturePreference _audioFeaturePreference = AudioFeaturePreference.Auto;

    [ObservableProperty]
    private SubtitleOptions _subtitleOptions = SubtitleOptions.None;

    [ObservableProperty]
    private DanmakuOptions _danmakuOptions = DanmakuOptions.None;

    [ObservableProperty]
    private long _perTaskRateLimitBytesPerSecond;

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
        IDownloadPresetService? presetService = null)
    {
        _settingsRepository = settingsRepository;
        _presetService = presetService ?? (presetRepository is null ? null : new DownloadPresetService(presetRepository));
        _getNamingTemplate = getNamingTemplate;
        SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync);
        ApplyPresetCommand = new RelayCommand(ApplySelectedPreset);
        SaveAsPresetCommand = new AsyncRelayCommand(SaveAsPresetAsync);
        DeleteSelectedPresetCommand = new AsyncRelayCommand(DeleteSelectedPresetAsync);
        RenameSelectedPresetCommand = new AsyncRelayCommand(RenameSelectedPresetAsync);
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
        QualityOptions.Clear();
        foreach (var q in qualities)
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
            var restored = qualities.FirstOrDefault(q => q.QualityId == pendingQuality);
            SelectedQuality = restored
                ?? MatchQualityByPreference(qualities, _pendingQualityPreference)
                ?? selectedQuality;
            QualityRestoreNotice = restored is null
                ? $"原视频画质 {pendingQuality} 当前不可用，已选择 {SelectedQuality?.DisplayName ?? "可用画质"}。"
                : "";
            _pendingQualityId = null;
        }
        else if (!string.IsNullOrEmpty(_pendingQualityPreference) && QualityOptions.Count > 0)
        {
            SelectedQuality = MatchQualityByPreference(qualities, _pendingQualityPreference);
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
        PerTaskRateLimitBytesPerSecond = Math.Max(0, preset.PerTaskRateLimitBytesPerSecond);
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
    partial void OnVideoCodecPreferenceChanged(VideoCodecPreference value) => MarkPresetModified();
    partial void OnOutputContainerChanged(OutputContainer value) => MarkPresetModified();
    partial void OnOutputMediaModeChanged(OutputMediaMode value) => MarkPresetModified();
    partial void OnVideoDynamicRangePreferenceChanged(VideoDynamicRangePreference value) => MarkPresetModified();
    partial void OnAudioFeaturePreferenceChanged(AudioFeaturePreference value) => MarkPresetModified();
    partial void OnSubtitleOptionsChanged(SubtitleOptions value) => MarkPresetModified();
    partial void OnDanmakuOptionsChanged(DanmakuOptions value) => MarkPresetModified();
    partial void OnPerTaskRateLimitBytesPerSecondChanging(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "单任务限速不能为负数。");
    }
    partial void OnPerTaskRateLimitBytesPerSecondChanged(long value) => MarkPresetModified();
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
}

/// <summary>文件冲突策略的界面选项；显示文案与持久化值明确分离。</summary>
public sealed record FileConflictPolicyOption(FileConflictPolicy Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}
