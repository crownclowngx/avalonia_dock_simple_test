using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagementCommon.Plugin;
using System;
using System.Text;

namespace MyAvaloniaManagement;

sealed class Program
{
    // 在进入 Avalonia 主生命周期之前，只允许执行与 UI 无关的宿主组合工作。
    // 此时 Avalonia、第三方 UI 组件和依赖 SynchronizationContext 的代码都尚未完成初始化，
    // 因此插件模块注册与本地状态初始化必须保持为纯后台逻辑，不能创建控件或访问视觉树。
    [STAThread]
    public static void Main(string[] args)
    { 
        Console.OutputEncoding = Encoding.UTF8;
        
        // 配置依赖注入容器。插件模块采用显式选择接入方式；
        // 未声明模块的历史插件不会参与服务注册和宿主管理生命周期。
        var services = new ServiceCollection();
        ConfigureServices(services);

        var pluginAssemblies = AssemblyLoaderHelper.LoadPluginsFromDirectories(
            AssemblyLoadConstant.PLUGINS_SUBDIRECTORY);
        var pluginCatalog = PluginModuleCatalog.Discover(pluginAssemblies);
        pluginCatalog.ConfigureServices(services);
        services.AddSingleton(pluginCatalog);
        services.AddSingleton<PluginLifecycleManager>();

        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            // 托管 Document 必须从独立 Scope 解析；打开验证后，任何误从根容器解析 scoped 服务的
            // 行为都会在启动或解析阶段立即暴露，而不是演变为关闭标签后仍残留文件句柄。
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        
        // 初始化全局服务提供者
        Business.Helpers.ServiceProvider.Initialize(serviceProvider);
        
        var lifecycleManager = serviceProvider.GetRequiredService<PluginLifecycleManager>();
        lifecycleManager.InitializeAllAsync().GetAwaiter().GetResult();

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            lifecycleManager.ShutdownAllAsync().GetAwaiter().GetResult();
        }
    } 

    /// <summary>
    /// 配置依赖注入服务
    /// </summary>
    /// <param name="services">服务集合</param>
    private static void ConfigureServices(IServiceCollection services)
    {
        // 注册应用程序核心服务
        services.AddApplicationServices();
        
        // 注册ViewModels
        services.AddViewModels();
    }

    // Avalonia 的统一启动配置，同时供运行时和可视化设计器使用，请勿从启动链路中移除。
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
