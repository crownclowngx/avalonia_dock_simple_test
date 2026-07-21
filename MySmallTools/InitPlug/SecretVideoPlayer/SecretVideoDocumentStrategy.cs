using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MySmallTools.Constants;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.InitPlug.SecretVideoPlayer;

public class SecretVideoDocumentStrategy: IDocumentCreationStrategy
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public SecretVideoDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory ?? throw new ArgumentNullException(nameof(documentScopeFactory));
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        var videoDoc = _documentScopeFactory.CreateDocument<SecretVideoPlayerViewModel>();
        videoDoc.Title = string.IsNullOrEmpty(@params.Title) ? "加密视频播放器" : @params.Title;

        return videoDoc;
    }

    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata(DocumentTypeIdConstant.SecretVideoDocumentId, "加密视频播放器")
        {
            Description = "支持 SECVID03/AES-256-GCM 认证分块和随机读取的加密视频播放器",
            MenuCategory = "视频工具"
        };
    }
}
