namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>
/// 由宿主统一管理的可选插件生命周期。
/// <para>
/// 只有插件模块主动注册到依赖注入容器中的实现才会参与初始化和关闭；
/// 历史插件不会因为公共程序集新增了此接口而自动获得生命周期回调。
/// </para>
/// </summary>
public interface IPluginLifecycle
{
    /// <summary>
    /// 与模块一致的稳定插件标识。
    /// </summary>
    PluginId PluginId { get; }

    /// <summary>
    /// 初始化顺序。数值较小的插件先初始化，关闭时按成功初始化顺序反向执行。
    /// </summary>
    int Order { get; }

    /// <summary>
    /// 初始化插件级后台服务。实现必须保持幂等，且不得依赖 Tool 或 Document 的视觉树生命周期。
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 停止并等待插件级后台服务退出。返回前应确保插件持有的后台工作不再访问待释放资源。
    /// </summary>
    Task ShutdownAsync(CancellationToken cancellationToken);
}
