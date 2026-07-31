using DaTangAccountingHelpPlug.Constants;
using DaTangAccountingHelpPlug.ViewModels;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace DaTangAccountingHelpPlug.Create;

public class InvoiceInfoImportDocumentStrategy: IDocumentCreationStrategy
{
    private readonly IServiceProvider _serviceProvider;

    public InvoiceInfoImportDocumentStrategy(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Document CreateDocument(DocumentCreationParams @params)
    {
        // 策略由宿主长期保存；在用户创建文档时解析 Transient ViewModel，
        // 确保多个发票计算文档之间不共享可变状态。
        var welcomeDoc = _serviceProvider.GetRequiredService<InvoiceInfoImportViewModel>();
        welcomeDoc.Title = string.IsNullOrEmpty(@params.Title) ? "发票信息导入和计算" : @params.Title;

        return welcomeDoc;
    }

    public DocumentMetadata GetMetadata()
    {
        return new DocumentMetadata(SaveDocumentTypeIdConstant.InvoiceInfoImportDocument, "综合计算发票信息")
        {
            Description = "依照发票表，当月明细，上月以及以前的综合计算 当月的综合表",
            MenuCategory = "大唐-会计"
        };
    }
}
