using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.PluginSdk;
using BiliDownloader.Constants;
using BiliDownloader.Messaging;
using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Download.Extras;
using BiliDownloader.Services.Persistence;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Naming;
using BiliDownloader.ViewModels.BiliDownloader;

namespace BiliDownloader.ViewModels;

/// <summary>
/// BiliDownloader Document ViewModel：负责子 VM 组合、持久化
/// </summary>
public class BiliDownloaderViewModel : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private static readonly IPluginLogger Log = PluginLog.For<BiliDownloaderViewModel>();

    private static int SourcePriority(SubtitleSourceType source) => source switch
    {
        SubtitleSourceType.Official => 0,
        SubtitleSourceType.Unknown => 1,
        SubtitleSourceType.AiGenerated => 2,
        _ => 3,
    };
    /// <summary>
    /// 本 Document 实例的唯一标识（持久化到 SaveData，跨重启不丢）
    /// </summary>
    public string DocumentId { get; private set; } = Guid.NewGuid().ToString("N");

    private readonly IBiliDownloaderEventBus _eventBus;
    private readonly List<IDisposable> _eventSubscriptions = [];
    private readonly IDownloadTaskRepository _taskRepository;
    private readonly IBiliDownloaderDocumentStateMapper _documentStateMapper;
    private readonly IDocumentLifetime _documentLifetime;
    // 这是 BiliDownloader Document 对象树的唯一关闭令牌源：宿主 ClosingToken 负责正常的
    // Dock 关闭路径，本地 CTS 则保证直接 new ViewModel 的兼容调用和单元测试也能通过
    // Dispose 触发同样的取消语义。该令牌只约束页面拥有的解析、探测、预检和 UI 投影；
    // 下载任务一旦提交到插件级 Coordinator，就不再由这棵 Document 对象树拥有。
    private readonly CancellationTokenSource _documentCts;
    private int _disposed;
    private readonly object _initializationLock = new();
    private readonly object _revisionLock = new();
    private Task? _initializationTask;
    private bool _isRestoringDocument;
    private long _contentRevision;
    private long _acceptedRevision;
    private string _title = "Bilibili下载";

    private static readonly HashSet<string> PersistedDownloadConfigProperties =
    [
        nameof(DownloadConfigViewModel.SelectedQuality),
        nameof(DownloadConfigViewModel.SelectedAudioQuality),
        nameof(DownloadConfigViewModel.UseGroupFolder),
        nameof(DownloadConfigViewModel.AddIndexToTitle),
        nameof(DownloadConfigViewModel.OutputDirectory),
        nameof(DownloadConfigViewModel.DownloadDanmaku),
        nameof(DownloadConfigViewModel.DownloadSubtitle),
        nameof(DownloadConfigViewModel.DownloadCover),
        nameof(DownloadConfigViewModel.SelectedConflictPolicy),
        nameof(DownloadConfigViewModel.SelectedPreset),
        nameof(DownloadConfigViewModel.VideoCodecPreference),
        nameof(DownloadConfigViewModel.OutputContainer),
        nameof(DownloadConfigViewModel.OutputMediaMode),
        nameof(DownloadConfigViewModel.VideoDynamicRangePreference),
        nameof(DownloadConfigViewModel.AudioFeaturePreference),
        nameof(DownloadConfigViewModel.SubtitleOptions),
        nameof(DownloadConfigViewModel.DanmakuOptions),
        nameof(DownloadConfigViewModel.PerTaskRateLimitBytesPerSecond),
    ];

    #region 子 ViewModel

    public LoginBarViewModel LoginBar { get; }
    public VideoParseViewModel VideoParse { get; }
    public DownloadSourceWorkflowViewModel SourceWorkflow { get; }
    public DownloadWorkspaceViewModel Workspace { get; }

    /// <inheritdoc />
    public bool IsDirty
    {
        get
        {
            lock (_revisionLock)
            {
                return _contentRevision != _acceptedRevision;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? IsDirtyChanged;

    /// <summary>
    /// 获取或设置当前标签标题。标题属于 Host 保存信封的展示数据，不进入插件内容 payload。
    /// </summary>
    public string Title
    {
        get => _title;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _title, value)) return;
            PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 兼容现有页面绑定的只读脏状态投影；真正的保存契约读取 <see cref="IsDirty"/>。
    /// </summary>
    public bool IsModified => IsDirty;

    /// <inheritdoc />
    public DocumentPresentationState Presentation => new(Title);

    /// <inheritdoc />
    public event EventHandler? PresentationChanged;

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
        set
        {
            if (SetProperty(ref _downloadInfo, value))
            {
                MarkDocumentModified();
            }
        }
    }

    public bool IsDownloadSettingsExpanded
    {
        get => Workspace.IsDownloadSettingsExpanded;
        set => Workspace.IsDownloadSettingsExpanded = value;
    }

    public string DownloadSettingsSummary => Workspace.DownloadSettingsSummary;

    #endregion

    public BiliDownloaderViewModel(
        IBiliDownloaderEventBus eventBus,
        IDownloadTaskRepository taskRepository,
        ISettingsRepository settingsRepository,
        BiliLoginStateService loginStateService,
        BiliLoginService loginService,
        IContentSourceProviderRegistry providerRegistry,
        IBiliMediaProbe mediaProbe,
        IBiliCredentialProvider credentialProvider,
        IFfmpegRuntimeLocator ffmpegService,
        IBiliDownloaderDocumentStateMapper documentStateMapper,
        IDocumentLifetime documentLifetime,
        IPresetRepository? presetRepository = null,
        IDownloadSubmissionService? submissionService = null,
        IUserPromptService? promptService = null,
        ILoginDialogService? loginDialogService = null,
        IFfmpegPackageInstaller? ffmpegInstaller = null,
        IIncrementalComparisonService? incrementalComparisonService = null,
        ISubtitleCatalogService? subtitleCatalogService = null)
    {
        _eventBus = eventBus;
        _taskRepository = taskRepository;
        _documentStateMapper = documentStateMapper ?? throw new ArgumentNullException(nameof(documentStateMapper));
        _documentLifetime = documentLifetime ?? throw new ArgumentNullException(nameof(documentLifetime));
        _documentCts = CancellationTokenSource.CreateLinkedTokenSource(
            documentLifetime.ClosingToken);

        // 初始化子 ViewModel（通过回调通信）
        LoginBar = loginDialogService is null
            ? new LoginBarViewModel(loginStateService, loginService, _documentCts.Token)
            : new LoginBarViewModel(loginStateService, loginDialogService, _documentCts.Token);

        VideoParse = new VideoParseViewModel(
            providerRegistry,
            mediaProbe,
            credentialProvider,
            onParsed: HandleParseResult,
            isLoggedInCheck: () => LoginBar.IsLoggedIn,
            documentToken: _documentCts.Token);
        // G5: 命名模板子 VM
        var namingTemplate = new NamingTemplateViewModel();
        VideoListViewModel? videoListForSubtitleDiscovery = null;
        var downloadConfig = new DownloadConfigViewModel(
            settingsRepository,
            presetRepository,
            () => namingTemplate.Template,
            subtitleDiscovery: async cancellationToken =>
            {
                if (subtitleCatalogService is null)
                    throw new InvalidOperationException("字幕目录服务未注册。");
                var selected = videoListForSubtitleDiscovery?.VideoItems
                    .Where(static item => item.IsSelected).ToArray() ?? Array.Empty<BiliVideoItem>();
                if (selected.Length == 0) return Array.Empty<SubtitleLanguageAvailability>();

                using var concurrency = new SemaphoreSlim(4, 4);
                var sync = new object();
                var availability = new Dictionary<string, (string Name, SubtitleSourceType Source, int Count)>(
                    StringComparer.OrdinalIgnoreCase);
                var successfulItems = 0;
                await Task.WhenAll(selected.Select(async item =>
                {
                    await concurrency.WaitAsync(cancellationToken);
                    try
                    {
                        var tracks = await subtitleCatalogService.GetPreferredTracksAsync(
                            item.Aid, item.Cid, credentialProvider.GetCookieHeader(), cancellationToken);
                        lock (sync)
                        {
                            successfulItems++;
                            foreach (var track in tracks)
                            {
                                if (availability.TryGetValue(track.StableLanguageKey, out var current))
                                {
                                    var preferred = track.SourcePriority < SourcePriority(current.Source)
                                        ? (track.DisplayName, track.SourceType) : (current.Name, current.Source);
                                    availability[track.StableLanguageKey] = (preferred.Item1, preferred.Item2, current.Count + 1);
                                }
                                else
                                {
                                    availability[track.StableLanguageKey] = (track.DisplayName, track.SourceType, 1);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Log.Warn($"字幕目录检测跳过媒体 {item.ItemId}：{SensitiveDataSanitizer.Sanitize(ex.Message)}");
                    }
                    finally { concurrency.Release(); }
                }));
                if (successfulItems == 0) throw new InvalidOperationException("所有所选媒体的字幕目录检测均失败。");
                return availability
                    .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => new SubtitleLanguageAvailability(
                        pair.Key, pair.Value.Name, pair.Value.Source, pair.Value.Count, selected.Length))
                    .ToArray();
            },
            promptService: promptService,
            documentToken: _documentCts.Token);
        downloadConfig.PresetApplied += preset =>
        {
            if (!IsClosing) namingTemplate.Template = preset.NamingTemplate;
        };

        var favoriteDiscovery = providerRegistry.Providers
            .OfType<IFavoriteSourceDiscoveryService>()
            .FirstOrDefault() ?? new UnavailableFavoriteSourceDiscoveryService();
        SourceWorkflow = new DownloadSourceWorkflowViewModel(
            VideoParse,
            providerRegistry,
            favoriteDiscovery,
            new VideoParseResultFactory(mediaProbe, credentialProvider),
            HandleParseResult,
            incrementalComparisonService,
            downloadConfig.CaptureRenditionSpecification,
            _documentCts.Token);

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
                VideoCodecPreference = DownloadConfig.VideoCodecPreference,
                OutputContainer = DownloadConfig.OutputContainer,
                OutputMediaMode = DownloadConfig.OutputMediaMode,
                VideoDynamicRangePreference = DownloadConfig.VideoDynamicRangePreference,
                AudioFeaturePreference = DownloadConfig.AudioFeaturePreference,
                SubtitleOptions = DownloadConfig.SubtitleOptions.Canonicalize(),
                DanmakuOptions = DownloadConfig.DanmakuOptions.Canonicalize(),
                PerTaskRateLimitBytesPerSecond = DownloadConfig.PerTaskRateLimitBytesPerSecond,
                IsHighSpecificationSelectionValid = DownloadConfig.IsHighSpecificationSelectionValid,
                IncrementalExpectation = SourceWorkflow.CreateSubmissionExpectation(),
            },
            eventBus: _eventBus,
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
                        await LoginBar.EnsureLoggedInAsync(_documentCts.Token);
                        AppendLog(LoginBar.IsLoggedIn ? "登录已恢复，请重新提交。" : "登录未完成。");
                        break;
                    case "ffmpeg":
                        if (ffmpegInstaller is null)
                        {
                            AppendLog("请在调度器工具中选择自定义 ffmpeg 路径。");
                            break;
                        }
                        var installation = await ffmpegInstaller.InstallOrRepairAsync(_documentCts.Token);
                        AppendLog(installation.Message);
                        break;
                    case "directory":
                    case "disk":
                        var directory = promptService is null
                            ? null
                            : await promptService.PickFolderAsync(
                                "选择新的输出目录",
                                DownloadConfig.OutputDirectory,
                                _documentCts.Token);
                        if (!string.IsNullOrWhiteSpace(directory))
                        {
                            DownloadConfig.OutputDirectory = directory;
                            AppendLog("输出目录已更新，请重新提交。");
                        }
                        break;
                    case "stale-comparison":
                        await SourceWorkflow.RefreshComparisonFromCacheAsync(_documentCts.Token);
                        break;
                }
            },
            documentToken: _documentCts.Token);
        videoListForSubtitleDiscovery = videoList;
        workspace = new DownloadWorkspaceViewModel(
            downloadConfig,
            namingTemplate,
            videoList,
            new MediaCapabilityInspectionService(mediaProbe, credentialProvider),
            _documentCts.Token);
        Workspace = workspace;
        Workspace.PropertyChanged += (_, args) =>
        {
            if (IsClosing) return;
            if (args.PropertyName == nameof(DownloadWorkspaceViewModel.IsParsed))
                OnPropertyChanged(nameof(IsParsed));
            if (args.PropertyName == nameof(DownloadWorkspaceViewModel.IsDownloadSettingsExpanded))
                OnPropertyChanged(nameof(IsDownloadSettingsExpanded));
            if (args.PropertyName == nameof(DownloadWorkspaceViewModel.DownloadSettingsSummary))
                OnPropertyChanged(nameof(DownloadSettingsSummary));
        };

        VideoParse.PropertyChanged += (_, args) =>
        {
            if (IsClosing) return;
            if (args.PropertyName == nameof(VideoParseViewModel.Url)) MarkDocumentModified();
        };
        SourceWorkflow.PersistentStateChanged += MarkDocumentModified;
        SourceWorkflow.IncrementalItemsAccepted += (items, expectation) =>
        {
            if (IsClosing) return;
            var rendition = DownloadConfig.CaptureRenditionSpecification();
            if (rendition is null || items.Count == 0) return;
            for (var index = 0; index < items.Count; index++) items[index].Index = index + 1;
            var collection = new BiliVideoCollection
            {
                SeriesTitle = SourceWorkflow.Browser.CurrentDescriptor?.DisplayName ?? "增量更新",
                Items = items.ToList(),
            };
            HandleParseResult(new VideoParseResult
            {
                Collection = collection,
                VideoItems = items.ToList(),
                QualityOptions =
                [
                    new BiliQualityOption
                    {
                        QualityId = rendition.VideoQualityId,
                        DisplayName = $"Q{rendition.VideoQualityId}",
                    },
                ],
                SelectedQuality = new BiliQualityOption
                {
                    QualityId = rendition.VideoQualityId,
                    DisplayName = $"Q{rendition.VideoQualityId}",
                },
                AudioQualityOptions = rendition.AudioQualityId > 0
                    ? [new BiliQualityOption { QualityId = rendition.AudioQualityId, DisplayName = $"音频 {rendition.AudioQualityId}" }]
                    : [],
                SelectedAudioQuality = rendition.AudioQualityId > 0
                    ? new BiliQualityOption { QualityId = rendition.AudioQualityId, DisplayName = $"音频 {rendition.AudioQualityId}" }
                    : null,
                IsMultiVideo = items.Count > 1,
                TitlesText = string.Join(Environment.NewLine, items.Select(item => item.Title)),
            });
        };
        DownloadConfig.PropertyChanged += (_, args) =>
        {
            if (IsClosing) return;
            if (args.PropertyName is not null && PersistedDownloadConfigProperties.Contains(args.PropertyName))
            {
                MarkDocumentModified();
                if (args.PropertyName is nameof(DownloadConfigViewModel.SelectedQuality) or
                    nameof(DownloadConfigViewModel.SelectedAudioQuality) or
                    nameof(DownloadConfigViewModel.VideoCodecPreference) or
                    nameof(DownloadConfigViewModel.OutputContainer) or
                    nameof(DownloadConfigViewModel.OutputMediaMode))
                    SourceWorkflow.MarkOutputIdentityChanged();
            }
        };
        NamingTemplate.PropertyChanged += (_, args) =>
        {
            if (IsClosing) return;
            if (args.PropertyName == nameof(NamingTemplateViewModel.Template)) MarkDocumentModified();
        };

        RegisterEvents();
    }

    /// <summary>创建意图只决定首次展示入口；保存与下载契约始终属于同一个 Document。</summary>
    private void ApplyCreationIntent(CreationIntentId? intentId) =>
        SourceWorkflow.SetInitialMode(
            intentId == BiliDownloaderContributionIds.PersonalSourceIntent
                ? DownloadCreationMode.PersonalSource
                : DownloadCreationMode.QuickUrl);

    /// <inheritdoc />
    public ValueTask InitializeAsync(
        DocumentActivation activation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        lock (_initializationLock)
        {
            _initializationTask ??= InitializeCoreAsync(activation, cancellationToken);
            return new ValueTask(_initializationTask);
        }
    }

    private async Task InitializeCoreAsync(
        DocumentActivation activation,
        CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _documentCts.Token);
        var effectiveToken = linkedCancellation.Token;
        effectiveToken.ThrowIfCancellationRequested();

        // 类型分支先把互斥输入解包为局部事实。恢复必须在独立 DTO 中完成 JSON 解码、安全校验和
        // 规范化；任何失败都发生在修改当前模型之前。Creation Intent 则只可能来自 New 分支。
        (BiliDownloaderRestoredState? Restored, CreationIntentId? CreationIntentId) input =
            activation switch
            {
                NewDocumentActivation created => (null, created.CreationIntentId),
                RestoreDocumentActivation restore =>
                    (_documentStateMapper.Restore(restore.RestoredContent), null),
                _ => throw new NotSupportedException("BiliDownloader 收到未知 Document 激活类型。"),
            };
        if (input.CreationIntentId is not null
            && input.CreationIntentId != BiliDownloaderContributionIds.QuickUrlIntent
            && input.CreationIntentId != BiliDownloaderContributionIds.PersonalSourceIntent)
        {
            throw new ArgumentException("未知的 BiliDownloader 创建意图。", nameof(activation));
        }

        Title = string.IsNullOrWhiteSpace(activation.Title) ? "Bilibili下载" : activation.Title;
        _isRestoringDocument = true;
        try
        {
            if (input.Restored is not null)
            {
                ApplyRestoredState(input.Restored);
                OnPropertyChanged(nameof(DocumentId));
            }
            else
            {
                ApplyCreationIntent(input.CreationIntentId);
            }

            await DownloadConfig.InitializeAsync();
            effectiveToken.ThrowIfCancellationRequested();
            await RecoverTasksFromStoreAsync(effectiveToken);
            ResetRevisionState();
        }
        finally
        {
            _isRestoringDocument = false;
        }
    }

    #region 事件总线注册

    private void RegisterEvents()
    {
        // 注册发生在完整对象树创建之后。任何失败都表示宿主组合或生命周期已损坏，
        // 必须向构造调用方暴露，不能留下只注册了一部分处理器的半可用 Document。
        try
        {
            _eventSubscriptions.Add(_eventBus.Subscribe<LoginStateChangedMessage>(msg =>
            {
                if (IsClosing) return;
                LoginBar.IsLoggedIn = msg.IsLoggedIn;
                LoginBar.UserName = LoginBarViewModel.GetDisplayName(msg.IsLoggedIn, msg.UserName);
                LoginBar.StatusMessage = msg.StatusMessage;
                Workspace.InvalidateMediaCapabilities();
            }));

            _eventSubscriptions.Add(_eventBus.Subscribe<DownloadTaskProgressMessage>(msg =>
            {
                if (IsClosing || msg.TargetDocumentId != DocumentId) return;
                VideoList.UpdateItemProgress(msg);
            }));

            _eventSubscriptions.Add(_eventBus.Subscribe<DownloadTaskDeletedMessage>(msg =>
            {
                if (IsClosing || msg.TargetDocumentId != DocumentId) return;
                VideoList.RemoveItem(msg.TaskId);
            }));

            _eventSubscriptions.Add(_eventBus.Subscribe<DownloadTaskStatusChangedMessage>(msg =>
            {
                if (IsClosing || msg.TargetDocumentId != DocumentId) return;
                VideoList.UpdateItemStatus(msg);
            }));
        }
        catch
        {
            // 注册多条事件必须全部成功或全部回滚。异常继续向构造调用方传播，
            // 已成功登记的强引用订阅则先逆序释放，避免创建失败后泄漏 Document。
            ReleaseEventSubscriptions();
            throw;
        }
    }

    #endregion

    #region 日志追加

    /// <summary>
    /// 追加日志行（保留历史记录）
    /// </summary>
    private void AppendLog(string message)
    {
        if (IsClosing) return;
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
        if (IsClosing) return;
        Workspace.ApplyParseResult(result);
        MarkDocumentModified();

        // 同步解析状态到 VideoParse 子 VM
        VideoParse.IsParsed = true;
    }

    private void MarkDocumentModified()
    {
        if (_isRestoringDocument || IsClosing) return;

        var dirtyChanged = false;
        lock (_revisionLock)
        {
            var wasDirty = _contentRevision != _acceptedRevision;
            _contentRevision = checked(_contentRevision + 1);
            dirtyChanged = !wasDirty;
        }

        if (dirtyChanged)
        {
            RaiseDirtyChanged();
        }
    }

    #endregion

    /// <summary>
    /// 在用户明确点击登录入口后加载并校验登录状态。
    /// 本方法不得由构造函数或视觉树附加回调自动调用，否则打开 Document 就可能产生远端请求。
    /// </summary>
    public async Task EnsureLoggedInAsync()
    {
        await LoginBar.EnsureLoggedInAsync(_documentCts.Token);
        Workspace.InvalidateMediaCapabilities();
    }

    /// <summary>
    /// 从 SQLite 恢复本 Document 的未完成任务状态（由 View 首次加载时调用）
    /// </summary>
    public Task RecoverTasksFromStoreAsync() => RecoverTasksFromStoreAsync(_documentCts.Token);

    private async Task RecoverTasksFromStoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _taskRepository.InitAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var records = await _taskRepository.GetByDocumentIdAsync(DocumentId);
            cancellationToken.ThrowIfCancellationRequested();

            int idx = VideoList.Count + 1;
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
        // 本地仓储损坏仍降级为 Document 内可见状态；关闭或 Host 取消则不进入此分支，
        // OperationCanceledException 会自然向初始化调用方传播，使候选 Scope 不被发布。
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
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
    /// 创建保存数据（Document V3 格式）。
    /// <para>
    /// 插件只负责采集业务字段并生成 V3 快照；方法不更新路径、标题或脏状态，
    /// 这些提交动作由宿主在主文件写入成功后统一完成。
    /// </para>
    /// </summary>
    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documentLifetime.ClosingToken.ThrowIfCancellationRequested();
            var revisionBeforeCapture = ReadCurrentRevision();
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
                DownloadConfig.SelectedConflictPolicy.Value,
                DownloadConfig.VideoCodecPreference,
                DownloadConfig.OutputContainer,
                DownloadConfig.OutputMediaMode,
                DownloadConfig.VideoDynamicRangePreference,
                DownloadConfig.AudioFeaturePreference,
                DownloadConfig.SubtitleOptions,
                DownloadConfig.DanmakuOptions,
                DownloadConfig.PerTaskRateLimitBytesPerSecond);
            var content = _documentStateMapper.Create(
                DocumentId,
                VideoParse.Url,
                _downloadInfo,
                configuration,
                NamingTemplate.Template,
                SourceWorkflow.CapturePersistentState());
            var revisionAfterCapture = ReadCurrentRevision();
            if (revisionBeforeCapture == revisionAfterCapture)
            {
                return ValueTask.FromResult(
                    new DocumentSaveSnapshot(revisionAfterCapture, content));
            }

            // BiliDownloader 的对象图较大，子模型通知可能在捕获 DTO 时到达。前后 Revision
            // 不一致时直接重建 DTO，比给全部子模型增加共享锁更朴素，也不扩大它们的职责。
        }
    }

    private void ApplyRestoredState(BiliDownloaderRestoredState restored)
    {
        var data = restored.Data;
        VideoParse.Url = data.Url;
        if (!string.IsNullOrWhiteSpace(data.DocumentId)) DocumentId = data.DocumentId;
        DownloadInfo = data.DownloadInfo;
        NamingTemplate.Template = data.NamingTemplate;
        DownloadConfig.RestoreDocumentConfiguration(data);
        SourceWorkflow.RestorePersistentState(
            new BiliDownloaderDocumentSourceState(data.Source, data.Filters, data.Baseline),
            data.Url);
    }

    /// <summary>
    /// 接受宿主已经原子写入主文件的指定修订。生成保存快照时不能提前清除脏状态，
    /// 且保存期间产生的新 Revision 不能被旧确认覆盖。
    /// </summary>
    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var dirtyChanged = false;
        lock (_revisionLock)
        {
            if (_contentRevision != savedRevision.Value)
            {
                return;
            }

            dirtyChanged = _acceptedRevision != _contentRevision;
            _acceptedRevision = _contentRevision;
        }

        if (dirtyChanged)
        {
            RaiseDirtyChanged();
        }
    }

    private DocumentRevision ReadCurrentRevision()
    {
        lock (_revisionLock)
        {
            return new DocumentRevision(_contentRevision);
        }
    }

    private void ResetRevisionState()
    {
        var dirtyChanged = false;
        lock (_revisionLock)
        {
            dirtyChanged = _contentRevision != _acceptedRevision;
            _acceptedRevision = _contentRevision;
        }

        if (dirtyChanged)
        {
            RaiseDirtyChanged();
        }
    }

    private void RaiseDirtyChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsModified));
        IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsClosing => Volatile.Read(ref _disposed) != 0
        || _documentLifetime.IsClosing
        || _documentCts.IsCancellationRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // 释放顺序刻意遵循“先失效外部入口，再广播取消，最后级联释放子对象”：
        // 先释放总线令牌，避免关闭过程中又收到后台进度；Document CTS 随后取消所有仍在
        // 等待的页面操作；子 ViewModel 最后负责解绑各自事件并释放局部 CTS。整个过程不等待
        // 异步任务退出，因此 Dock 可以立即关闭，同时迟到回调会被 IsClosing 门禁丢弃。
        ReleaseEventSubscriptions();
        _documentCts.Cancel();
        (SourceWorkflow as IDisposable)?.Dispose();
        (Workspace as IDisposable)?.Dispose();
        (LoginBar as IDisposable)?.Dispose();
        VideoParse.Dispose();
        _documentCts.Dispose();
    }

    private void ReleaseEventSubscriptions()
    {
        for (var index = _eventSubscriptions.Count - 1; index >= 0; index--)
        {
            _eventSubscriptions[index].Dispose();
        }

        _eventSubscriptions.Clear();
    }

    #endregion
}
