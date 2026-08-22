using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.PluginSdk;
using MyPlugTest.Models;
using MyPlugTest.Messaging;
using MyPlugTest.Persistence;
using MyPlugTest.Services;

namespace MyPlugTest.ViewModels;

/// <summary>
/// 展示 URL 请求、历史记录与可持久化内容的普通插件 Document 模型。
/// </summary>
/// <remarks>
/// 本类型只拥有当前 Document 的界面状态、命令和事件发布；Dock 标题、路径、信封与原子文件事务均由
/// Host Adapter/持久化链拥有。每个实例位于独立插件 Scope，关闭信号由 <see cref="IDocumentLifetime"/>
/// 提供，最终释放会取消进行中的请求并解除集合订阅。
/// </remarks>
public sealed class TestWelcomeViewModel : ObservableObject, IPersistablePluginDocument, IDisposable
{
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly object _revisionLock = new();
    private readonly IMyPlugTestEventBus _eventBus;
    private readonly IUrlContentService _urlContentService;
    private readonly IDocumentLifetime _documentLifetime;
    private int _disposed;
    private bool _isRestoring;
    private long _contentRevision;
    private long _acceptedRevision;
    private string _title = "Test欢迎";
    private string _url = "https://example.com";
    private string _responseContent = string.Empty;
    private bool _isLoading;

    /// <summary>初始化当前 Document 的全部必需依赖。</summary>
    public TestWelcomeViewModel(
        IMyPlugTestEventBus eventBus,
        UrlHistoryViewModel urlHistory,
        IUrlContentService urlContentService,
        IDocumentLifetime documentLifetime)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        UrlHistory = urlHistory ?? throw new ArgumentNullException(nameof(urlHistory));
        _urlContentService = urlContentService ?? throw new ArgumentNullException(nameof(urlContentService));
        _documentLifetime = documentLifetime ?? throw new ArgumentNullException(nameof(documentLifetime));
        UrlHistory.HistoryItems.CollectionChanged += OnHistoryChanged;
        SendRequestCommand = new AsyncRelayCommand(SendRequestAsync);
    }

    /// <inheritdoc />
    public DocumentPresentationState Presentation => new(_title);

    /// <inheritdoc />
    public event EventHandler? PresentationChanged;

    /// <inheritdoc />
    public bool IsDirty
    {
        get
        {
            lock (_revisionLock)
            {
                return _contentRevision != _acceptedRevision;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? IsDirtyChanged;

    /// <summary>获取只属于当前 Document Scope 的 URL 历史。</summary>
    public UrlHistoryViewModel UrlHistory { get; }

    /// <summary>获取或设置请求地址。</summary>
    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>获取或设置最近一次响应正文。</summary>
    public string ResponseContent
    {
        get => _responseContent;
        set
        {
            if (SetProperty(ref _responseContent, value))
            {
                MarkDirty();
            }
        }
    }

    /// <summary>获取当前是否正在执行 URL 请求。</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>获取发送当前 URL 请求的异步命令。</summary>
    public IAsyncRelayCommand SendRequestCommand { get; }

    /// <inheritdoc />
    public ValueTask InitializeAsync(
        DocumentActivation activation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();

        // Codec 先验证出完整临时状态，再开始修改可观察属性。这样 payload 尾部损坏不会留下
        // URL 已恢复但历史记录只恢复一半的状态；Host 还会在异常时释放未发布 Scope。
        var restoredState = activation switch
        {
            NewDocumentActivation => null,
            RestoreDocumentActivation restore =>
                TestWelcomeDocumentContentCodec.Decode(restore.RestoredContent),
            _ => throw new NotSupportedException("Test Welcome 收到未知 Document 激活类型。"),
        };

        SetPresentationTitle(string.IsNullOrWhiteSpace(activation.Title) ? "Test欢迎" : activation.Title);
        if (restoredState is not null)
        {
            ApplyRestoredState(restoredState);
        }
        else
        {
            ResetRevisionState();
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documentLifetime.ClosingToken.ThrowIfCancellationRequested();
            var revisionBeforeCapture = ReadCurrentRevision();

            // 先枚举为数组，确保编码不依赖后续集合变化；DocumentContent 随后再次克隆
            // JsonElement。若捕获期间任一持久字段又发生变化，下面的修订复核会丢弃本轮内容
            // 并重新捕获，Host 因而永远不会拿到“旧修订号 + 不稳定内容”的确认令牌。
            var historyUrls = UrlHistory.HistoryItems.Select(static item => item.Url).ToArray();
            var content = TestWelcomeDocumentContentCodec.Encode(
                _url,
                _responseContent,
                historyUrls);
            var revisionAfterCapture = ReadCurrentRevision();
            if (revisionBeforeCapture == revisionAfterCapture)
            {
                return ValueTask.FromResult(
                    new DocumentSaveSnapshot(revisionAfterCapture, content));
            }
        }
    }

    /// <inheritdoc />
    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var dirtyChanged = false;
        lock (_revisionLock)
        {
            // Host 只允许确认自己实际写入的修订。当前值更大时说明保存期间又发生了编辑，
            // 此时不能为了“保存成功”而清除新的 Dirty；重复确认当前修订则保持幂等。
            if (_contentRevision != savedRevision.Value)
            {
                return;
            }

            dirtyChanged = _acceptedRevision != _contentRevision;
            _acceptedRevision = _contentRevision;
        }

        if (dirtyChanged)
        {
            RaiseDirtyChanged();
        }
    }

    private async Task SendRequestAsync(CancellationToken commandToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            _disposeCts.Token,
            _documentLifetime.ClosingToken);
        var cancellationToken = linked.Token;

        if (string.IsNullOrWhiteSpace(Url))
        {
            if (!IsClosing)
            {
                ResponseContent = "请输入有效的网址";
            }
            return;
        }

        try
        {
            // 命令可能在用户确认关闭与控件解绑之间被触发；先观察联合令牌，再修改任何可观察状态。
            cancellationToken.ThrowIfCancellationRequested();
            IsLoading = true;
            ResponseContent = "正在发送请求...";

            var url = Url;
            if (!url.StartsWith("http://", StringComparison.Ordinal) &&
                !url.StartsWith("https://", StringComparison.Ordinal))
            {
                url = "https://" + url;
            }

            var content = await _urlContentService.GetStringAsync(url, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosing)
            {
                return;
            }

            ResponseContent = content;
            UrlHistory.AddUrl(url);
            _eventBus.Publish(new RequestResponseMessage(content, url, true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Document 关闭和命令取消都属于正常协作取消。关闭后不能再更新 UI，也不能发布一个
            // 伪造的失败事件；Host 只负责发信号，网络 Task 的退出责任仍属于本模型。
        }
        catch (UrlContentRequestException exception)
        {
            // 某些网络实现会把关闭竞争表现为领域异常而不是 OperationCanceledException。
            // Scope 一旦进入关闭态，异常详情也不能再回写已经解绑的 View。
            if (!IsClosing)
            {
                ResponseContent = $"请求失败: 状态码 {exception.StatusCode}, 错误信息: {exception.ResponseContent}";
            }
        }
        catch (Exception exception)
        {
            if (!IsClosing)
            {
                ResponseContent = "请求异常: " + exception.Message;
            }
        }
        finally
        {
            if (!IsClosing)
            {
                IsLoading = false;
            }
        }
    }

    private void ApplyRestoredState(TestWelcomeDocumentState state)
    {
        _isRestoring = true;
        try
        {
            Url = state.Url;
            ResponseContent = state.ResponseContent;
            UrlHistory.HistoryItems.Clear();
            foreach (var restoredUrl in state.HistoryUrls)
            {
                UrlHistory.HistoryItems.Add(new UrlHistoryItem(restoredUrl));
            }
            ResetRevisionState();
        }
        finally
        {
            _isRestoring = false;
        }
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

    private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs args) => MarkDirty();

    private void MarkDirty()
    {
        if (!_isRestoring && !IsClosing)
        {
            var dirtyChanged = false;
            lock (_revisionLock)
            {
                var wasDirty = _contentRevision != _acceptedRevision;
                _contentRevision = checked(_contentRevision + 1);
                dirtyChanged = !wasDirty;
            }

            if (dirtyChanged)
            {
                RaiseDirtyChanged();
            }
        }
    }

    private DocumentRevision ReadCurrentRevision()
    {
        lock (_revisionLock)
        {
            return new DocumentRevision(_contentRevision);
        }
    }

    private void ResetRevisionState()
    {
        var dirtyChanged = false;
        lock (_revisionLock)
        {
            dirtyChanged = _contentRevision != _acceptedRevision;
            _acceptedRevision = _contentRevision;
        }

        if (dirtyChanged)
        {
            RaiseDirtyChanged();
        }
    }

    private void RaiseDirtyChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsClosing =>
        Volatile.Read(ref _disposed) != 0 || _documentLifetime.IsClosing;

    /// <summary>
    /// 取消当前 Scope 拥有的请求并解除集合事件；重复释放保持幂等。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        SendRequestCommand.Cancel();
        UrlHistory.HistoryItems.CollectionChanged -= OnHistoryChanged;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}
