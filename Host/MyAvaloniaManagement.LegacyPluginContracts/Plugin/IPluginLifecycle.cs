namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>
/// 由宿主统一管理的可选插件级后台生命周期。
/// </summary>
/// <remarks>
/// 实现不声明插件身份；宿主根据调用 <see cref="IPluginRegistrationContext.AddLifecycle{TLifecycle}"/>
/// 时绑定的 manifest 身份建立生命周期计划。没有显式登记的实现不会获得回调。
/// </remarks>
public interface IPluginLifecycle
{
    /// <summary>
    /// 初始化插件级后台服务。实现必须保持幂等，且不得依赖 Tool 或 Document 的视觉树生命周期。
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 停止并等待插件级后台服务退出。返回前应确保插件持有的后台工作不再访问待释放资源。
    /// </summary>
    Task ShutdownAsync(CancellationToken cancellationToken);
}
