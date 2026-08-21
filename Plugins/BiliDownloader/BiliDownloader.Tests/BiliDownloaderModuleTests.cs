using BiliDownloader.Constants;
using BiliDownloader.Plugin;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Persistence;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.History;
using BiliDownloader.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Events;
using MyAvaloniaManagementCommon.Plugin;

namespace BiliDownloader.Tests;

public sealed class BiliDownloaderModuleTests
{
    [Fact]
    public void 模块只注册自身服务_并复用宿主消息服务()
    {
        var services = new ServiceCollection();
        var module = new BiliDownloaderPluginModule();
        var context = new TestPluginRegistrationContext(
            new PluginId("myavalonia.plugin.bili-downloader"), services);

        module.Configure(context);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostEventBus));
        Assert.Equal(
            ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(BiliDownloadCoordinator)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(BiliSchedulerToolViewModel)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Scoped,
            FindDescriptor(services, typeof(BiliDownloaderViewModel)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(IFfmpegService)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(IFfmpegRuntimeLocator)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(IMediaMuxer)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(IFfmpegPackageInstaller)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(IDownloadFailureActionService)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(IBiliHttpClientFactory)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(IBiliContentSourceApi)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(IBiliMediaProbe)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(IContentSourceProviderRegistry)).Lifetime);
        Assert.Equal(9, services.Count(descriptor =>
            descriptor.ServiceType == typeof(IContentSourceProvider)
            && descriptor.Lifetime == ServiceLifetime.Singleton));
        Assert.Equal(
            ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(IDownloadRuntime)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(ITaskHistoryReadRepository)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(ITaskHistoryQueryService)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(IOutputFileStatusService)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(ITaskHistoryExporter)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(ITaskHistoryRedownloadService)).Lifetime);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IPluginLifecycle));
        var lifecycle = Assert.Single(context.Contributions, item => item.Kind == "Lifecycle");
        Assert.Equal(typeof(BiliDownloaderPluginLifecycle), lifecycle.First);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
        Assert.Throws<InvalidOperationException>(
            provider.GetRequiredService<BiliDownloaderViewModel>);
    }

    [Fact]
    public async Task 模块生命周期解析唯一协调器_且回调完成初始化与关闭职责()
    {
        var services = new ServiceCollection();
        var pluginId = new PluginId("myavalonia.plugin.bili-downloader");
        var context = new TestPluginRegistrationContext(pluginId, services);
        new BiliDownloaderPluginModule().Configure(context);
        Assert.Single(context.Contributions, item => item.Kind == "Lifecycle");

        // 测试在模块注册之后覆盖所有可能产生外部副作用的边界。
        // Microsoft DI 对单服务解析采用最后一次注册，因此这里不会创建真实 SQLite 仓储、
        // 不会读取登录 Cookie，也不会构造会访问网络、媒体目录或 ffmpeg 的生产执行器。
        var repository = new InMemoryDownloadTaskRepository();
        services.AddSingleton<IHostEventBus>(new IsolatedHostEventBus());
        services.AddSingleton<IBiliLocalStateInitializer>(new NoOpLocalStateInitializer());
        services.AddSingleton<IBiliCredentialStore>(new InMemoryBiliCredentialStore());
        services.AddSingleton<IBiliSessionApi>(new StubBiliSessionApi());
        services.AddSingleton<IDownloadTaskRepository>(repository);
        services.AddSingleton<ISettingsRepository>(new InMemorySettingsRepository());
        services.AddSingleton<IFfmpegRuntimeLocator>(new FakeFfmpegService { ReadyOverride = true });
        services.AddSingleton<IBiliCredentialProvider>(new FakeCredentialProvider());
        services.AddSingleton<IDownloadTaskExecutor>(new FakeDownloadTaskExecutor());
        // 该轻量测试上下文只记录贡献声明，不模拟 Host 的插件 Provider；显式注册实例
        // 仅用于验证 Bili 生命周期自身，不重新引入已删除的 Host Manager。
        services.AddSingleton<BiliDownloaderPluginLifecycle>();

        using var provider = services.BuildServiceProvider();
        var firstCoordinator = provider.GetRequiredService<BiliDownloadCoordinator>();
        var secondCoordinator = provider.GetRequiredService<BiliDownloadCoordinator>();
        var lifecycle = provider.GetRequiredService<BiliDownloaderPluginLifecycle>();

        Assert.Same(firstCoordinator, secondCoordinator);

        await lifecycle.InitializeAsync(CancellationToken.None);
        Assert.Equal(1, repository.InitializeCount);

        await lifecycle.ShutdownAsync(CancellationToken.None);
    }

    private static ServiceDescriptor FindDescriptor(IServiceCollection services, Type serviceType)
        => Assert.Single(services, descriptor => descriptor.ServiceType == serviceType);
}
