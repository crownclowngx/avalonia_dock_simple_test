using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyPlugTest.Constants;
using MyPlugTest.ViewModels;

namespace MyPlugTest.Create;

/// <summary>
/// 创建逐行 HTTP GET 临时文档。
/// </summary>
public sealed class BatchHttpGetDocumentStrategy : IDocumentCreationStrategy
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public BatchHttpGetDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory
                                ?? throw new ArgumentNullException(nameof(documentScopeFactory));
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        var document = _documentScopeFactory.CreateDocument<BatchHttpGetViewModel>();
        document.Title = string.IsNullOrWhiteSpace(@params.Title)
            ? "逐行 HTTP GET"
            : @params.Title;
        return document;
    }

    public DocumentMetadata GetMetadata() =>
        new(SaveDocumentTypeIdConstant.BatchHttpGetDocumentId, "逐行 HTTP GET")
        {
            Description = "将多行网址按输入顺序逐个执行 GET 请求",
            MenuCategory = "测试插件",
        };
}
