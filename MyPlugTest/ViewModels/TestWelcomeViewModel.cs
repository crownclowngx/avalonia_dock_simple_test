using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Flurl.Http;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyPlugTest.Constants;
using MyPlugTest.Models;
using Newtonsoft.Json;

namespace MyPlugTest.ViewModels;

public class TestWelcomeViewModel : Document, ISavableDocument
{

    public string SaveDocumentTypeId => SaveDocumentTypeIdConstant.TestWelcomeDocumentId;
    public string FilePath { get; set; }
    
    private string _url = "https://example.com";
    private string _responseContent = "";
    private bool _isLoading = false;
    private readonly IMessengerService _messengerService;
    // 添加URL历史记录ViewModel
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
            if (saveData != null)
            {
                // 反序列化文档内容
                var viewModelData = JsonConvert.DeserializeObject<dynamic>(saveData.Content);
                if (viewModelData != null)
                {
                    // 恢复URL和响应内容
                    _url = viewModelData.Url?.ToString() ?? "https://example.com";
                    _responseContent = viewModelData.ResponseContent?.ToString() ?? "";
                    OnPropertyChanged(nameof(Url));
                    OnPropertyChanged(nameof(ResponseContent));
                    
                    // 恢复UrlHistory的状态
                    if (viewModelData.HistoryItems != null)
                    {
                        // 清空现有历史记录
                        UrlHistory.HistoryItems.Clear();
                        
                        // 从保存的数据中添加历史记录项
                        foreach (var item in viewModelData.HistoryItems)
                        {
                            string url = item.Url?.ToString();
                            if (!string.IsNullOrEmpty(url))
                            {
                                // 创建新的历史记录项并添加
                                UrlHistory.HistoryItems.Add(new UrlHistoryItem(url));
                            }
                        }
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
    
    public TestWelcomeViewModel(IMessengerService messengerService = null)
    {
        // 使用传入的messengerService或创建默认实例
        _messengerService = messengerService ?? new MessengerService();
        SendRequestCommand = new AsyncRelayCommand(SendRequestAsync);
        // 初始化URL历史记录ViewModel
        UrlHistory = new UrlHistoryViewModel();
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

            // 发送请求并获取响应内容
            // Flurl提供了流畅的API，一行代码即可完成请求
            string content = await url.GetStringAsync();
            ResponseContent = content;
            // 添加URL到历史记录
            UrlHistory.AddUrl(url);
            IsModified = true;
            // 发送成功消息到消息总线
            _messengerService.Send(new RequestResponseMessage(content, url, true));
        }
        catch (FlurlHttpException ex)
        {
            // 处理HTTP错误
            ResponseContent = $"请求失败: 状态码 {ex.StatusCode}, 错误信息: {await ex.GetResponseStringAsync()}";
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