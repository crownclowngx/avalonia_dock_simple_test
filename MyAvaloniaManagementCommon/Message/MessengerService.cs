using CommunityToolkit.Mvvm.Messaging;

namespace MyAvaloniaManagementCommon.Message;

/// <summary>
/// 消息总线服务的实现，基于CommunityToolkit.Mvvm的WeakReferenceMessenger
/// </summary>
public class MessengerService : IMessengerService
{
    private readonly IMessenger _messenger;
    
    /// <summary>
    /// 获取底层的IMessenger实例
    /// </summary>
    public IMessenger Messenger => _messenger;
    
    /// <summary>
    /// 构造函数
    /// </summary>
    public MessengerService()
    {
        // 使用WeakReferenceMessenger可以避免内存泄漏问题
        _messenger = WeakReferenceMessenger.Default;
    }
    
    /// <summary>
    /// 发送消息
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <param name="message">消息实例</param>
    public void Send<TMessage>(TMessage message) where TMessage : class
    {
        _messenger.Send(message);
    }
    
    /// <summary>
    /// 注册消息接收者
    /// </summary>
    /// <typeparam name="TReceiver">接收者类型</typeparam>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <param name="receiver">接收者实例</param>
    /// <param name="handler">消息处理方法</param>
    public void Register<TReceiver, TMessage>(TReceiver receiver, MessageHandler<TReceiver, TMessage> handler) 
        where TReceiver : class 
        where TMessage : class
    {
        _messenger.Register<TReceiver, TMessage>(receiver, (r, m) => handler(r, m));
    }
    
    /// <summary>
    /// 注销消息接收者
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <param name="receiver">接收者实例</param>
    public void Unregister<TMessage>(object receiver) where TMessage : class
    {
        _messenger.Unregister<TMessage>(receiver);
    }
    
    /// <summary>
    /// 注销所有消息接收
    /// </summary>
    /// <param name="receiver">接收者实例</param>
    public void UnregisterAll(object receiver)
    {
        _messenger.UnregisterAll(receiver);
    }
}