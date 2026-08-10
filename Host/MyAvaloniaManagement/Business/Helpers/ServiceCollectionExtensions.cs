using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 依赖注入服务注册扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册应用程序核心服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    /// <remarks>
    /// 状态协调器采用单例，保证全应用共享同一 Dock、消息和布局状态；
    /// 存储服务也作为无状态单例注册，便于 ViewModel 通过接口替换测试实现。
    /// </remarks>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(new PluginLifecycleOptions());
        services.AddSingleton<PluginLifecycleManager>();

        // 注册消息服务为单例
        services.AddSingleton<IMessengerService, MessengerService>();

        // 每个由托管插件创建的 Document 都拥有独立 Scope。插件只依赖公共创建接口，
        // Dock 关闭时则由宿主使用具体管理器释放对应 Scope。
        services.AddSingleton<DocumentScopeManager>();
        services.AddSingleton<IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<DocumentScopeManager>());

        services.AddSingleton<DockLayoutStore>();
        services.AddSingleton<DockLayoutLifecycle>();
        services.AddSingleton<AppearanceSettingsStore>();
        services.AddSingleton<ApplicationThemeService>();
        services.AddSingleton<IHostStorageService, AvaloniaHostStorageService>();
        
        // 注册ManagementFactory为单例
        services.AddSingleton<ManagementFactory>();
        
        // 注册PluginMenuService为单例，依赖ManagementFactory
        services.AddSingleton<PluginMenuService>(provider =>
        {
            var factory = provider.GetRequiredService<ManagementFactory>();
            return new PluginMenuService(factory);
        });
        
        return services;
    }
    
    /// <summary>
    /// 注册主窗口及三个宿主工具 ViewModel。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    /// <remarks>
    /// ViewModel 采用瞬态生命周期，避免设计器、Headless 测试和窗口实例之间共享可变绑定状态。
    /// 显式工厂注册用于调用 internal 注入构造函数，同时保留公开无参构造的兼容能力。
    /// </remarks>
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddTransient(provider => new FileSystemTreeViewModel(
            provider.GetRequiredService<IHostStorageService>(),
            provider.GetRequiredService<IMessengerService>()));
        services.AddTransient(provider => new PlugGroupMenuViewModel(
            provider.GetRequiredService<ManagementFactory>(),
            provider.GetRequiredService<PluginMenuService>()));
        services.AddTransient(provider => new ToolManagementViewModel(
            provider.GetRequiredService<ManagementFactory>(),
            provider.GetRequiredService<IMessengerService>()));
        services.AddTransient(provider => new PluginStatusViewModel(
            provider.GetRequiredService<PluginModuleCatalog>(),
            provider.GetRequiredService<PluginLifecycleManager>()));

        // 注册MainWindowViewModel为瞬态，每次请求都创建新实例
        services.AddTransient(provider => new MainWindowViewModel(
            provider.GetRequiredService<ManagementFactory>(),
            provider.GetRequiredService<PluginMenuService>(),
            provider.GetRequiredService<IMessengerService>(),
            provider.GetRequiredService<DockLayoutLifecycle>(),
            provider.GetRequiredService<IHostStorageService>(),
            provider.GetRequiredService<ApplicationThemeService>()));
        
        return services;
    }
}
