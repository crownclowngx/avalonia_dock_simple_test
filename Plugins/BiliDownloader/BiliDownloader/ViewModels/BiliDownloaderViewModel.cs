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
    private readonly BiliDownloaderDocumentStateMapper _documentStateMapper = new();
    private readonly object _initializationLock = new();
    private Task? _initializationTask;

    #region 子 ViewModel

    public LoginBarViewModel LoginBar { get; }
    public VideoParseViewModel VideoParse { get; }
    public DownloadSourceWorkflowViewModel SourceWorkflow { get; }
    public DownloadWorkspaceViewModel Workspace { get; }

    // 兼容既有调用方；新 View 统一从 Workspace 绑定，转发属性不再承载展示逻辑。
    public DownloadConfigViewModel DownloadConfig => Workspace.DownloadConfig;
    public VideoListViewModel VideoList => Workspace.VideoList;
    public NamingTemplateViewModel NamingTemplate => Workspace.NamingTemplate;

    #endregion

    #region 属性

    public bool IsParsed
    {
        get => Workspace.IsParsed;
        set => Workspace.IsParsed = value;
    }

    private string _downloadInfo = "";
    public string DownloadInfo
    {
        get => _downloadInfo;
        set => SetProperty(ref _downloadInfo, value);
    }

    public bool IsDownloadSettingsExpanded
    {
        get => Workspace.IsDownloadSettingsExpanded;
        set => Workspace.IsDownloadSettingsExpanded = value;
    }

    public string DownloadSettingsSummary => Workspace.DownloadSettingsSummary;

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
        var favoriteDiscovery = providerRegistry.Providers
            .OfType<IFavoriteSourceDiscoveryService>()
            .FirstOrDefault() ?? new UnavailableFavoriteSourceDiscoveryService();
        SourceWorkflow = new DownloadSourceWorkflowViewModel(
            VideoParse,
            providerRegistry,
            favoriteDiscovery,
            new VideoParseResultFactory(mediaProbe, credentialProvider),
            HandleParseResult);

        // G5: 命名模板子 VM
        var namingTemplate = new NamingTemplateViewModel();
        var downloadConfig = new DownloadConfigViewModel(
            settingsRepository,
            presetRepository,
            () => namingTemplate.Template);
        downloadConfig.PresetApplied += preset => namingTemplate.Template = preset.NamingTemplate;

        DownloadWorkspaceViewModel? workspace = null;
        var videoList = new VideoListViewModel(
            getSubmitContext: () => new SubmitContext
            {
                DocumentId = DocumentId,
                DocumentTitle = Title,
                QualityId = DownloadConfig.SelectedQuality?.QualityId ?? 0,
                AudioQualityId = DownloadConfig.SelectedAudioQuality?.QualityId ?? 0,
                OutputDirectory = DownloadConfig.OutputDirectory,
                UseGroupFolder = DownloadConfig.UseGroupFolder,
                AddIndexToTitle = DownloadConfig.AddIndexToTitle,
                SeriesTitle = workspace?.VideoCollection?.SeriesTitle ?? "下载",
                DownloadDanmaku = DownloadConfig.DownloadDanmaku,
                DownloadSubtitle = DownloadConfig.DownloadSubtitle,
                DownloadCover = DownloadConfig.DownloadCover,
                CoverUrl = workspace?.VideoCollection?.Cover ?? "",
                // G5: 命名模板和上下文变量
                NamingTemplate = NamingTemplate.Template,
                UpName = workspace?.VideoCollection?.UpName ?? "",
                PublishDate = workspace?.VideoCollection?.PublishDate,
                IsNamingValid = NamingTemplate.IsValid,
                NamingValidationError = NamingTemplate.ValidationError ?? "",
                ConflictPolicy = DownloadConfig.SelectedConflictPolicy.Value,
            },
            messengerService: _messengerService,
            onStatusMessage: msg => AppendLog(msg),
            ffmpegService: ffmpegService,
            onConfigurationBlocked: () => workspace?.ExpandSettings(),
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
        workspace = new DownloadWorkspaceViewModel(downloadConfig, namingTemplate, videoList);
        Workspace = workspace;
        Workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DownloadWorkspaceViewModel.IsParsed))
                OnPropertyChanged(nameof(IsParsed));
            if (args.PropertyName == nameof(DownloadWorkspaceViewModel.IsDownloadSettingsExpanded))
                OnPropertyChanged(nameof(IsDownloadSettingsExpanded));
            if (args.PropertyName == nameof(DownloadWorkspaceViewModel.DownloadSettingsSummary))
                OnPropertyChanged(nameof(DownloadSettingsSummary));
        };

        RegisterMessengers();
    }

    /// <summary>创建意图只决定首次展示入口；保存与下载契约始终属于同一个 Document。</summary>
    public void ApplyCreationIntent(string? intentId) =>
        SourceWorkflow.SetInitialMode(
            string.Equals(intentId, "personal-source", StringComparison.Ordinal)
                ? DownloadCreationMode.PersonalSource
                : DownloadCreationMode.QuickUrl);

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
        Workspace.ApplyParseResult(result);
        IsModified = true;

        // 同步解析状态到 VideoParse 子 VM
        VideoParse.IsParsed = true;
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
        var configuration = new DownloadConfigViewModelSnapshot(
            DownloadConfig.OutputDirectory,
            DownloadConfig.UseGroupFolder,
            DownloadConfig.AddIndexToTitle,
            DownloadConfig.SelectedPreset?.Id ?? BuiltInPresets.CompatId,
            DownloadConfig.SelectedQuality?.QualityId ?? 0,
            DownloadConfig.SelectedAudioQuality?.QualityId ?? 0,
            DownloadConfig.DownloadDanmaku,
            DownloadConfig.DownloadSubtitle,
            DownloadConfig.DownloadCover,
            DownloadConfig.SelectedConflictPolicy.Value);
        var saveData = _documentStateMapper.Create(
            Title, DocumentId, VideoParse.Url, _downloadInfo, configuration, NamingTemplate.Template);

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

            var restored = _documentStateMapper.Restore(saveData, DownloadConfig.OutputDirectory);
            ApplyRestoredState(restored);

            OnPropertyChanged(nameof(DocumentId));
        }
        catch (Exception ex)
        {
            Log.Error("加载文档失败。", ex);
        }
    }

    private void ApplyRestoredState(BiliDownloaderRestoredState restored)
    {
        var data = restored.Data;
        if (!restored.IsKnownVersion)
            Log.Error("未知的 Document 主版本，仅恢复安全公共字段。", null);
        VideoParse.Url = data.Url;
        if (!string.IsNullOrWhiteSpace(data.DocumentId)) DocumentId = data.DocumentId;
        if (!restored.RestoreFullConfiguration)
        {
            if (!string.IsNullOrWhiteSpace(data.OutputDirectory))
                DownloadConfig.OutputDirectory = data.OutputDirectory;
            return;
        }
        DownloadInfo = data.DownloadInfo;
        NamingTemplate.Template = data.NamingTemplate;
        DownloadConfig.RestoreDocumentConfiguration(data);
    }

    #endregion
}
