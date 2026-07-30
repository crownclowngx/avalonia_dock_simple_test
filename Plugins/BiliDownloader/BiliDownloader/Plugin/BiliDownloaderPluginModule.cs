using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Download.Extras;
using BiliDownloader.Services.Infrastructure;
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
        services.AddSingleton<ISettingsRepository, SettingsStore>();
        services.AddSingleton<IFfmpegProcessFactory, FfmpegProcessFactory>();
        services.AddSingleton<IFfmpegService, FfmpegService>();
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
        services.AddSingleton<IBiliCredentialProvider, BiliCredentialProvider>();

        // 有网络和文件副作用的服务集中在 IDownloadTaskExecutor 之后，
        // Coordinator 测试可用假执行器完整替换这一边界。
        services.AddSingleton<BiliApiService>();
        services.AddSingleton<BiliDownloadService>();
        services.AddSingleton(provider => ExtrasHandlerRegistry.CreateDefault(
            provider.GetRequiredService<IBiliHttpClientFactory>()));
        services.AddSingleton<IDownloadTaskExecutor, BiliDownloadTaskExecutor>();
        services.AddSingleton<IDownloadProgressTracker, DownloadProgressTracker>();

        services.AddSingleton<BiliDownloadCoordinator>();
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

    public BiliDownloaderPluginLifecycle(
        IBiliLocalStateInitializer localStateInitializer,
        BiliLoginStateService loginStateService,
        BiliDownloadCoordinator coordinator)
    {
        _localStateInitializer = localStateInitializer;
        _loginStateService = loginStateService;
        _coordinator = coordinator;
    }

    public string PluginId => "BiliDownloader";

    public int Order => 100;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _localStateInitializer.InitializeAsync(cancellationToken);
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
