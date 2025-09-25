using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.Message;

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
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // 注册消息服务为单例
        services.AddSingleton<IMessengerService, MessengerService>();
        
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
    /// 注册ViewModels
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        // 注册MainWindowViewModel为瞬态，每次请求都创建新实例
        services.AddTransient<MainWindowViewModel>();
        
        return services;
    }
}