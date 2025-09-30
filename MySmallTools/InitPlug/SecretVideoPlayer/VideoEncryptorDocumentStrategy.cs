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
    public Document CreateDocument(DocumentCreationParams @params)
    {
        var encryptorDoc = new VideoEncryptorViewModel()
        {
            Title = string.IsNullOrEmpty(@params.Title) ? "视频文件加密器" : @params.Title,
        };

        return encryptorDoc;
    }

    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata(DocumentTypeIdConstant.VideoEncryptorDocumentId, "视频文件加密器")
        {
            Description = "选择视频文件，输入密码进行AES-CTR加密，生成加密后的视频文件",
            MenuCategory = "视频工具"
        };
    }
}