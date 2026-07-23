using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;
using MySmallTools.Business.SecretVideoPlayer;
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

        // 每个 Document Scope 拥有独立播放器及其恢复状态，绝不在不同视频标签页之间共享原生对象。
        services.AddScoped<SecureVideoPlayer>();
        services.AddScoped<VideoSurfaceRecoveryPolicy>();
        services.AddScoped<VideoPlayerControlViewModel>();
        services.AddScoped<SecretVideoPlayerViewModel>();

        // 文件夹浏览只读取公开区；扫描器无跨调用状态，浏览和密码状态则严格属于单个 Document。
        services.AddTransient<IVideoLibraryScanner, VideoLibraryScanner>();
        services.AddScoped<VideoLibraryBrowserViewModel>();
        services.AddScoped<SecretVideoLibraryViewModel>();

        // 加密任务状态属于单个 Document；底层加密器无跨任务可变状态，按需创建即可。
        services.AddTransient<Secvid03Encryptor>();
        services.AddScoped<VideoEncryptorService>();
        services.AddScoped<VideoEncryptorViewModel>();

        // 单文件解密器无共享状态；批处理编排和队列则跟随各自 Document Scope。
        services.AddTransient<ISecvid03Decryptor, Secvid03Decryptor>();
        services.AddTransient<DecryptionOutputPathResolver>();
        services.AddScoped<IVideoDecryptionService, VideoDecryptionService>();
        services.AddScoped<VideoDecryptorViewModel>();

    }
}
