using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MySmallTools.Constants;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.InitPlug.SecretVideoPlayer;

public sealed class VideoDecryptorDocumentStrategy : IDocumentCreationStrategy
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public VideoDecryptorDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory ?? throw new ArgumentNullException(nameof(documentScopeFactory));
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        var document = _documentScopeFactory.CreateDocument<VideoDecryptorViewModel>();
        document.Title = string.IsNullOrEmpty(@params.Title) ? "批量视频解密器" : @params.Title;
        return document;
    }

    public DocumentMetadata GetMetadata() =>
        new(
            DocumentTypeIdConstant.VideoDecryptorDocumentId,
            "批量视频解密器",
            [DocumentTypeIdConstant.LegacyVideoDecryptorDocumentId])
        {
            Description = "使用一个公共密码批量解密 SECVID03 视频，并安全导出原始文件",
            MenuCategory = "视频工具"
        };
}
