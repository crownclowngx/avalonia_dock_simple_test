using BiliDownloader.Plugin;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Plugin;

namespace BiliDownloader.Tests;

public sealed class BiliDownloaderModuleTests
{
    [Fact]
    public void 模块只注册自身服务_并复用宿主消息服务()
    {
        var services = new ServiceCollection();
        var module = new BiliDownloaderPluginModule();

        module.ConfigureServices(services);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IMessengerService));
        Assert.Equal(
            ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(BiliDownloadCoordinator)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            FindDescriptor(services, typeof(BiliSchedulerToolViewModel)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Transient,
            FindDescriptor(services, typeof(BiliDownloaderViewModel)).Lifetime);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPluginLifecycle));
    }

    [Fact]
    public async Task 模块生命周期解析唯一协调器_且初始化关闭均由宿主管理器执行()
    {
        var services = new ServiceCollection();
        new BiliDownloaderPluginModule().ConfigureServices(services);

        // 测试在模块注册之后覆盖所有可能产生外部副作用的边界。
        // Microsoft DI 对单服务解析采用最后一次注册，因此这里不会创建真实 SQLite 仓储、
        // 不会读取登录 Cookie，也不会构造会访问网络、媒体目录或 ffmpeg 的生产执行器。
        var repository = new InMemoryDownloadTaskRepository();
        services.AddSingleton<IMessengerService>(new IsolatedMessengerService());
        services.AddSingleton<IDownloadTaskRepository>(repository);
        services.AddSingleton<IBiliCredentialProvider>(new FakeCredentialProvider());
        services.AddSingleton<IDownloadTaskExecutor>(new FakeDownloadTaskExecutor());
        services.AddSingleton<PluginLifecycleManager>();

        using var provider = services.BuildServiceProvider();
        var firstCoordinator = provider.GetRequiredService<BiliDownloadCoordinator>();
        var secondCoordinator = provider.GetRequiredService<BiliDownloadCoordinator>();
        var manager = provider.GetRequiredService<PluginLifecycleManager>();

        Assert.Same(firstCoordinator, secondCoordinator);
        Assert.Single(manager.States);
        Assert.Equal(PluginLifecycleStatus.NotStarted, manager.GetState("BiliDownloader")?.Status);

        await manager.InitializeAllAsync();
        Assert.Equal(1, repository.InitializeCount);
        Assert.Equal(PluginLifecycleStatus.Ready, manager.GetState("BiliDownloader")?.Status);

        await manager.ShutdownAllAsync();
        Assert.Equal(PluginLifecycleStatus.Stopped, manager.GetState("BiliDownloader")?.Status);
    }

    private static ServiceDescriptor FindDescriptor(IServiceCollection services, Type serviceType)
        => Assert.Single(services, descriptor => descriptor.ServiceType == serviceType);
}
