using CommunityToolkit.Mvvm.Input;
using System.Collections.Specialized;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Save;
using MyPlugTest.Models;
using MyPlugTest.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyPlugTest.ViewModels;

public class TestWelcomeViewModel : Document, ISavableDocument, IDocumentSaveState, IDisposable
{
    private const int CurrentContentSchemaVersion = 1;
    // 本地 CTS 与宿主 ClosingToken 共同组成操作令牌：正式 Scope 关闭由宿主触发，直接构造
    // 场景则由 Dispose 触发。两条路径统一后，HTTP 结果、历史记录和 Messenger 消息都通过
    // 同一个 IsClosing 门禁决定是否仍可提交，避免出现“请求已取消但成功消息仍迟到”的分裂状态。
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly IDocumentLifetime? _documentLifetime;
    private int _disposed;
    private bool _isRestoring;

    public bool IsDirty => IsModified;
    
    private string _url = "https://example.com";
    private string _responseContent = "";
    private bool _isLoading = false;
    private readonly IMessengerService _messengerService;
    private readonly IUrlContentService _urlContentService;

    // 每个 Document 注入自己的瞬态 URL 历史记录 ViewModel，避免多个文档共享可变集合。
    public UrlHistoryViewModel UrlHistory { get; }
    


    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value)) MarkDirty();
        }
    }

    public string ResponseContent
    {
        get => _responseContent;
        set
        {
            if (SetProperty(ref _responseContent, value)) MarkDirty();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public IAsyncRelayCommand SendRequestCommand { get; }
    
    public DocumentContentSnapshot CreateContentSnapshot()
    {
        var saveDataObject = new
        {
            Url = _url,
            ResponseContent = _responseContent,
            // 保存UrlHistory的状态
            HistoryItems = UrlHistory.HistoryItems.Select(item => new
            {
                item.Url,
                item.DisplayTime,
                item.RequestTime
            }).ToList()
        };
        // 插件只返回自己的内容版本和正文。标题、时间及稳定身份由宿主信封统一填充，
        // 避免插件与 Registry 各自维护一份可能漂移的所有权事实。
        return new DocumentContentSnapshot(
            CurrentContentSchemaVersion,
            JsonConvert.SerializeObject(saveDataObject));
    }
    
    public void RestoreContent(DocumentContentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // 内容 schema 是该 Document 自己的整数协议，与插件包版本、程序集版本
        // 或宿主信封 schema 无关。当前没有真实旧内容，因此精确只接受 1；
        // 未来若出现旧版，应在此显式增加读取分支，而不是默认尝试当前结构。
        if (snapshot.ContentSchemaVersion != CurrentContentSchemaVersion)
        {
            throw new DocumentLoadException("测试文档内容版本不受支持。");
        }

        _isRestoring = true;
        try
        {
            var viewModelData = JsonConvert.DeserializeObject<JObject>(snapshot.Payload);
            if (viewModelData == null)
            {
                throw new DocumentLoadException("测试文档内容为空。");
            }

            // 三个字段都是当前 schema 1 自己写出的业务必填项。这里不为缺失字段补默认值，
            // 否则截断文件会被悄悄当成合法空文档，用户下一次保存时反而覆盖原有状态。
            if (viewModelData["Url"] is not { Type: JTokenType.String } urlToken ||
                string.IsNullOrWhiteSpace(urlToken.Value<string>()) ||
                viewModelData["ResponseContent"] is not { Type: JTokenType.String } responseToken ||
                viewModelData["HistoryItems"] is not JArray historyItems)
            {
                throw new DocumentLoadException("测试文档缺少必填业务字段。");
            }

            // 先把整份数组验证到临时集合，再提交 ViewModel。这样后段记录损坏时不会
            // 留下“URL 已更新、历史只加载一半”的部分状态。
            var restoredUrls = new List<string>(historyItems.Count);
            foreach (var item in historyItems)
            {
                if (item is not JObject historyItem ||
                    historyItem["Url"] is not { Type: JTokenType.String } historyUrlToken ||
                    string.IsNullOrWhiteSpace(historyUrlToken.Value<string>()))
                {
                    throw new DocumentLoadException("测试文档的历史记录字段无效。");
                }

                restoredUrls.Add(historyUrlToken.Value<string>()!);
            }

            Url = urlToken.Value<string>()!;
            ResponseContent = responseToken.Value<string>()!;
            UrlHistory.HistoryItems.Clear();
            foreach (var restoredUrl in restoredUrls)
            {
                UrlHistory.HistoryItems.Add(new UrlHistoryItem(restoredUrl));
            }
            IsModified = false;
        }
        catch (DocumentLoadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new DocumentLoadException(
                "测试文档结构损坏或包含无效字段。",
                exception);
        }
        finally
        {
            _isRestoring = false;
        }
    }
    
    public TestWelcomeViewModel(
        IMessengerService messengerService,
        UrlHistoryViewModel urlHistory,
        IUrlContentService urlContentService,
        IDocumentLifetime? documentLifetime = null)
    {
        // 三个依赖均由 MyPlugTestPluginModule 提供；这里不保留手工 new 的回退路径，
        // 从而保证所有 Document 都使用宿主的共享消息总线和统一的网络服务所有权。
        _messengerService = messengerService;
        _urlContentService = urlContentService;
        _documentLifetime = documentLifetime;
        UrlHistory = urlHistory;
        UrlHistory.HistoryItems.CollectionChanged += OnHistoryChanged;
        SendRequestCommand = new AsyncRelayCommand(SendRequestAsync);
    }
    
    private async Task SendRequestAsync(CancellationToken commandToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            _disposeCts.Token,
            _documentLifetime?.ClosingToken ?? CancellationToken.None);
        var cancellationToken = linked.Token;

        if (string.IsNullOrWhiteSpace(Url))
        {
            if (!IsClosing) ResponseContent = "请输入有效的网址";
            return;
        }

        try
        {
            IsLoading = true;
            ResponseContent = "正在发送请求...";
            cancellationToken.ThrowIfCancellationRequested();

            // 确保URL格式正确
            string url = Url;
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "https://" + url;
            }

            // 网络副作用通过注入服务执行，ViewModel 只负责界面状态和消息通信。
            string content = await _urlContentService.GetStringAsync(url, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosing) return;
            ResponseContent = content;
            // 添加URL到历史记录
            UrlHistory.AddUrl(url);
            IsModified = true;
            // 发送成功消息到消息总线
            _messengerService.Send(new RequestResponseMessage(content, url, true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 关闭 Document 是用户结束当前页面生命周期，不是 HTTP 请求故障。保持静默可以
            // 避免关闭后覆盖最后一次有效响应，也不会向共享消息总线广播一个伪造的失败结果。
        }
        catch (UrlContentRequestException ex)
        {
            // 保持迁移前的错误展示格式，同时不让 ViewModel 依赖 Flurl 的异常类型。
            ResponseContent = $"请求失败: 状态码 {ex.StatusCode}, 错误信息: {ex.ResponseContent}";
        }
        catch (Exception ex)
        {
            // 处理其他异常
            ResponseContent = "请求异常: " + ex.Message;
        }
        finally
        {
            if (!IsClosing) IsLoading = false;
        }
    }

    private bool IsClosing => Volatile.Read(ref _disposed) != 0 || _documentLifetime?.IsClosing == true;

    public void AcceptChanges() => IsModified = false;

    private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        MarkDirty();

    private void MarkDirty()
    {
        if (!_isRestoring && !IsClosing)
        {
            IsModified = true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // 先取消命令，再取消本地令牌，使命令基础设施和服务调用都能尽快观察关闭；不在这里
        // 等待 HTTP Task 完成，迟到结果由 SendRequestAsync 中的关闭检查负责丢弃。
        SendRequestCommand.Cancel();
        UrlHistory.HistoryItems.CollectionChanged -= OnHistoryChanged;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

}
