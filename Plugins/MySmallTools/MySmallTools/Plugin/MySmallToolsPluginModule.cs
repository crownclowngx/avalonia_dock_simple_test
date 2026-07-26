using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;
using MySmallTools.Business.SecretVideoPlayer.Decryption;
using MySmallTools.Business.SecretVideoPlayer.Encryption;
using MySmallTools.Business.SecretVideoPlayer.Library;
using MySmallTools.Business.SecretVideoPlayer.Operations;
using MySmallTools.Business.SecretVideoPlayer.Playback;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Plugin;

/// <summary>
/// MySmallTools 显式接入宿主依赖注入容器的模块入口。
/// </summary>
/// <remarks>
/// 本模块只声明服务的构造关系和生命周期，不创建 View、Document 或 LibVLC 实例。
/// 播放器和加密器 Document 由宿主为每次创建建立独立 Scope，因此多个标签页之间不会共享
/// 播放位置、恢复快照、加密任务或原生播放器；关闭标签页后也能由容器及时释放全部 scoped 服务。
/// </remarks>
public sealed class MySmallToolsPluginModule : IPluginModule
{
    public string PluginId => "MySmallTools";

    public void ConfigureServices(IServiceCollection services)
    {
        // 平台能力、运行时布局和部署探针都是进程级无状态事实源。运行时初始化器仍保持
        // 惰性，并且只消费已经通过检查的插件私有绝对目录。
        services.AddSingleton<IPlaybackRuntimeLayoutProvider,
            PluginLocalPlaybackRuntimeLayoutProvider>();
        services.AddSingleton<WindowsX64PlaybackCapabilitiesProvider>();
        services.AddSingleton<PlaybackDeploymentProbe>();
        services.AddSingleton<IPlaybackDeploymentProbe>(provider =>
            provider.GetRequiredService<PlaybackDeploymentProbe>());
        services.AddSingleton<IPlaybackPlatformStatus, PlaybackPlatformStatus>();
        services.AddSingleton<LibVlcRuntime>();
        services.AddSingleton<IPlaybackRuntimeInitializer>(provider =>
            provider.GetRequiredService<LibVlcRuntime>());

        // G3.1 的核心资源边界：
        // 1. PlayerHost 在整个 Document 生命周期中只创建一次，所以切换媒体不会重新创建
        //    LibVLC、MediaPlayer，也不会迫使 VideoView 重新绑定 HWND。
        // 2. MediaSource 仍按“一个视频一个实例”创建，它只拥有可安全独立回收的文件、解密流和 Media。
        // 3. Dispatcher 与 Reaper 都是 Document-scoped 单消费者，既把原生调用串行化，
        //    也防止快速连续切换产生无界的后台释放任务。
        services.AddScoped<IPlaybackBackendFactory, LibVlcPlaybackBackendFactory>();
        services.AddScoped<LazyPlaybackBackend>();
        services.AddScoped<IPlaybackPlayerHost>(provider =>
            provider.GetRequiredService<LazyPlaybackBackend>());
        services.AddScoped<IPlaybackMediaSourceFactory>(provider =>
            provider.GetRequiredService<LazyPlaybackBackend>());
        services.AddScoped<IPlaybackBackendInitializer>(provider =>
            provider.GetRequiredService<LazyPlaybackBackend>());
        services.AddScoped<IPlaybackNativeDispatcher, PlaybackNativeDispatcher>();
        services.AddScoped<IPlaybackResourceReaper, PlaybackResourceReaper>();
        services.AddScoped<SecureVideoPlayer>();
        services.AddScoped<ISecureVideoPlaybackSession>(provider =>
            provider.GetRequiredService<SecureVideoPlayer>());
        services.AddScoped<IPlaybackSurfaceSession>(provider =>
            provider.GetRequiredService<SecureVideoPlayer>());
        // G7.1 保留顶层类型作为宿主解析边界；Playback 功能包的状态与子组件由该兼容
        // 外壳的基类在同一 Document Scope 内创建，不额外注册全局或跨文档 UI 状态。
        services.AddScoped<VideoPlayerControlViewModel>();
        services.AddScoped<SecretVideoPlayerViewModel>();

        // 用户数据文件是进程级唯一事实源；三个接口映射同一实例，既保证并发写入串行，
        // 又让播放器、媒体库设置和历史协调器只依赖各自需要的窄契约。
        services.AddSingleton<SecretVideoUserDataStore>();
        services.AddSingleton<IPlaybackPreferenceStore>(provider =>
            provider.GetRequiredService<SecretVideoUserDataStore>());
        services.AddSingleton<IVideoLibrarySettingsStore>(provider =>
            provider.GetRequiredService<SecretVideoUserDataStore>());
        services.AddSingleton<IPlaybackHistoryStore>(provider =>
            provider.GetRequiredService<SecretVideoUserDataStore>());
        services.AddSingleton<ISecretVideoUserDataDiagnostics>(provider =>
            provider.GetRequiredService<SecretVideoUserDataStore>());

        // 目录会话和历史跟踪都属于 Document：关闭标签页必须终止 watcher、Channel
        // 和位置订阅。扫描器本身无跨调用状态，仍保持 transient。
        services.AddTransient<IVideoLibraryScanner, VideoLibraryScanner>();
        services.AddScoped<IVideoLibraryCatalogSession, VideoLibraryCatalogSession>();
        services.AddScoped<PlaybackHistoryCoordinator>();
        services.AddScoped<VideoLibraryBrowserViewModel>();
        services.AddScoped<SecretVideoLibraryViewModel>();

        // 存储预检和输出事务无跨调用状态；加密/解密共享相同的不覆盖提交语义。
        services.AddTransient<IStoragePreflightProbe, StoragePreflightProbe>();
        services.AddTransient<IOutputFileTransactionFactory, OutputFileTransactionFactory>();

        // G5 队列运行器必须是 Document-scoped：它拥有“当前项”和两级取消源，跨 Document
        // 共享会让一个标签页的取消命令影响另一个标签页。开放泛型只复用编排机制，
        // 加密与解密仍使用各自的预检项目和应用服务。
        services.AddScoped(typeof(ISequentialVideoQueueRunner<>), typeof(SequentialVideoQueueRunner<>));
        services.AddTransient<IOutputPathConflictResolver, OutputPathConflictResolver>();

        // 加密任务状态属于单个 Document；批次计划和单文件执行分离，密码仍只在
        // ViewModel 调用单项服务的同步调用链中传递。
        services.AddTransient<ISecvid03Encryptor, Secvid03Encryptor>();
        services.AddScoped<IVideoEncryptionService, VideoEncryptorService>();
        services.AddScoped<IVideoBatchEncryptionService, VideoBatchEncryptionService>();
        // 加密/解密顶层 Document 是无状态兼容外壳，队列与批次实现仍由各自 Document
        // 独占，继续沿用原有 Scoped 服务和取消边界。
        services.AddScoped<VideoEncryptorViewModel>();

        // 单文件解密器无共享状态；批处理编排和队列则跟随各自 Document Scope。
        services.AddTransient<ISecvid03Decryptor, Secvid03Decryptor>();
        services.AddTransient<DecryptionOutputPathResolver>();
        services.AddScoped<IVideoDecryptionService, VideoDecryptionService>();
        services.AddScoped<VideoDecryptorViewModel>();

    }
}
