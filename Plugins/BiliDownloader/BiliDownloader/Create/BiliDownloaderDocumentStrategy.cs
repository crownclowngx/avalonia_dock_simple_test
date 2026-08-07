using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using BiliDownloader.Constants;
using BiliDownloader.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BiliDownloader.Create;

public class BiliDownloaderDocumentStrategy : IDocumentCreationStrategy, IDocumentCreationIntentProvider
{
    private readonly IServiceProvider _serviceProvider;

    public BiliDownloaderDocumentStrategy(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        if (!string.IsNullOrEmpty(@params.CreationIntentId)
            && @params.CreationIntentId is not ("quick-url" or "personal-source"))
            throw new ArgumentException("未知的 BiliDownloader 创建意图。", nameof(@params));

        // 每个 Document 保持独立 ViewModel，但其中的仓储、消息服务和 Coordinator
        // 均来自同一个插件级容器；创建 Document 不再承担插件初始化职责。
        var doc = _serviceProvider.GetRequiredService<BiliDownloaderViewModel>();
        doc.Title = string.IsNullOrEmpty(@params.Title) ? "Bilibili下载" : @params.Title;
        doc.ApplyCreationIntent(@params.CreationIntentId);

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

    public IReadOnlyList<DocumentCreationIntentMetadata> GetCreationIntents() =>
    [
        new("quick-url", "链接下载") { Description = "粘贴视频、番剧或短链接并创建下载计划。" },
        new("personal-source", "个人内容来源") { Description = "浏览 UP 主投稿、收藏夹、稍后再看和历史记录。" },
    ];
}
