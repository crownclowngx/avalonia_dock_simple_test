using System;
using System.Text;
using System.Threading;
using Avalonia;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        using var runtime = HostRuntime.Create();

        // public 无参 ViewModel 和外部测试宿主仍依赖历史服务定位器；
        // 实际容器所有权归 HostRuntime，避免兼容入口重新承担组合根职责。
        Business.Helpers.ServiceProvider.Initialize(runtime.Services);
        runtime.InitializePlugins();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// 在 Avalonia 消息循环结束后反向关闭插件。
    /// 保留该入口是为了兼容既有测试，同时由 HostRuntime 控制调用时机。
    /// </summary>
    internal static void ShutdownPlugins(PluginLifecycleManager lifecycleManager)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        lifecycleManager.ShutdownAllAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 创建生产启动与兼容测试宿主共用的 Avalonia 应用构建器。
    /// 维持单一构建路径可防止两种启动方式产生不同的平台配置。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
