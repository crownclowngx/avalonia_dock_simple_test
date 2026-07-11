using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Save;
using BiliDownloader.Constants;
using Newtonsoft.Json;

namespace BiliDownloader.ViewModels;

public class BiliDownloaderViewModel : Document, ISavableDocument
{
    public string SaveDocumentTypeId => SaveDocumentTypeIdConstant.BiliDownloaderDocumentId;
    public string FilePath { get; set; } = string.Empty;

    private string _url = "";
    private string _downloadInfo = "";
    private bool _isLoading = false;

    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    public string DownloadInfo
    {
        get => _downloadInfo;
        set => SetProperty(ref _downloadInfo, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public IRelayCommand DownloadCommand { get; }

    public BiliDownloaderViewModel()
    {
        DownloadCommand = new AsyncRelayCommand(DownloadAsync);
    }

    private async Task DownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            DownloadInfo = "请输入有效的B站视频链接";
            return;
        }

        try
        {
            IsLoading = true;
            DownloadInfo = "正在解析视频信息...";

            // TODO: 实现实际的B站视频解析逻辑
            await Task.Delay(500);

            DownloadInfo = $"已获取链接: {Url}\n解析功能开发中...";
            IsModified = true;
        }
        catch (Exception ex)
        {
            DownloadInfo = $"解析异常: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public DocumentSaveData CreateSaveDocumentMetaData(string filePath)
    {
        var saveDataObject = new
        {
            Url = _url,
            DownloadInfo = _downloadInfo
        };

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
                var viewModelData = JsonConvert.DeserializeObject<dynamic>(saveData.Content);
                if (viewModelData != null)
                {
                    _url = viewModelData.Url?.ToString() ?? "";
                    _downloadInfo = viewModelData.DownloadInfo?.ToString() ?? "";
                    OnPropertyChanged(nameof(Url));
                    OnPropertyChanged(nameof(DownloadInfo));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载文档错误: {ex.Message}");
        }
    }
}
