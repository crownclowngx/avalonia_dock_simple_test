namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>
/// 将宿主验证的 manifest 身份与一个生命周期实例绑定为不可变注册项。
/// </summary>
/// <param name="PluginId">生命周期所属的 manifest 插件身份。</param>
/// <param name="Lifecycle">由宿主依赖注入容器创建并持有的生命周期实例。</param>
/// <remarks>
/// 该值由宿主的 Plugin Registry 产生。身份与实现分离可以避免插件代码重复声明或伪造所有权，
/// 同时允许生命周期管理器保持为不依赖具体宿主加载器的公共协调组件。
/// </remarks>
public sealed record PluginLifecycleRegistration(
    PluginId PluginId,
    IPluginLifecycle Lifecycle);
