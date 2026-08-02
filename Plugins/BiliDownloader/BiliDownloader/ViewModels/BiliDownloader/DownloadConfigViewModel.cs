using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;

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
    private readonly IPresetRepository? _presetRepository;

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

    #endregion

    public IRelayCommand SelectFolderCommand { get; }

    public DownloadConfigViewModel(ISettingsRepository settingsRepository, IPresetRepository? presetRepository = null)
    {
        _settingsRepository = settingsRepository;
        _presetRepository = presetRepository;
        SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync);
        ApplyPresetCommand = new RelayCommand(ApplySelectedPreset);
        SaveAsPresetCommand = new AsyncRelayCommand(SaveAsPresetAsync);
        _ = InitAsync();
    }

    /// <summary>
    /// 初始化：加载默认目录、预设列表、恢复最后使用的预设。
    /// 设计思考：合并原来的 InitDefaultOutputDirectoryAsync 和预设加载，
    /// 避免多次异步初始化竞争。
    /// </summary>
    private async Task InitAsync()
    {
        try
        {
            await _settingsRepository.InitAsync();

            // 恢复输出目录
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

            // 加载预设列表
            if (_presetRepository != null)
            {
                var presets = await _presetRepository.GetAllAsync();
                Presets.Clear();
                foreach (var p in presets)
                    Presets.Add(p);

                // 恢复最后使用的预设
                var lastPresetId = await _settingsRepository.GetSettingAsync("last_preset_id");
                if (!string.IsNullOrEmpty(lastPresetId))
                {
                    var lastPreset = presets.FirstOrDefault(p => p.Id == lastPresetId);
                    if (lastPreset != null)
                    {
                        SelectedPreset = lastPreset;
                        ApplyPreset(lastPreset);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("初始化下载配置失败。", ex);
            // 回退默认值
            if (string.IsNullOrEmpty(OutputDirectory))
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
        UseGroupFolder = isMultiVideo;

        // G5: 如果有待匹配的清晰度偏好，延迟匹配到实际可用选项
        if (!string.IsNullOrEmpty(_pendingQualityPreference) && QualityOptions.Count > 0)
        {
            SelectedQuality = MatchQualityByPreference(qualities, _pendingQualityPreference);
            _pendingQualityPreference = ""; // 匹配完成后清除
        }
        else
        {
            SelectedQuality = selectedQuality;
        }

        SelectedAudioQuality = selectedAudioQuality;
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
        UseGroupFolder = preset.UseGroupFolder;
        AddIndexToTitle = preset.AddIndexToTitle;
        DownloadDanmaku = preset.DownloadDanmaku;
        DownloadSubtitle = preset.DownloadSubtitle;
        DownloadCover = preset.DownloadCover;

        // 输出目录：预设指定则使用，否则保持当前默认
        if (!string.IsNullOrEmpty(preset.OutputDirectory))
            OutputDirectory = preset.OutputDirectory;

        // 清晰度偏好延迟匹配（解析完成后在 PopulateQualities 中生效）
        _pendingQualityPreference = preset.QualityPreference;

        // 如果清晰度选项已加载，立即匹配
        if (QualityOptions.Count > 0)
        {
            SelectedQuality = MatchQualityByPreference(QualityOptions.ToList(), preset.QualityPreference);
            _pendingQualityPreference = "";
        }
    }

    /// <summary>
    /// 从当前配置属性快照生成预设对象。
    /// </summary>
    public DownloadPreset CaptureCurrentAsPreset(string id, string name)
    {
        return new DownloadPreset
        {
            Id = id,
            Name = name,
            IsBuiltIn = false,
            QualityPreference = _pendingQualityPreference,
            AudioQualityId = SelectedAudioQuality?.QualityId ?? 0,
            UseGroupFolder = UseGroupFolder,
            AddIndexToTitle = AddIndexToTitle,
            DownloadDanmaku = DownloadDanmaku,
            DownloadSubtitle = DownloadSubtitle,
            DownloadCover = DownloadCover,
            NamingTemplate = "", // 由 NamingTemplateViewModel 提供
            OutputDirectory = OutputDirectory
        };
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
        if (_presetRepository == null) return;

        try
        {
            var preset = CaptureCurrentAsPreset(
                Guid.NewGuid().ToString("N"),
                $"自定义预设 {Presets.Count(p => !p.IsBuiltIn) + 1}");

            await _presetRepository.SaveAsync(preset);

            // 刷新预设列表
            var allPresets = await _presetRepository.GetAllAsync();
            Presets.Clear();
            foreach (var p in allPresets)
                Presets.Add(p);

            SelectedPreset = preset;
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
            }
        }
        catch (Exception ex)
        {
            Log.Error("选择文件夹失败。", ex);
        }
    }

    private async Task InitDefaultOutputDirectoryAsync()
    {
        try
        {
            await _settingsRepository.InitAsync();
            var savedDir = await _settingsRepository.GetSettingAsync("default_output_dir");
            if (!string.IsNullOrEmpty(savedDir))
            {
                OutputDirectory = savedDir;
                return;
            }
        }
        catch { /* 忽略 */ }

        // 回退默认值：程序根目录/视频下载
        var appDir = Path.GetDirectoryName(typeof(DownloadConfigViewModel).Assembly.Location) ?? "";
        OutputDirectory = Path.Combine(appDir, "视频下载");
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
}
