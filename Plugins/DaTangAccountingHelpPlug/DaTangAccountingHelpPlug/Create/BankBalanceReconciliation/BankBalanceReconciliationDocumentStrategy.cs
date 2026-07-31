using DaTangAccountingHelpPlug.Constants;
using DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace DaTangAccountingHelpPlug.Create.BankBalanceReconciliation;

/// <summary>通过宿主 Scope 创建银行余额调节 Document。</summary>
public sealed class BankBalanceReconciliationDocumentStrategy : IDocumentCreationStrategy
{
    private readonly IDocumentScopeFactory _documentScopeFactory;

    public BankBalanceReconciliationDocumentStrategy(IDocumentScopeFactory documentScopeFactory)
    {
        _documentScopeFactory = documentScopeFactory
                                ?? throw new ArgumentNullException(nameof(documentScopeFactory));
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        // Document 及其运行状态必须由宿主创建的 Scope 托管。
        // 这样关闭一个对账标签页时，只会取消并释放该标签页的任务和文件句柄。
        var document = _documentScopeFactory.CreateDocument<BankBalanceReconciliationViewModel>();
        document.Title = string.IsNullOrWhiteSpace(@params.Title)
            ? "银行余额调节表"
            : @params.Title;
        return document;
    }

    public DocumentMetadata GetMetadata() =>
        new(SaveDocumentTypeIdConstant.BankBalanceReconciliationDocument, "银行余额调节表")
        {
            Description = "只读分析企业账与银行账，生成调节表、收付款明细和匹配审计",
            MenuCategory = "大唐-会计"
        };
}
