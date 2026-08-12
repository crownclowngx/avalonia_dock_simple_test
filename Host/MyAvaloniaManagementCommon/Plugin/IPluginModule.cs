using Microsoft.Extensions.DependencyInjection;

namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>
/// 插件选择接入宿主依赖注入容器时使用的可选入口。
/// <para>
/// 该接口采用显式接入设计：只有实现此接口的插件程序集才会在宿主构建
/// <see cref="IServiceProvider"/> 前获得服务注册机会。未实现此接口的历史插件仍由
/// 原有的无参构造函数和创建策略实例化，宿主不会尝试解析其内部依赖或改变初始化时机。
/// </para>
/// </summary>
public interface IPluginModule
{
    /// <summary>
    /// 插件的稳定标识，用于模块排序、生命周期状态查询和错误诊断。
    /// </summary>
    PluginId PluginId { get; }

    /// <summary>
    /// 将插件需要由宿主统一管理的服务注册到根级依赖注入容器。
    /// 此方法在根级 ServiceProvider 构建前且每次进程启动只调用一次。
    /// </summary>
    void ConfigureServices(IServiceCollection services);
}
