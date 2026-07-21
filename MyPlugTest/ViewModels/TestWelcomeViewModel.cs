using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Save;
using MyPlugTest.Constants;
using MyPlugTest.Models;
using MyPlugTest.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyPlugTest.ViewModels;

public class TestWelcomeViewModel : Document, ISavableDocument
{

    public string SaveDocumentTypeId => SaveDocumentTypeIdConstant.TestWelcomeDocumentId;
    public string FilePath { get; set; } = string.Empty;
    
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
        set => SetProperty(ref _url, value);
    }

    public string ResponseContent
    {
        get => _responseContent;
        set => SetProperty(ref _responseContent, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public IRelayCommand SendRequestCommand { get; }
    
    public DocumentSaveData CreateSaveDocumentMetaData(string filePath)
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
        // 创建文档保存数据
        var saveData = new DocumentSaveData
        {
            DocumentTypeId = SaveDocumentTypeId,
            Title = Title,
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(saveDataObject),
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "1.0" })
        };
        IsModified = false;
        return saveData;
    }
    
    public void LoadDocumentByMetaData(DocumentSaveData saveData)
    {
        try
        {
            var viewModelData = JsonConvert.DeserializeObject<JObject>(saveData.Content);
            if (viewModelData == null)
            {
                return;
            }

            // 使用明确的 JSON 节点读取保存数据，避免 dynamic 在字段缺失时产生空引用风险。
            Url = viewModelData["Url"]?.ToString() ?? "https://example.com";
            ResponseContent = viewModelData["ResponseContent"]?.ToString() ?? string.Empty;

            // 每次加载都以保存文件为准重建当前 Document 的历史记录投影。
            UrlHistory.HistoryItems.Clear();
            if (viewModelData["HistoryItems"] is JArray historyItems)
            {
                foreach (var item in historyItems)
                {
                    var url = item["Url"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        UrlHistory.HistoryItems.Add(new UrlHistoryItem(url));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 错误处理
            Console.WriteLine($"加载文档错误: {ex.Message}");
        }
    }
    
    public TestWelcomeViewModel(
        IMessengerService messengerService,
        UrlHistoryViewModel urlHistory,
        IUrlContentService urlContentService)
    {
        // 三个依赖均由 MyPlugTestPluginModule 提供；这里不保留手工 new 的回退路径，
        // 从而保证所有 Document 都使用宿主的共享消息总线和统一的网络服务所有权。
        _messengerService = messengerService;
        _urlContentService = urlContentService;
        UrlHistory = urlHistory;
        SendRequestCommand = new AsyncRelayCommand(SendRequestAsync);
    }
    
    private async Task SendRequestAsync()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            ResponseContent = "请输入有效的网址";
            return;
        }

        try
        {
            IsLoading = true;
            ResponseContent = "正在发送请求...";

            // 确保URL格式正确
            string url = Url;
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "https://" + url;
            }

            // 网络副作用通过注入服务执行，ViewModel 只负责界面状态和消息通信。
            string content = await _urlContentService.GetStringAsync(url);
            ResponseContent = content;
            // 添加URL到历史记录
            UrlHistory.AddUrl(url);
            IsModified = true;
            // 发送成功消息到消息总线
            _messengerService.Send(new RequestResponseMessage(content, url, true));
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
            IsLoading = false;
        }
    }

}
