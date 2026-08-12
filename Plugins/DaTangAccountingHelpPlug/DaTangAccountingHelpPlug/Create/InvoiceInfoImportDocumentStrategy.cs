using DaTangAccountingHelpPlug.Constants;
using DaTangAccountingHelpPlug.ViewModels;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace DaTangAccountingHelpPlug.Create;

public class InvoiceInfoImportDocumentStrategy: IDocumentCreationStrategy
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public InvoiceInfoImportDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory
                                ?? throw new ArgumentNullException(nameof(documentScopeFactory));
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        var welcomeDoc = _documentScopeFactory.CreateDocument<InvoiceInfoImportViewModel>();
        welcomeDoc.Title = string.IsNullOrEmpty(@params.Title) ? "发票信息导入和计算" : @params.Title;

        return welcomeDoc;
    }

    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata(
            SaveDocumentTypeIdConstant.InvoiceInfoImportDocument,
            "综合计算发票信息",
            [SaveDocumentTypeIdConstant.LegacyInvoiceInfoImportDocument])
        {
            Description = "依照发票表，当月明细，上月以及以前的综合计算 当月的综合表",
            MenuCategory = "大唐-会计"
        };
    }
}
