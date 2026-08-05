using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Save;
using BiliDownloader.Constants;
using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Persistence;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Naming;
using BiliDownloader.ViewModels.BiliDownloader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BiliDownloader.ViewModels;

/// <summary>
/// BiliDownloader Document ViewModel：负责子 VM 组合、持久化
/// </summary>
public class BiliDownloaderViewModel : Document, ISavableDocument
{
    private static readonly IPluginLogger Log = PluginLog.For<BiliDownloaderViewModel>();
    public string SaveDocumentTypeId => SaveDocumentTypeIdConstant.BiliDownloaderDocumentId;
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 本 Document 实例的唯一标识（持久化到 SaveData，跨重启不丢）
    /// </summary>
    public string DocumentId { get; private set; } = Guid.NewGuid().ToString("N");

    private readonly IMessengerService _messengerService;
    private readonly IDownloadTaskRepository _taskRepository;
    private readonly object _initializationLock = new();
    private Task? _initializationTask;

    #region 子 ViewModel

    public LoginBarViewModel LoginBar { get; }
    public VideoParseViewModel VideoParse { get; }
    public DownloadConfigViewModel DownloadConfig { get; }
    public VideoListViewModel VideoList { get; }

    /// <summary>G5: 命名模板子 VM（管理模板编辑、验证和预览）</summary>
    public NamingTemplateViewModel NamingTemplate { get; }

    #endregion

    #region 属性

    private BiliVideoCollection? _videoCollection;

    private bool _isParsed;
    public bool IsParsed
    {
        get => _isParsed;
        set => SetProperty(ref _isParsed, value);
    }

    private string _downloadInfo = "";
    public string DownloadInfo
    {
        get => _downloadInfo;
        set => SetProperty(ref _downloadInfo, value);
    }

    private bool _isDownloadSettingsExpanded;
    public bool IsDownloadSettingsExpanded
    {
        get => _isDownloadSettingsExpanded;
        set => SetProperty(ref _isDownloadSettingsExpanded, value);
    }

    /// <summary>折叠状态下展示的下载方案摘要。</summary>
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

    #endregion

    public BiliDownloaderViewModel(
        IMessengerService messengerService,
        IDownloadTaskRepository taskRepository,
        ISettingsRepository settingsRepository,
        BiliLoginStateService loginStateService,
        BiliLoginService loginService,
        IContentSourceProviderRegistry providerRegistry,
        IBiliMediaProbe mediaProbe,
        IBiliCredentialProvider credentialProvider,
        IFfmpegRuntimeLocator ffmpegService,
        IPresetRepository? presetRepository = null,
        IDownloadSubmissionService? submissionService = null,
        IUserPromptService? promptService = null,
        ILoginDialogService? loginDialogService = null,
        IFfmpegPackageInstaller? ffmpegInstaller = null)
    {
        _messengerService = messengerService;
        _taskRepository = taskRepository;

        // 初始化子 ViewModel（通过回调通信）
        LoginBar = loginDialogService is null
            ? new LoginBarViewModel(loginStateService, loginService)
            : new LoginBarViewModel(loginStateService, loginDialogService);

        VideoParse = new VideoParseViewModel(
            providerRegistry,
            mediaProbe,
            credentialProvider,
            onParsed: HandleParseResult,
            isLoggedInCheck: () => LoginBar.IsLoggedIn);

        // G5: 命名模板子 VM
        NamingTemplate = new NamingTemplateViewModel();
        DownloadConfig = new DownloadConfigViewModel(
            settingsRepository,
            presetRepository,
            () => NamingTemplate.Template);
        DownloadConfig.PresetApplied += preset => NamingTemplate.Template = preset.NamingTemplate;
        DownloadConfig.PropertyChanged += OnDownloadConfigPropertyChanged;
        NamingTemplate.PropertyChanged += OnNamingTemplatePropertyChanged;

        VideoList = new VideoListViewModel(
            getSubmitContext: () => new SubmitContext
            {
                DocumentId = DocumentId,
                DocumentTitle = Title,
                QualityId = DownloadConfig.SelectedQuality?.QualityId ?? 0,
                AudioQualityId = DownloadConfig.SelectedAudioQuality?.QualityId ?? 0,
                OutputDirectory = DownloadConfig.OutputDirectory,
                UseGroupFolder = DownloadConfig.UseGroupFolder,
                AddIndexToTitle = DownloadConfig.AddIndexToTitle,
                SeriesTitle = _videoCollection?.SeriesTitle ?? "下载",
                DownloadDanmaku = DownloadConfig.DownloadDanmaku,
                DownloadSubtitle = DownloadConfig.DownloadSubtitle,
                DownloadCover = DownloadConfig.DownloadCover,
                CoverUrl = _videoCollection?.Cover ?? "",
                // G5: 命名模板和上下文变量
                NamingTemplate = NamingTemplate.Template,
                UpName = _videoCollection?.UpName ?? "",
                PublishDate = _videoCollection?.PublishDate,
                IsNamingValid = NamingTemplate.IsValid,
                NamingValidationError = NamingTemplate.ValidationError ?? "",
                ConflictPolicy = DownloadConfig.SelectedConflictPolicy.Value,
            },
            messengerService: _messengerService,
            onStatusMessage: msg => AppendLog(msg),
            ffmpegService: ffmpegService,
            onConfigurationBlocked: ExpandDownloadSettings,
            submissionService: submissionService,
            promptService: promptService,
            onPreflightAction: async code =>
            {
                switch (code)
                {
                    case "login":
                        await LoginBar.EnsureLoggedInAsync();
                        AppendLog(LoginBar.IsLoggedIn ? "登录已恢复，请重新提交。" : "登录未完成。");
                        break;
                    case "ffmpeg":
                        if (ffmpegInstaller is null)
                        {
                            AppendLog("请在调度器工具中选择自定义 ffmpeg 路径。");
                            break;
                        }
                        var installation = await ffmpegInstaller.InstallOrRepairAsync();
                        AppendLog(installation.Message);
                        break;
                    case "directory":
                    case "disk":
                        var directory = promptService is null
                            ? null
                            : await promptService.PickFolderAsync(
                                "选择新的输出目录", DownloadConfig.OutputDirectory);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            DownloadConfig.OutputDirectory = directory;
                            AppendLog("输出目录已更新，请重新提交。");
                        }
                        break;
                }
            });
        VideoList.SelectionOrTitleChanged += RefreshNamingPreview;

        RegisterMessengers();
    }

    /// <summary>
    /// P0 构造兼容入口。内部仍组装统一 Provider，避免旧调用方绕过 P1-G0 契约。
    /// </summary>
    internal BiliDownloaderViewModel(
        IMessengerService messengerService,
        IDownloadTaskRepository taskRepository,
        ISettingsRepository settingsRepository,
        BiliLoginStateService loginStateService,
        BiliLoginService loginService,
        BiliApiService apiService,
        IBiliCredentialProvider credentialProvider,
        IFfmpegRuntimeLocator ffmpegService,
        IPresetRepository? presetRepository = null,
        IDownloadSubmissionService? submissionService = null,
        IUserPromptService? promptService = null,
        ILoginDialogService? loginDialogService = null,
        IFfmpegPackageInstaller? ffmpegInstaller = null)
        : this(
            messengerService,
            taskRepository,
            settingsRepository,
            loginStateService,
            loginService,
            new ContentSourceProviderRegistry(
                [new DirectLinkProvider(apiService, credentialProvider)]),
            apiService,
            credentialProvider,
            ffmpegService,
            presetRepository,
            submissionService,
            promptService,
            loginDialogService,
            ffmpegInstaller)
    {
    }

    public Task InitializeAsync()
    {
        lock (_initializationLock)
            return _initializationTask ??= InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        await DownloadConfig.InitializeAsync();
        await RecoverTasksFromStoreAsync();
    }

    #region 消息总线注册

    private void RegisterMessengers()
    {
        try
        {
            // 登录状态变更 -> 同步到 LoginBar 子 VM
            _messengerService.Register<BiliDownloaderViewModel, LoginStateChangedMessage>(
                this, (vm, msg) =>
                {
                    vm.LoginBar.IsLoggedIn = msg.IsLoggedIn;
                    vm.LoginBar.UserName = LoginBarViewModel.GetDisplayName(
                        msg.IsLoggedIn,
                        msg.UserName);
                    vm.LoginBar.StatusMessage = msg.StatusMessage;
                });

            // 下载进度回传（按 DocumentId 过滤）-> 委托给 VideoList
            _messengerService.Register<BiliDownloaderViewModel, DownloadTaskProgressMessage>(
                this, (vm, msg) =>
                {
                    if (msg.TargetDocumentId != vm.DocumentId) return;
                    vm.VideoList.UpdateItemProgress(msg);
                });

            // 任务被删除通知 -> 委托给 VideoList
            _messengerService.Register<BiliDownloaderViewModel, DownloadTaskDeletedMessage>(
                this, (vm, msg) =>
                {
                    if (msg.TargetDocumentId != vm.DocumentId) return;
                    vm.VideoList.RemoveItem(msg.TaskId);
                });

            // 调度器自主状态变更通知 -> 委托给 VideoList
            _messengerService.Register<BiliDownloaderViewModel, DownloadTaskStatusChangedMessage>(
                this, (vm, msg) =>
                {
                    if (msg.TargetDocumentId != vm.DocumentId) return;
                    vm.VideoList.UpdateItemStatus(msg);
                });
        }
        catch
        {
            // 忽略
        }
    }

    #endregion

    #region 日志追加

    /// <summary>
    /// 追加日志行（保留历史记录）
    /// </summary>
    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{timestamp}] {message}";
        DownloadInfo = string.IsNullOrEmpty(DownloadInfo) ? line : DownloadInfo + Environment.NewLine + line;
    }

    #endregion

    #region 子 VM 回调处理

    /// <summary>
    /// 解析成功后的回调：填充 VideoList、分发清晰度到 DownloadConfig
    /// </summary>
    private void HandleParseResult(VideoParseResult result)
    {
        _videoCollection = result.Collection;

        // 填充视频列表 + 初始化重命名面板
        VideoList.SetItems(result.VideoItems);

        // 分发清晰度到 DownloadConfig
        DownloadConfig.PopulateQualities(
            result.QualityOptions,
            result.SelectedQuality,
            result.AudioQualityOptions,
            result.SelectedAudioQuality,
            result.IsMultiVideo);
        RefreshNamingPreview();

        IsParsed = true;
        IsModified = true;

        // 同步解析状态到 VideoParse 子 VM
        VideoParse.IsParsed = true;
    }

    private void RefreshNamingPreview()
    {
        var contexts = VideoList.VideoItems
            .Where(item => item.IsSelected)
            .Select(item => new NamingContext
            {
                Title = item.Title,
                Index = item.Index,
                Bvid = item.Bvid,
                UpName = _videoCollection?.UpName ?? "",
                PublishDate = _videoCollection?.PublishDate,
                SeriesTitle = _videoCollection?.SeriesTitle ?? "",
            })
            .ToList();
        NamingTemplate.UpdatePreview(contexts);
    }

    private void OnDownloadConfigPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(DownloadSettingsSummary));

        if (DownloadConfig.IsRestoredPresetUnavailable
            || !string.IsNullOrWhiteSpace(DownloadConfig.QualityRestoreNotice))
        {
            ExpandDownloadSettings();
        }
    }

    private void OnNamingTemplatePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(DownloadSettingsSummary));
        if (!NamingTemplate.IsValid)
            ExpandDownloadSettings();
    }

    private void ExpandDownloadSettings() => IsDownloadSettingsExpanded = true;

    private static string GetOutputDirectoryLabel(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return "默认目录";

        var trimmed = Path.TrimEndingDirectorySeparator(outputDirectory);
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? trimmed : leaf;
    }

    #endregion

    /// <summary>
    /// 在用户明确点击登录入口后加载并校验登录状态。
    /// 本方法不得由构造函数或视觉树附加回调自动调用，否则打开 Document 就可能产生远端请求。
    /// </summary>
    public async Task EnsureLoggedInAsync()
    {
        await LoginBar.EnsureLoggedInAsync();
    }

    /// <summary>
    /// 从 SQLite 恢复本 Document 的未完成任务状态（由 View 首次加载时调用）
    /// </summary>
    public async Task RecoverTasksFromStoreAsync()
    {
        try
        {
            await _taskRepository.InitAsync();
            var records = await _taskRepository.GetByDocumentIdAsync(DocumentId);

            int idx = VideoList.Count + 1;
            foreach (var record in records)
            {
                var item = new BiliVideoItem
                {
                    Index = idx++,
                    ItemId = record.TaskId,
                    OriginalTitle = record.ItemTitle,
                    Title = record.ItemTitle,
                    Aid = record.Aid,
                    Bvid = record.Bvid,
                    Cid = record.Cid,
                    IsSelected = false,
                    Status = MapStatusToDisplay(record.Status),
                    StageText = MapStageToDisplay(record.Status),
                    Progress = record.Progress,
                    VideoProgress = record.VideoProgress,
                    AudioProgress = record.AudioProgress,
                    MergeProgress = record.MergeProgress,
                    SpeedText = record.SpeedText,
                };
                VideoList.AddRecoveredItem(item);
            }
        }
        catch (Exception ex)
        {
            DownloadInfo = $"恢复任务状态失败: {ex.Message}";
        }
    }

    #region 辅助方法

    private static string MapStatusToDisplay(string status)
        => DownloadTaskStatusMapper.ToDisplayText(DownloadTaskStatusMapper.FromStorageString(status));

    private static string MapStageToDisplay(string status)
        => DownloadTaskStatusMapper.ToDisplayText(DownloadTaskStatusMapper.FromStorageString(status));

    #endregion

    #region 持久化

    /// <summary>
    /// 创建保存数据（Document V2 格式）。
    /// <para>
    /// 设计思考（G5）：使用强类型 DocumentSaveDataV2 替代 V1 的匿名对象，
    /// 提高可读性和版本演进能力。PluginMetadata.Version = "2.0" 供加载时判别版本。
    /// </para>
    /// </summary>
    public DocumentSaveData CreateSaveDocumentMetaData(string filePath)
    {
        var saveDataObject = new DocumentSaveDataV2
        {
            DocumentId = DocumentId,
            Url = VideoParse.Url,
            DownloadInfo = _downloadInfo,
            OutputDirectory = DownloadConfig.OutputDirectory,
            UseGroupFolder = DownloadConfig.UseGroupFolder,
            AddIndexToTitle = DownloadConfig.AddIndexToTitle,
            // V2 新增字段
            PresetId = DownloadConfig.SelectedPreset?.Id ?? BuiltInPresets.CompatId,
            NamingTemplate = NamingTemplate.Template,
            QualityId = DownloadConfig.SelectedQuality?.QualityId ?? 0,
            AudioQualityId = DownloadConfig.SelectedAudioQuality?.QualityId ?? 0,
            DownloadDanmaku = DownloadConfig.DownloadDanmaku,
            DownloadSubtitle = DownloadConfig.DownloadSubtitle,
            DownloadCover = DownloadConfig.DownloadCover,
            ConflictPolicy = DownloadConfig.SelectedConflictPolicy.Value,
        };

        var saveData = DocumentSaveCodec.EncodeV2(SaveDocumentTypeId, Title, saveDataObject);

        IsModified = false;
        return saveData;
    }

    /// <summary>
    /// 从保存数据加载 Document（支持 V1 和 V2 版本）。
    /// <para>
    /// 设计思考（G5）：
    /// - V1 路径保持 JObject 逐字段读取不变，仅追加默认值补齐，确保不回归。
    /// - V2 路径反序列化 DocumentSaveDataV2，完整恢复所有配置。
    /// - 未知版本宽容读取已知字段 + 日志警告（向前兼容，不崩溃）。
    /// - V1 → V2 语义迁移：AddIndexToTitle=true → "{index}.{title}"，false → "{title}"。
    /// </para>
    /// </summary>
    public void LoadDocumentByMetaData(DocumentSaveData saveData)
    {
        try
        {
            if (saveData == null) return;

            var decoded = DocumentSaveCodec.Decode(saveData);
            if (decoded.MajorVersion == 2)
            {
                LoadV2(decoded.Content);
            }
            else if (decoded.MajorVersion == 1)
            {
                LoadV1(decoded.Content);
            }
            else
            {
                Log.Error("未知的 Document 主版本，仅恢复安全公共字段。", null);
                LoadSafeCommonFields(decoded.Content);
            }

            OnPropertyChanged(nameof(DocumentId));
        }
        catch (Exception ex)
        {
            Log.Error("加载文档失败。", ex);
        }
    }

    /// <summary>
    /// V1 加载逻辑（保持原有行为 + 补齐默认值）。
    /// </summary>
    private void LoadV1(string content)
    {
        var data = JsonConvert.DeserializeObject<JObject>(content);
        if (data == null) return;

        var url = data["Url"]?.ToString() ?? "";
        var downloadInfo = data["DownloadInfo"]?.ToString() ?? "";
        var outputDirectory = data["OutputDirectory"]?.ToString() ?? DownloadConfig.OutputDirectory;

        VideoParse.Url = url;
        DownloadInfo = downloadInfo;
        // 恢复 UseGroupFolder
        var useGroupFolderVal = data["UseGroupFolder"];
        var useGroupFolder = useGroupFolderVal != null
            && useGroupFolderVal.Type != JTokenType.Null
            && (bool)useGroupFolderVal;

        // 恢复 AddIndexToTitle
        var addIndexVal = data["AddIndexToTitle"];
        var addIndex = true;
        if (addIndexVal != null && addIndexVal.Type != JTokenType.Null)
        {
            addIndex = (bool)addIndexVal;
        }

        // 恢复 DocumentId
        var savedDocId = data["DocumentId"]?.ToString();
        if (!string.IsNullOrEmpty(savedDocId))
            DocumentId = savedDocId;

        // G5: V1 → V2 语义迁移：根据 AddIndexToTitle 生成命名模板
        NamingTemplate.Template = addIndex ? "{index}.{title}" : "{title}";
        DownloadConfig.RestoreDocumentConfiguration(new DocumentSaveDataV2
        {
            OutputDirectory = outputDirectory,
            UseGroupFolder = useGroupFolder,
            AddIndexToTitle = addIndex,
            NamingTemplate = NamingTemplate.Template,
        });
    }

    /// <summary>
    /// V2 加载逻辑（完整恢复所有配置）。
    /// </summary>
    private void LoadV2(string content)
    {
        var data = JsonConvert.DeserializeObject<DocumentSaveDataV2>(content);
        if (data == null) return;

        // V1 兼容字段
        VideoParse.Url = data.Url;
        DownloadInfo = data.DownloadInfo;
        DownloadConfig.RestoreDocumentConfiguration(data);

        if (!string.IsNullOrEmpty(data.DocumentId))
            DocumentId = data.DocumentId;

        // V2 新增字段
        NamingTemplate.Template = data.NamingTemplate;
    }

    private void LoadSafeCommonFields(string content)
    {
        var data = JsonConvert.DeserializeObject<JObject>(content);
        if (data is null) return;
        VideoParse.Url = data["Url"]?.ToString() ?? "";
        var documentId = data["DocumentId"]?.ToString();
        if (!string.IsNullOrWhiteSpace(documentId)) DocumentId = documentId;
        var output = data["OutputDirectory"]?.ToString();
        if (!string.IsNullOrWhiteSpace(output)) DownloadConfig.OutputDirectory = output;
    }

    #endregion
}
