using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Events;
using MyPlugTest.Models;

namespace MyPlugTest.ViewModels;

public partial class TestMessageReceiveViewModel : Document, IDisposable
{
    // 列表数据源
    [ObservableProperty]
    private ObservableCollection<MessageItem> _messages = [];
    private readonly IDisposable _eventSubscription;
    private readonly IDocumentLifetime? _documentLifetime;
    private int _disposed;
    private int _messageIdCounter = 1;
    public TestMessageReceiveViewModel(
        IHostEventBus eventBus,
        IDocumentLifetime? documentLifetime = null)
    {
        // 设置标题
        Title = "消息接收测试";

        // 必须使用宿主注入的事件总线；发送 Document 与接收 Document 因此处于同一运行时。
        // 返回令牌由当前 Document 持有，Scope 释放 Document 时会确定地结束这条强引用订阅。
        _documentLifetime = documentLifetime;
        _eventSubscription = eventBus.Subscribe<RequestResponseMessage>(OnRequestResponseMessageReceived);
    }
    
    /// <summary>
    /// 处理接收到的请求响应消息
    /// </summary>
    private void OnRequestResponseMessageReceived(RequestResponseMessage message)
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
        // 根级事件总线的生命周期长于单个 Document，必须在 Scope 释放时主动解绑。消息接收
        // 与 Dispatcher 回调都另有 IsClosing 二次门禁，因此已经入队但尚未执行的回调也会被
        // 丢弃；这里仅释放自己的令牌，不会影响其他仍打开的消息接收 Document。
        _eventSubscription.Dispose();
    }
}
