using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyPlugTest.Models;

namespace MyPlugTest.ViewModels;

public partial class TestMessageReceiveViewModel : Document, IDisposable
{
    // 列表数据源
    [ObservableProperty]
    private ObservableCollection<MessageItem> _messages = [];
    private readonly IMessengerService _messengerService;
    private readonly IDocumentLifetime? _documentLifetime;
    private int _disposed;
    private int _messageIdCounter = 1;
    public TestMessageReceiveViewModel(
        IMessengerService messengerService,
        IDocumentLifetime? documentLifetime = null)
    {
        // 设置标题
        Title = "消息接收测试";

        // 必须使用宿主注入的共享消息服务；发送 Document 与接收 Document
        // 因此处于同一消息事实源中，插件内部不再创建第二个 MessengerService。
        _messengerService = messengerService;
        _documentLifetime = documentLifetime;
        
        // 注册消息接收器
        _messengerService.Register<TestMessageReceiveViewModel, RequestResponseMessage>(this, OnRequestResponseMessageReceived);

    }
    
    /// <summary>
    /// 处理接收到的请求响应消息
    /// </summary>
    private void OnRequestResponseMessageReceived(TestMessageReceiveViewModel receiver, RequestResponseMessage message)
    {
        if (IsClosing) return;
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
            if (!IsClosing) Messages.Add(newMessage);
        }
        else
        {
            // 不在UI线程上，需要调度到UI线程
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsClosing) Messages.Add(newMessage);
            });
        }
    }

    private bool IsClosing => Volatile.Read(ref _disposed) != 0 || _documentLifetime?.IsClosing == true;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // Messenger 注册的生命周期长于单个 Document，必须在 Scope 释放时主动解绑。消息接收
        // 与 Dispatcher 回调都另有 IsClosing 二次门禁，因此已经入队但尚未执行的回调也会被
        // 丢弃；这里不清空共享 Messenger，更不会影响其他仍打开的消息接收 Document。
        _messengerService.UnregisterAll(this);
    }
}
