using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyPlugTest.Constants;
using MyPlugTest.ViewModels;

namespace MyPlugTest.Create;

/// <summary>创建根据 Excel 数据生成 GET 地址的临时文档。</summary>
public sealed class ExcelGetUrlGeneratorDocumentStrategy : IDocumentCreationStrategy
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public ExcelGetUrlGeneratorDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory
                                ?? throw new ArgumentNullException(nameof(documentScopeFactory));
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        var document = _documentScopeFactory.CreateDocument<ExcelGetUrlGeneratorViewModel>();
        document.Title = string.IsNullOrWhiteSpace(@params.Title)
            ? "Excel GET 地址生成器"
            : @params.Title;
        return document;
    }

    public DocumentMetadata GetMetadata() =>
        new(
            SaveDocumentTypeIdConstant.ExcelGetUrlGeneratorDocumentId,
            "Excel GET 地址生成器")
        {
            Description = "按 Excel 列映射批量生成 GET 请求地址",
            MenuCategory = "测试插件",
        };
}
