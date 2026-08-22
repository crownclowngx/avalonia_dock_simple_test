namespace BiliDownloader.Messaging;

/// <summary>定义只在 BiliDownloader 插件 Provider 内可解析的同步事件通信端口。</summary>
/// <remarks>
/// 登录、提交、进度、状态和删除消息全部由 BiliDownloader 自己拥有。该端口不进入 Host SDK，
/// 因而 Host 与其他插件既不需要理解这些消息，也不会意外成为它们的路由或版本所有者。
/// </remarks>
public interface IBiliDownloaderEventBus
{
    /// <summary>在调用线程按订阅顺序同步发布非空事件。</summary>
    /// <typeparam name="TEvent">BiliDownloader 内部的精确事件类型。</typeparam>
    /// <param name="event">要发布的事件实例。</param>
    void Publish<TEvent>(TEvent @event) where TEvent : class;

    /// <summary>订阅精确事件类型，并返回由订阅者负责释放的幂等令牌。</summary>
    /// <typeparam name="TEvent">只接收该精确类型，不匹配其基类或派生类。</typeparam>
    /// <param name="handler">在发布线程同步执行的处理器。</param>
    /// <returns>随订阅者生命周期释放的令牌；允许重复释放。</returns>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}
