using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using System;
using System.Text;

namespace MyAvaloniaManagement;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    { 
        Console.OutputEncoding = Encoding.UTF8;
        
        // 配置依赖注入容器
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();
        
        // 初始化全局服务提供者
        Business.Helpers.ServiceProvider.Initialize(serviceProvider);
        
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
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

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}