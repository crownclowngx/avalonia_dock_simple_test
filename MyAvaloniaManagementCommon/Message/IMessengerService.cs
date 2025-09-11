using CommunityToolkit.Mvvm.Messaging;

namespace MyAvaloniaManagementCommon.Message;

/// <summary>
/// 消息总线服务接口，用于在应用程序和插件之间共享消息通信
/// </summary>
public interface IMessengerService
{
    /// <summary>
    /// 获取底层的IMessenger实例
    /// </summary>
    IMessenger Messenger { get; }
    
    /// <summary>
    /// 发送消息
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <param name="message">消息实例</param>
    void Send<TMessage>(TMessage message) where TMessage : class;
    
    /// <summary>
    /// 注册消息接收者
    /// </summary>
    /// <typeparam name="TReceiver">接收者类型</typeparam>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <param name="receiver">接收者实例</param>
    /// <param name="handler">消息处理方法</param>
    void Register<TReceiver, TMessage>(TReceiver receiver, MessageHandler<TReceiver, TMessage> handler) 
        where TReceiver : class 
        where TMessage : class;
    
    /// <summary>
    /// 注销消息接收者
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <param name="receiver">接收者实例</param>
    void Unregister<TMessage>(object receiver) where TMessage : class;
    
    /// <summary>
    /// 注销所有消息接收
    /// </summary>
    /// <param name="receiver">接收者实例</param>
    void UnregisterAll(object receiver);
}

/// <summary>
/// 消息处理委托
/// </summary>
/// <typeparam name="TReceiver">接收者类型</typeparam>
/// <typeparam name="TMessage">消息类型</typeparam>
/// <param name="receiver">接收者实例</param>
/// <param name="message">消息实例</param>
public delegate void MessageHandler<TReceiver, TMessage>(TReceiver receiver, TMessage message)
    where TReceiver : class
    where TMessage : class;