using MyAvaloniaManagement.ViewModels;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 应用程序服务定位器，提供对共享服务的访问
/// </summary>
public class AppServices
{
    // 单例实例
    private static AppServices? _instance;
    
    /// <summary>
    /// 获取AppServices的单例实例
    /// </summary>
    public static AppServices Instance
    {
        get
        {
            if (_instance == null)
            {
                throw new System.InvalidOperationException("AppServices尚未初始化");
            }
            return _instance;
        }
    }
    
    /// <summary>
    /// ManagementFactory实例
    /// </summary>
    public ManagementFactory? ManagementFactory { get; private set; }
    
    /// <summary>
    /// PluginMenuService实例
    /// </summary>
    public PluginMenuService? PluginMenuService { get; private set; }
    
    /// <summary>
    /// 初始化AppServices
    /// </summary>
    /// <param name="factory">ManagementFactory实例</param>
    /// <param name="pluginMenuService">PluginMenuService实例</param>
    public static void Initialize(ManagementFactory factory, PluginMenuService pluginMenuService)
    {
        _instance = new AppServices
        {
            ManagementFactory = factory,
            PluginMenuService = pluginMenuService
        };
    }
}