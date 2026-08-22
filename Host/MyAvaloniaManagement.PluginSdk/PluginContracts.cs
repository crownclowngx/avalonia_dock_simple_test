namespace MyAvaloniaManagement.PluginSdk;

/// <summary>定义由 Host 统一启动和停止的可选插件级后台生命周期。</summary>
/// <remarks>
/// 生命周期不声明身份、顺序或其他插件依赖。Host 根据 manifest 身份确定性排序并负责超时、状态和诊断，
/// 从而让插件只实现自身资源的启动与停止职责。
/// </remarks>
public interface IPluginLifecycle
{
    /// <summary>初始化插件级后台资源；实现不得依赖 Document 或 Tool 的视觉树生命周期。</summary>
    /// <param name="cancellationToken">由 Host 在启动超时或进程关闭时触发的协作取消令牌。</param>
    /// <returns>表示初始化完成的任务；返回前插件贡献不会进入可用状态。</returns>
    /// <remarks>调用顺序、超时、异常诊断与失败隔离均由 Host 编排，实现只拥有自己的资源。</remarks>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>停止后台工作，并在返回前停止访问即将释放的插件资源。</summary>
    /// <param name="cancellationToken">由 Host 在关闭超时时触发的协作取消令牌。</param>
    /// <returns>表示插件级资源已停止使用的任务。</returns>
    /// <remarks>Host 负责逆序调用和最终释放容器；插件不应主动释放 Host 端口。</remarks>
    Task ShutdownAsync(CancellationToken cancellationToken);
}
