using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MySmallTools.Constants;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.InitPlug.SecretVideoPlayer;

public class SecretVideoDocumentStrategy: IDocumentCreationStrategy
{
    public Document CreateDocument(DocumentCreationParams @params)
    {
        var videoDoc = new SecretVideoPlayerViewModel()
        {
            Title = string.IsNullOrEmpty(@params.Title) ? "加密视频播放器" : @params.Title,
        };

        return videoDoc;
    }

    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata(DocumentTypeIdConstant.SecretVideoDocumentId, "加密视频播放器")
        {
            Description = "支持AES-CTR加密的视频播放器，保留头信息实时解密",
            MenuCategory = "视频工具"
        };
    }
}