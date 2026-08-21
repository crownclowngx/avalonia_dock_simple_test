namespace MyAvaloniaManagementCommon.Events;

/// <summary>
/// 提供宿主进程内的强类型事件发布与订阅能力。
/// </summary>
/// <remarks>
/// <para>
/// 该接口是 Managed Plugin SDK 自有契约，不暴露宿主采用的具体实现或第三方消息组件。
/// 每个宿主运行时拥有独立的事件总线，因此插件只能与当前运行时中的发布者和订阅者通信。
/// </para>
/// <para>
/// 事件在调用 <see cref="Publish{TEvent}"/> 的线程上同步派发，并按订阅顺序调用处理器。
/// 总线不负责线程切换、异步等待、重试或异常吞噬；处理器抛出的异常会原样返回给发布者，
/// 后续处理器不再执行。需要进入 UI 线程或启动异步工作的订阅者必须在自己的边界内明确处理。
/// </para>
/// <para>
/// <see cref="Subscribe{TEvent}"/> 返回的令牌代表一条订阅的所有权。拥有订阅的对象必须保存并
/// 释放该令牌；Document 应让令牌随自身依赖注入作用域一同释放。令牌释放是幂等的，但已经进入
/// 某次发布快照的处理器仍可能执行最后一次，因此 Document 处理器还应检查关闭生命周期。
/// </para>
/// </remarks>
public interface IHostEventBus
{
    /// <summary>
    /// 在当前线程同步发布一个事件。
    /// </summary>
    /// <typeparam name="TEvent">事件的精确契约类型；不会自动派发给其基类或接口订阅者。</typeparam>
    /// <param name="event">要发布的非空事件实例。</param>
    /// <exception cref="ArgumentNullException"><paramref name="event"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ObjectDisposedException">当前宿主运行时的事件总线已经释放。</exception>
    /// <remarks>
    /// 若某个处理器抛出异常，该异常会原样传播给调用方，且本次发布中位于其后的处理器不会执行。
    /// 普通进程内事件不携带统一版本字段；破坏事件语义时应创建新事件类型或提升 SDK 主版本。
    /// </remarks>
    void Publish<TEvent>(TEvent @event) where TEvent : class;

    /// <summary>
    /// 订阅指定精确类型的事件。
    /// </summary>
    /// <typeparam name="TEvent">要接收的事件契约类型。</typeparam>
    /// <param name="handler">在发布线程上同步执行的事件处理器。</param>
    /// <returns>用于取消本条订阅的幂等释放令牌。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ObjectDisposedException">当前宿主运行时的事件总线已经释放。</exception>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}
