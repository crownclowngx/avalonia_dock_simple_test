using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Download.Extras;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.History;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;

namespace BiliDownloader.Plugin;

/// <summary>
/// BiliDownloader 选择接入宿主管理生命周期的模块入口。
/// <para>
/// 本类型只注册 BiliDownloader 自己的服务，不会扫描、替换或初始化其他插件。
/// 其他未声明 <see cref="IPluginModule"/> 的程序集仍按宿主原有无参策略流程运行。
/// </para>
/// </summary>
public sealed class BiliDownloaderPluginModule : IPluginModule
{
    public string PluginId => "BiliDownloader";

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IBiliDataPaths, BiliDataPaths>();
        services.AddSingleton<IBiliLocalStateInitializer, BiliLocalStateInitializer>();

        // SQLite 仓储在插件进程生命周期内保持唯一，确保 Tool、Document 和 Coordinator
        // 观察同一任务事实源，而不是各自创建数据库访问对象。
        services.AddSingleton<IDownloadTaskRepository, DownloadTaskStore>();
        services.AddSingleton<ITaskHistoryReadRepository>(provider =>
            (ITaskHistoryReadRepository)provider.GetRequiredService<IDownloadTaskRepository>());
        services.AddSingleton<ISettingsRepository, SettingsStore>();
        services.AddSingleton<IPresetRepository, PresetStore>(); // G5: 预设持久化
        services.AddSingleton<IDownloadPresetService, DownloadPresetService>();
        // Document 版本识别、迁移和安全校验为无状态单例；Document ViewModel 只依赖窄接口。
        services.AddSingleton<IBiliDownloaderDocumentStateMapper, BiliDownloaderDocumentStateMapper>();
        services.AddSingleton<IFfmpegProcessFactory, FfmpegProcessFactory>();
        services.AddSingleton<FfmpegService>();
        // 同一个本地适配器分别暴露定位与封装能力，消费者只依赖自己真正需要的接口。
        // 保留 IFfmpegService 映射仅用于旧构造路径兼容，不再作为生产类的首选依赖。
        services.AddSingleton<IFfmpegRuntimeLocator>(provider => provider.GetRequiredService<FfmpegService>());
        services.AddSingleton<IMediaMuxer>(provider => provider.GetRequiredService<FfmpegService>());
        services.AddSingleton<IMediaMuxerCapabilityProvider>(provider => provider.GetRequiredService<FfmpegService>());
        services.AddSingleton<IFfmpegService>(provider => provider.GetRequiredService<FfmpegService>());
        services.AddSingleton<IFfmpegPackageDownloader, HttpFfmpegPackageDownloader>();
        services.AddSingleton<IFfmpegInstallPlatform, SystemFfmpegInstallPlatform>();
        services.AddSingleton(FfmpegPackageManifest.GyanReleaseEssentials812);
        services.AddSingleton<IFfmpegPackageInstaller, FfmpegPackageInstaller>();
        services.AddSingleton<IUserPromptService, AvaloniaUserPromptService>();
        services.AddSingleton<IConfirmationService>(provider => provider.GetRequiredService<IUserPromptService>());
        services.AddSingleton<IFileRevealService, FileRevealService>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IBiliHttpClientFactory, BiliHttpClientFactory>();
        services.AddSingleton<IDownloadRuntime, SystemDownloadRuntime>();

        // 登录态只依赖凭据存储接口；SQLite 内只保存 AES-GCM 密文信封。
        services.AddSingleton<InstallationKeyStore>();
        services.AddSingleton<ICredentialProtector, AesGcmCredentialProtector>();
        services.AddSingleton<IBiliCredentialStore, BiliCredentialStore>();
        services.AddSingleton<BiliLoginService>();
        services.AddSingleton<IBiliSessionApi>(provider =>
            provider.GetRequiredService<BiliLoginService>());
        services.AddSingleton<BiliLoginStateService>();
        services.AddSingleton<ILoginDialogService, AvaloniaLoginDialogService>();
        services.AddSingleton<IBiliCredentialProvider, BiliCredentialProvider>();
        services.AddSingleton<IBiliAccountContext, BiliAccountContext>();

        // 有网络和文件副作用的服务集中在 IDownloadTaskExecutor 之后，
        // Coordinator 测试可用假执行器完整替换这一边界。
        services.AddSingleton<BiliApiService>();
        // Provider 与解析界面只依赖窄 API；两个投影复用同一 BiliApiService 实例。
        services.AddSingleton<IBiliContentSourceApi>(provider => provider.GetRequiredService<BiliApiService>());
        services.AddSingleton<IBiliMediaProbe>(provider => provider.GetRequiredService<BiliApiService>());
        services.AddSingleton<BiliPersonalContentApi>();
        services.AddSingleton<IBiliUploaderCatalogApi>(provider => provider.GetRequiredService<BiliPersonalContentApi>());
        services.AddSingleton<IBiliFavoriteCatalogApi>(provider => provider.GetRequiredService<BiliPersonalContentApi>());
        services.AddSingleton<IBiliWatchLaterCatalogApi>(provider => provider.GetRequiredService<BiliPersonalContentApi>());
        services.AddSingleton<IBiliHistoryCatalogApi>(provider => provider.GetRequiredService<BiliPersonalContentApi>());
        services.AddSingleton<BiliSubscriptionContentApi>();
        services.AddSingleton<IBiliFollowingCatalogApi>(provider => provider.GetRequiredService<BiliSubscriptionContentApi>());
        services.AddSingleton<IBiliCollectedFolderApi>(provider => provider.GetRequiredService<BiliSubscriptionContentApi>());
        services.AddSingleton<IBiliCourseCatalogApi>(provider => provider.GetRequiredService<BiliSubscriptionContentApi>());
        services.AddSingleton<IContentSourceItemResolver, ContentSourceItemResolver>();
        services.AddSingleton<BoundedContentSnapshotStore>();
        services.AddSingleton<HierarchicalContentSnapshotStore>();
        services.AddSingleton<IContentSourceProvider, DirectLinkProvider>();
        services.AddSingleton<IContentSourceProvider, UploaderSourceProvider>();
        services.AddSingleton<FavoriteSourceProvider>();
        services.AddSingleton<IContentSourceProvider>(provider => provider.GetRequiredService<FavoriteSourceProvider>());
        services.AddSingleton<IFavoriteSourceDiscoveryService>(provider => provider.GetRequiredService<FavoriteSourceProvider>());
        services.AddSingleton<IContentSourceProvider, WatchLaterSourceProvider>();
        services.AddSingleton<IContentSourceProvider, HistorySourceProvider>();
        services.AddSingleton<IContentSourceProvider, FollowingBangumiSourceProvider>();
        services.AddSingleton<IContentSourceProvider, FollowingCinemaSourceProvider>();
        services.AddSingleton<IContentSourceProvider, CollectionSourceProvider>();
        services.AddSingleton<IContentSourceProvider, CourseSourceProvider>();
        services.AddSingleton<IContentSourceProviderRegistry, ContentSourceProviderRegistry>();
        // P1-G5 将远端扫描、纯分类和任务事实编排拆成窄服务，检查更新因此不会依赖 Coordinator，
        // 也不可能在扫描阶段意外创建或启动下载任务。
        services.AddSingleton<IContentSourceScanService, ContentSourceScanService>();
        services.AddSingleton<IOutputFileFactProvider, SystemOutputFileFactProvider>();
        services.AddSingleton<IContentComparisonPolicy, ContentComparisonPolicy>();
        services.AddSingleton<IIncrementalComparisonService, IncrementalComparisonService>();
        services.AddSingleton<BiliDownloadService>();
        services.AddSingleton(provider => ExtrasHandlerRegistry.CreateDefault(
            provider.GetRequiredService<IBiliHttpClientFactory>()));
        services.AddSingleton<IDownloadTaskExecutor, BiliDownloadTaskExecutor>();
        services.AddSingleton<IDownloadProgressTracker, DownloadProgressTracker>();
        services.AddSingleton<IDownloadRecoveryService, DownloadRecoveryService>();
        services.AddSingleton<IStorageCapacityProvider, SystemStorageCapacityProvider>();
        services.AddSingleton<IOutputArtifactPolicy, OutputArtifactPolicy>();
        services.AddSingleton<IMediaStreamSelectionPolicy, MediaStreamSelectionPolicy>();
        services.AddSingleton<INativeAudioPublisher, NativeAudioPublisher>();
        services.AddSingleton<IMediaSizeCalculator, MediaSizeCalculator>();
        services.AddSingleton<IMediaPreflightAnalyzer, DashMediaPreflightAnalyzer>();
        services.AddSingleton<IMediaSizeEstimator, DashMediaSizeEstimator>();
        services.AddSingleton<IFileConflictStrategy, SkipConflictStrategy>();
        services.AddSingleton<IFileConflictStrategy, OverwriteConflictStrategy>();
        services.AddSingleton<IFileConflictStrategy, ResumeVerifiedConflictStrategy>();
        services.AddSingleton<IFileConflictStrategy, AutoNumberConflictStrategy>();
        services.AddSingleton<ISubmissionPreflightService, SubmissionPreflightService>();
        services.AddSingleton<IDownloadFailurePresentationPolicy, DownloadFailurePresentationPolicy>();

        // P1-G6：历史中心的四个业务能力分别注册到窄接口。文件选择器和文件系统探测
        // 只在用户主动命令中调用，插件生命周期初始化不会遍历任何历史路径。
        services.AddSingleton<ITaskHistoryQueryService, TaskHistoryQueryService>();
        services.AddSingleton<IOutputFileStatusService, OutputFileStatusService>();
        services.AddSingleton<ITaskHistoryExporter, TaskHistoryExporter>();
        services.AddSingleton<ITaskHistoryRedownloadService, TaskHistoryRedownloadService>();
        services.AddSingleton<IHistoryExportDestinationPicker, AvaloniaHistoryExportDestinationPicker>();

        services.AddSingleton<BiliDownloadCoordinator>();
        services.AddSingleton<IDownloadSubmissionService, DownloadSubmissionService>();
        services.AddSingleton<IDownloadFailureActionService, DownloadFailureActionService>();
        services.AddSingleton<BiliSchedulerToolViewModel>();
        services.AddTransient<BiliDownloaderViewModel>();

        services.AddSingleton<IPluginLifecycle, BiliDownloaderPluginLifecycle>();
    }
}

/// <summary>
/// BiliDownloader 的宿主管理生命周期。初始化先恢复本地状态，再启动非阻塞登录验证；
/// 关闭时取消并等待后台验证与 Coordinator，不依赖任何 Tool 或 Document 视图。
/// </summary>
public sealed class BiliDownloaderPluginLifecycle : IPluginLifecycle
{
    private readonly IBiliLocalStateInitializer _localStateInitializer;
    private readonly BiliLoginStateService _loginStateService;
    private readonly BiliDownloadCoordinator _coordinator;
    private readonly ISettingsRepository _settings;
    private readonly IFfmpegRuntimeLocator _ffmpeg;

    public BiliDownloaderPluginLifecycle(
        IBiliLocalStateInitializer localStateInitializer,
        BiliLoginStateService loginStateService,
        BiliDownloadCoordinator coordinator,
        ISettingsRepository settings,
        IFfmpegRuntimeLocator ffmpeg)
    {
        _localStateInitializer = localStateInitializer;
        _loginStateService = loginStateService;
        _coordinator = coordinator;
        _settings = settings;
        _ffmpeg = ffmpeg;
    }

    public string PluginId => "BiliDownloader";

    public int Order => 100;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _localStateInitializer.InitializeAsync(cancellationToken);
        // 仅加载本地配置并执行 -version 探测，不下载任何内容。这样 Document 即使先于 Tool 打开，
        // 提交预检也能观察到真实 ffmpeg 状态，而不是依赖某个设置视图曾经被激活。
        await _settings.InitAsync();
        _ffmpeg.CustomPath = await _settings.GetSettingAsync("ffmpeg_custom_path");
        await _ffmpeg.DetectAsync(cancellationToken);
        await _loginStateService.RestoreSavedSessionAsync(cancellationToken);
        await _coordinator.InitializeAsync();
        _loginStateService.StartBackgroundValidation();
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _loginStateService.StopAsync(cancellationToken);
        await _coordinator.ShutdownAsync();
    }
}
