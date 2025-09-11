using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.Message;
using MyPlugTest.Models;

namespace MyPlugTest.ViewModels;

public partial class TestMessageReceiveViewModel : Document
{
    // 列表数据源
    [ObservableProperty]
    private ObservableCollection<MessageItem> _messages = new();
    private readonly IMessengerService _messengerService;
    private int _messageIdCounter = 1;
    public TestMessageReceiveViewModel(IMessengerService messengerService = null)
    {
        // 设置标题
        Title = "消息接收测试";
            
        // 初始化消息列表
        _messages = new ObservableCollection<MessageItem>();
        
        // 使用传入的messengerService或创建默认实例
        _messengerService = messengerService ?? new MessengerService();
        
        // 注册消息接收器
        _messengerService.Register<TestMessageReceiveViewModel, RequestResponseMessage>(this, OnRequestResponseMessageReceived);

    }
    
    /// <summary>
    /// 处理接收到的请求响应消息
    /// </summary>
    private void OnRequestResponseMessageReceived(TestMessageReceiveViewModel receiver, RequestResponseMessage message)
    {
        // 创建新的消息项并添加到集合
        var newMessage = new MessageItem
        {
            Id = _messageIdCounter++.ToString(),
            Content = $"[{message.Timestamp:HH:mm:ss}] {message.RequestUrl}",
            IsRead = false
        };
        
        // 在UI线程上添加消息 - 使用Avalonia正确的方式
        if (Dispatcher.UIThread.CheckAccess())
        {
            // 已经在UI线程上，可以直接操作
            Messages.Add(newMessage);
        }
        else
        {
            // 不在UI线程上，需要调度到UI线程
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Messages.Add(newMessage);
            });
        }
    }
}