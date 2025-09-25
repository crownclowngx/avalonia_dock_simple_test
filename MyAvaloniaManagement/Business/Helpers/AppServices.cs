using System;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.Message;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 应用程序服务访问器，提供对共享服务的访问
/// 现在使用依赖注入容器而非静态服务定位器
/// </summary>
[Obsolete("请使用依赖注入容器ServiceProvider替代AppServices。此类保留用于向后兼容。")]
public class AppServices
{
    /// <summary>
    /// 获取AppServices的单例实例（向后兼容）
    /// </summary>
    public static AppServices Instance => new AppServices();
    
    /// <summary>
    /// ManagementFactory实例
    /// </summary>
    public ManagementFactory? ManagementFactory => ServiceProvider.GetService<ManagementFactory>();
    
    /// <summary>
    /// PluginMenuService实例
    /// </summary>
    public PluginMenuService? PluginMenuService => ServiceProvider.GetService<PluginMenuService>();
    
    /// <summary>
    /// MessengerService实例，用于消息传递
    /// </summary>
    public IMessengerService? MessengerServiceDefault => ServiceProvider.GetService<IMessengerService>();
    
    /// <summary>
    /// 初始化AppServices（向后兼容，现在为空实现）
    /// </summary>
    /// <param name="factory">ManagementFactory实例</param>
    /// <param name="pluginMenuService">PluginMenuService实例</param>
    [Obsolete("不再需要手动初始化，服务现在通过依赖注入容器管理")]
    public static void Initialize(ManagementFactory factory, PluginMenuService pluginMenuService)
    {
        // 空实现，保留用于向后兼容
        // 服务现在通过依赖注入容器管理
    }
}