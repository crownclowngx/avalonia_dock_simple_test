using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using BiliDownloader.Constants;
using BiliDownloader.ViewModels;

namespace BiliDownloader.Create;

public class BiliDownloaderDocumentStrategy : IDocumentCreationStrategy, IDocumentCreationIntentProvider
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public BiliDownloaderDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory
                                ?? throw new ArgumentNullException(nameof(documentScopeFactory));
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        if (@params.CreationIntentId is { Value: not ("quick-url" or "personal-source") })
            throw new ArgumentException("未知的 BiliDownloader 创建意图。", nameof(@params));

        // 每个 Document 由宿主创建独立 Scope；仓储、消息服务和 Coordinator 仍复用
        // 插件级单例，创建与关闭 Document 不承担插件级后台任务的生命周期职责。
        var doc = _documentScopeFactory.CreateDocument<BiliDownloaderViewModel>();
        doc.Title = string.IsNullOrEmpty(@params.Title) ? "Bilibili下载" : @params.Title;
        doc.ApplyCreationIntent(@params.CreationIntentId);

        return doc;
    }

    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata(
            SaveDocumentTypeIdConstant.BiliDownloaderDocumentId,
            "下载",
            [SaveDocumentTypeIdConstant.LegacyBiliDownloaderDocumentId])
        {
            Description = "Bilibili视频下载器",
            MenuCategory = "Bilibili下载器"
        };
    }

    public IReadOnlyList<DocumentCreationIntentMetadata> GetCreationIntents() =>
    [
        new(new CreationIntentId("quick-url"), "链接下载") { Description = "粘贴视频、番剧或短链接并创建下载计划。" },
        new(new CreationIntentId("personal-source"), "个人内容来源") { Description = "浏览 UP 主投稿、收藏夹、稍后再看和历史记录。" },
    ];
}
