namespace MyPlugTest.Messaging;

/// <summary>定义只在 MyPlugTest 插件 Provider 内可解析的同步事件通信端口。</summary>
/// <remarks>
/// 此接口位于插件程序集而不是 Host SDK，明确表示消息契约及其版本由 MyPlugTest 自己拥有。
/// 发布与订阅只匹配精确事件类型；实现不负责 UI 线程切换、异步等待、重试或异常吞并。
/// </remarks>
public interface IMyPlugTestEventBus
{
    /// <summary>在调用线程按订阅顺序同步发布非空事件。</summary>
    /// <typeparam name="TEvent">MyPlugTest 内部发布方与订阅方共同引用的精确事件类型。</typeparam>
    /// <param name="event">要发布的事件实例。</param>
    void Publish<TEvent>(TEvent @event) where TEvent : class;

    /// <summary>订阅精确事件类型，并返回由订阅者负责释放的幂等令牌。</summary>
    /// <typeparam name="TEvent">只接收该精确类型，不匹配其基类或派生类。</typeparam>
    /// <param name="handler">在发布线程同步执行的处理器。</param>
    /// <returns>随订阅者生命周期释放的令牌；允许重复释放。</returns>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}
