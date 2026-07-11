using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using BiliDownloader.Constants;
using BiliDownloader.ViewModels;

namespace BiliDownloader.Create;

public class BiliDownloaderDocumentStrategy : IDocumentCreationStrategy
{
    public Document CreateDocument(DocumentCreationParams @params)
    {
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
