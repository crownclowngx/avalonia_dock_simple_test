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
        // Core.Initialize 是进程级操作，但初始化时机保持惰性：只有首次解析播放器时才执行。
        services.AddSingleton<LibVlcRuntime>();

        // G3.1 的核心资源边界：
        // 1. PlayerHost 在整个 Document 生命周期中只创建一次，所以切换媒体不会重新创建
        //    LibVLC、MediaPlayer，也不会迫使 VideoView 重新绑定 HWND。
        // 2. MediaSource 仍按“一个视频一个实例”创建，它只拥有可安全独立回收的文件、解密流和 Media。
        // 3. Dispatcher 与 Reaper 都是 Document-scoped 单消费者，既把原生调用串行化，
        //    也防止快速连续切换产生无界的后台释放任务。
        services.AddScoped<LibVlcDocumentPlayerHost>();
        services.AddScoped<IPlaybackPlayerHost>(provider =>
            provider.GetRequiredService<LibVlcDocumentPlayerHost>());
        services.AddScoped<IPlaybackMediaSourceFactory, LibVlcPlaybackMediaSourceFactory>();
        services.AddScoped<IPlaybackNativeDispatcher, PlaybackNativeDispatcher>();
        services.AddScoped<IPlaybackResourceReaper, PlaybackResourceReaper>();
        services.AddScoped<SecureVideoPlayer>();
        services.AddScoped<ISecureVideoPlaybackSession>(provider =>
            provider.GetRequiredService<SecureVideoPlayer>());
        services.AddScoped<ILibVlcVideoOutputSource>(provider =>
            provider.GetRequiredService<SecureVideoPlayer>());
        services.AddScoped<VideoPlayerControlViewModel>();
        services.AddScoped<SecretVideoPlayerViewModel>();

        // 文件夹浏览只读取公开区；扫描器无跨调用状态，浏览和密码状态则严格属于单个 Document。
        services.AddTransient<IVideoLibraryScanner, VideoLibraryScanner>();
        services.AddScoped<VideoLibraryBrowserViewModel>();
        services.AddScoped<SecretVideoLibraryViewModel>();

        // 存储预检和输出事务无跨调用状态；加密/解密共享相同的不覆盖提交语义。
        services.AddTransient<IStoragePreflightProbe, StoragePreflightProbe>();
        services.AddTransient<IOutputFileTransactionFactory, OutputFileTransactionFactory>();

        // 加密任务状态属于单个 Document；密码只在 ViewModel 调用栈中传递。
        services.AddTransient<ISecvid03Encryptor, Secvid03Encryptor>();
        services.AddScoped<IVideoEncryptionService, VideoEncryptorService>();
        services.AddScoped<VideoEncryptorViewModel>();

        // 单文件解密器无共享状态；批处理编排和队列则跟随各自 Document Scope。
        services.AddTransient<ISecvid03Decryptor, Secvid03Decryptor>();
        services.AddTransient<DecryptionOutputPathResolver>();
        services.AddScoped<IVideoDecryptionService, VideoDecryptionService>();
        services.AddScoped<VideoDecryptorViewModel>();

    }
}
