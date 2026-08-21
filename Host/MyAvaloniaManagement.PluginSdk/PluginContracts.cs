namespace MyAvaloniaManagement.PluginSdk;

/// <summary>提供当前 HostRuntime 内的强类型同步事件发布与订阅能力。</summary>
/// <remarks>
/// 总线只派发精确事件类型，不切换线程、不等待异步处理、不重试也不吞异常。处理器异常会原样
/// 返回发布者并停止后续派发；订阅者必须保存并释放返回令牌，明确承担订阅所有权。
/// </remarks>
public interface IHostEventBus
{
    /// <summary>在调用线程按订阅顺序发布非空事件。</summary>
    /// <typeparam name="TEvent">由发布方和订阅方共同引用的精确事件类型。</typeparam>
    /// <param name="event">要同步发布的事件实例。</param>
    /// <exception cref="ArgumentNullException">事件为 null。</exception>
    /// <remarks>实现不得切换线程；处理器异常应原样返回，并停止本次快照中的后续处理器。</remarks>
    void Publish<TEvent>(TEvent @event) where TEvent : class;

    /// <summary>订阅精确事件类型，并返回用于取消本条订阅的幂等释放令牌。</summary>
    /// <typeparam name="TEvent">只接收此精确类型的事件，不匹配其基类或派生类。</typeparam>
    /// <param name="handler">在发布线程同步执行的非空处理器。</param>
    /// <returns>由订阅者持有并随自身生命周期释放的令牌；允许重复释放。</returns>
    /// <exception cref="ArgumentNullException">处理器为 null。</exception>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}

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
