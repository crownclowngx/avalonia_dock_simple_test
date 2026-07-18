using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using BiliDownloader.Constants;
using BiliDownloader.Services.Auth;
using BiliDownloader.ViewModels;

namespace BiliDownloader.Create;

public class BiliDownloaderDocumentStrategy : IDocumentCreationStrategy
{
    private static bool _initTriggered;

    public Document CreateDocument(DocumentCreationParams @params)
    {
        // 首次创建 Document 时触发懒初始化（建表 + 加载历史 Cookie）
        if (!_initTriggered)
        {
            _initTriggered = true;
            // fire-and-forget：不阻塞创建流程，初始化完成后通过广播更新状态
            _ = BiliLoginStateService.Instance.InitAsync();
        }

        var doc = new BiliDownloaderViewModel
        {
            Title = string.IsNullOrEmpty(@params.Title) ? "Bilibili下载" : @params.Title,
        };

        return doc;
    }

    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata(SaveDocumentTypeIdConstant.BiliDownloaderDocumentId, "下载")
        {
            Description = "Bilibili视频下载器",
            MenuCategory = "Bilibili下载器"
        };
    }
}
