using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MySmallTools.Constants;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.InitPlug.SecretVideoPlayer;

/// <summary>
/// 视频文件加密器文档创建策略
/// </summary>
public class VideoEncryptorDocumentStrategy : IDocumentCreationStrategy
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public VideoEncryptorDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory ?? throw new ArgumentNullException(nameof(documentScopeFactory));
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        var encryptorDoc = _documentScopeFactory.CreateDocument<VideoEncryptorViewModel>();
        encryptorDoc.Title = string.IsNullOrEmpty(@params.Title) ? "视频文件加密器" : @params.Title;

        return encryptorDoc;
    }

    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata(
            DocumentTypeIdConstant.VideoEncryptorDocumentId,
            "视频文件加密器",
            [DocumentTypeIdConstant.LegacyVideoEncryptorDocumentId])
        {
            Description = "使用 SECVID03/AES-256-GCM 分块加密视频，支持标题、描述和随机读取播放",
            MenuCategory = "视频工具"
        };
    }
}
