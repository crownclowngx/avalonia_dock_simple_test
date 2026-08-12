using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MySmallTools.Constants;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.InitPlug.SecretVideoPlayer;

public sealed class SecretVideoLibraryDocumentStrategy : IDocumentCreationStrategy
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public SecretVideoLibraryDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory ?? throw new ArgumentNullException(nameof(documentScopeFactory));
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        var document = _documentScopeFactory.CreateDocument<SecretVideoLibraryViewModel>();
        document.Title = string.IsNullOrEmpty(@params.Title) ? "加密视频库播放器" : @params.Title;
        return document;
    }

    public DocumentMetadata GetMetadata() =>
        new(
            DocumentTypeIdConstant.SecretVideoLibraryDocumentId,
            "加密视频库播放器",
            [DocumentTypeIdConstant.LegacySecretVideoLibraryDocumentId])
        {
            Description = "扫描文件夹中的 SECVID03 视频，支持公开信息搜索和公共密码播放",
            MenuCategory = "视频工具"
        };
}
