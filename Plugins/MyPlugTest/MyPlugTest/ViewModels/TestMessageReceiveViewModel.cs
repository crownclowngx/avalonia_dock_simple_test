using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MyAvaloniaManagement.PluginSdk;
using MyPlugTest.Messaging;
using MyPlugTest.Models;

namespace MyPlugTest.ViewModels;

/// <summary>接收当前 MyPlugTest 插件 Provider 中请求结果的普通插件 Document 模型。</summary>
/// <remarks>
/// 每个 Document Scope 持有一枚独立订阅令牌。Scope 关闭只释放本实例的令牌；其他接收 Document
/// 继续订阅同一个插件私有事件总线。事件总线同步调用处理器，而集合属于 UI，因此后台发布在模型边界
/// 切回 Avalonia Dispatcher，已经排队的回调还会用关闭状态进行第二次门控。
/// </remarks>
public sealed class TestMessageReceiveViewModel : ObservableObject, IPluginDocument, IDisposable
{
    private readonly IDisposable _eventSubscription;
    private readonly IDocumentLifetime _documentLifetime;
    private int _disposed;
    private int _messageIdCounter = 1;
    private string _title = "消息接收测试";

    /// <summary>创建并立即取得当前 Document Scope 所拥有的事件订阅。</summary>
    public TestMessageReceiveViewModel(
        IMyPlugTestEventBus eventBus,
        IDocumentLifetime documentLifetime)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        _documentLifetime = documentLifetime ?? throw new ArgumentNullException(nameof(documentLifetime));
        _eventSubscription = eventBus.Subscribe<RequestResponseMessage>(OnRequestResponseMessageReceived);
    }

    /// <summary>获取当前 Document 已接收的消息集合。</summary>
    public ObservableCollection<MessageItem> Messages { get; } = [];

    /// <inheritdoc />
    public DocumentPresentationState Presentation => new(_title);

    /// <inheritdoc />
    public event EventHandler? PresentationChanged;

    /// <inheritdoc />
    public ValueTask InitializeAsync(
        DocumentActivation activation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();
        if (activation is not NewDocumentActivation)
        {
            // 消息集合只属于当前 Scope 的瞬态状态，不从 Document envelope 恢复。
            throw new NotSupportedException("消息接收测试只支持新建激活。");
        }

        SetPresentationTitle(string.IsNullOrWhiteSpace(activation.Title)
            ? "消息接收测试"
            : activation.Title);
        return ValueTask.CompletedTask;
    }

    private void OnRequestResponseMessageReceived(RequestResponseMessage message)
    {
        if (IsClosing)
        {
            return;
        }

        var newMessage = new MessageItem
        {
            // 总线不强制发布线程；并发后台发布先以原子计数分配身份，集合写入再统一切回 UI。
            Id = (Interlocked.Increment(ref _messageIdCounter) - 1).ToString(),
            Content = $"[{message.Timestamp:HH:mm:ss}] {message.RequestUrl}",
            IsRead = false,
        };

        if (Dispatcher.UIThread.CheckAccess())
        {
            if (!IsClosing)
            {
                Messages.Add(newMessage);
            }
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsClosing)
            {
                Messages.Add(newMessage);
            }
        });
    }

    private void SetPresentationTitle(string title)
    {
        if (string.Equals(_title, title, StringComparison.Ordinal))
        {
            return;
        }

        _title = title;
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsClosing =>
        Volatile.Read(ref _disposed) != 0 || _documentLifetime.IsClosing;

    /// <summary>释放当前实例拥有的订阅令牌；重复调用不影响其他订阅者。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _eventSubscription.Dispose();
        }
    }
}
